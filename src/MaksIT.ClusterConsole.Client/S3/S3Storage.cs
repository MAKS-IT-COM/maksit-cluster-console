using System.Net;
using System.Text;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Amazon;
using Amazon.S3;
using Amazon.Runtime;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client.S3;

/// <summary>
/// Lists every bucket on an endpoint, then lists and changes objects in the open bucket.
/// The client matches a path-style S3 endpoint: checksums are sent only when the operation requires them.
/// </summary>
public static class S3Storage {
  public static Task<Result<IReadOnlyList<S3Bucket>>> ListBucketsAsync(
    S3Target target,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      var buckets = new List<S3Bucket>();
      string? continuation = null;
      var pages = 0;

      do {
        var page = await client.ListBucketsAsync(new ListBucketsRequest {
          ContinuationToken = continuation
        }, token).ConfigureAwait(false);

        if (page.Buckets is not null) {
          foreach (var bucket in page.Buckets) {
            if (string.IsNullOrWhiteSpace(bucket.BucketName))
              continue;

            DateTimeOffset? created = bucket.CreationDate is { } when
              ? new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc))
              : null;
            buckets.Add(new S3Bucket(bucket.BucketName, created));
          }
        }

        var next = page.ContinuationToken;
        pages++;

        if (string.IsNullOrEmpty(next) || next == continuation || pages >= 50)
          break;

        continuation = next;
      }
      while (true);

      return Result<IReadOnlyList<S3Bucket>>.Ok(buckets);
    }, cancellationToken);

  public static Task<Result> CreateBucketAsync(
    S3Target target,
    string name,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      var bucket = name.Trim();

      if (!S3Layout.IsBucket(bucket))
        return Result.BadRequest("Enter a bucket name with 3 to 63 lowercase letters, numbers, dots, or hyphens.");

      var request = new PutBucketRequest {
        BucketName = bucket
      };
      var custom = S3Layout.FirstEndpoint(target.Endpoint).Length > 0;

      if (!custom
          && !string.IsNullOrWhiteSpace(target.Region)
          && !target.Region.Equals("us-east-1", StringComparison.OrdinalIgnoreCase))
        request.BucketRegionName = target.Region;

      await client.PutBucketAsync(request, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result> DeleteBucketAsync(
    S3Target target,
    string name,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      var bucket = name.Trim();
      var cleared = await DeleteObjectsAsync(client, bucket, "", progress, token).ConfigureAwait(false);

      if (!cleared.IsSuccess)
        return Refuse(cleared);

      progress?.Invoke("Deleting bucket " + bucket);
      await client.DeleteBucketAsync(bucket, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result<IReadOnlyList<S3Entry>>> ListAsync(
    S3Target target,
    string? prefix,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      var root = S3Layout.NormalizePrefix(prefix);
      var folders = new List<string>();
      var objects = new List<S3ObjectInfo>();
      string? continuation = null;

      do {
        var page = await client.ListObjectsV2Async(new ListObjectsV2Request {
          BucketName = target.Bucket,
          Prefix = root,
          Delimiter = "/",
          ContinuationToken = continuation
        }, token).ConfigureAwait(false);

        if (page.CommonPrefixes is not null)
          folders.AddRange(page.CommonPrefixes);

        if (page.S3Objects is not null) {
          foreach (var item in page.S3Objects)
            objects.Add(Info(item));
        }

        continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
      }
      while (!string.IsNullOrEmpty(continuation));

      return Result<IReadOnlyList<S3Entry>>.Ok(S3Layout.List(root, folders, objects));
    }, cancellationToken);

  public static Task<Result<S3TextFile>> ReadTextAsync(
    S3Target target,
    string key,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      using var response = await client.GetObjectAsync(new GetObjectRequest {
        BucketName = target.Bucket,
        Key = key,
        ByteRange = new ByteRange(0, S3Layout.PreviewLimit - 1)
      }, token).ConfigureAwait(false);
      await using var stream = response.ResponseStream;
      var buffer = new byte[S3Layout.PreviewLimit];
      var read = 0;

      while (read < buffer.Length) {
        var got = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), token).ConfigureAwait(false);

        if (got == 0)
          break;

        read += got;
      }

      var binary = buffer.AsSpan(0, read).Contains((byte)0);

      return Result<S3TextFile>.Ok(new S3TextFile(binary ? "" : Encoding.UTF8.GetString(buffer, 0, read), binary));
    }, cancellationToken);

  public static Task<Result> WriteTextAsync(
    S3Target target,
    string key,
    string text,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      using var body = new MemoryStream(Encoding.UTF8.GetBytes(text));
      await client.PutObjectAsync(new PutObjectRequest {
        BucketName = target.Bucket,
        Key = key,
        InputStream = body
      }, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result> CreateFolderAsync(
    S3Target target,
    string prefix,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      using var empty = new MemoryStream([]);
      await client.PutObjectAsync(new PutObjectRequest {
        BucketName = target.Bucket,
        Key = S3Layout.NormalizePrefix(prefix),
        InputStream = empty
      }, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result> DeleteObjectAsync(
    S3Target target,
    string key,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      await client.DeleteObjectAsync(target.Bucket, key, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result<int>> DeletePrefixAsync(
    S3Target target,
    string prefix,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Guard(target, (client, token) => DeleteObjectsAsync(client, target.Bucket, S3Layout.NormalizePrefix(prefix), progress, token), cancellationToken);

  public static Task<Result> UploadAsync(
    S3Target target,
    string key,
    string path,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Transfer(target, async (transfer, token) => {
      var request = new TransferUtilityUploadRequest {
        BucketName = target.Bucket,
        Key = key,
        FilePath = path
      };
      request.UploadProgressEvent += (_, args) => progress?.Invoke("Uploading " + Leaf(key) + " · " + args.PercentDone + "%");
      await transfer.UploadAsync(request, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result> DownloadAsync(
    S3Target target,
    string key,
    string path,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Transfer(target, async (transfer, token) => {
      var directory = Path.GetDirectoryName(path);

      if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

      var request = new TransferUtilityDownloadRequest {
        BucketName = target.Bucket,
        Key = key,
        FilePath = path
      };
      request.WriteObjectProgressEvent += (_, args) => progress?.Invoke("Downloading " + Leaf(key) + " · " + args.PercentDone + "%");
      await transfer.DownloadAsync(request, token).ConfigureAwait(false);

      return Result.Ok();
    }, cancellationToken);

  public static Task<Result<int>> DownloadPrefixAsync(
    S3Target target,
    string prefix,
    string directory,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      var root = S3Layout.NormalizePrefix(prefix);
      var listed = await ListKeysAsync(client, target.Bucket, root, token).ConfigureAwait(false);

      if (!listed.IsSuccess || listed.Value is null)
        return new Result<int>(0, false, new List<string>(listed.Messages), listed.StatusCode);

      var files = listed.Value.Where(item => !item.Key.EndsWith('/')).ToList();
      using var transfer = new TransferUtility(client);

      for (var index = 0; index < files.Count; index++) {
        var item = files[index];
        var relative = item.Key[root.Length..];
        var path = LocalPath(directory, relative);
        var folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
          Directory.CreateDirectory(folder);

        progress?.Invoke("Downloading " + (index + 1) + " of " + files.Count + " · " + Leaf(item.Key));
        await transfer.DownloadAsync(path, target.Bucket, item.Key, token).ConfigureAwait(false);
      }

      return Result<int>.Ok(files.Count);
    }, cancellationToken);

  public static Task<Result> CopyObjectAsync(
    S3Target target,
    string sourceKey,
    string destinationKey,
    long size,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Guard(target, (client, token) => CopyOneAsync(client, target.Bucket, sourceKey, destinationKey, size, progress, token), cancellationToken);

  public static Task<Result<int>> CopyPrefixAsync(
    S3Target target,
    string sourcePrefix,
    string destinationPrefix,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default) =>
    Guard(target, async (client, token) => {
      var source = S3Layout.NormalizePrefix(sourcePrefix);
      var destination = S3Layout.NormalizePrefix(destinationPrefix);

      if (source == destination)
        return Result<int>.BadRequest(0, "Choose a different folder.");

      if (S3Layout.ContainsPrefix(source, destination))
        return Result<int>.BadRequest(0, "The new folder is inside the folder being copied.");

      var listed = await ListKeysAsync(client, target.Bucket, source, token).ConfigureAwait(false);

      if (!listed.IsSuccess || listed.Value is null)
        return new Result<int>(0, false, new List<string>(listed.Messages), listed.StatusCode);

      var copied = 0;

      foreach (var item in listed.Value) {
        if (!item.Key.StartsWith(source, StringComparison.Ordinal))
          continue;

        var destinationKey = destination + item.Key[source.Length..];
        progress?.Invoke("Copying " + (copied + 1) + " of " + listed.Value.Count + " · " + Leaf(item.Key));
        var done = await CopyOneAsync(client, target.Bucket, item.Key, destinationKey, item.Size, null, token).ConfigureAwait(false);

        if (!done.IsSuccess)
          return new Result<int>(copied, false, new List<string>(done.Messages), done.StatusCode);

        copied++;
      }

      return Result<int>.Ok(copied);
    }, cancellationToken);

  private static async Task<Result> CopyOneAsync(
    AmazonS3Client client,
    string bucket,
    string sourceKey,
    string destinationKey,
    long size,
    Action<string>? progress,
    CancellationToken cancellationToken) {
    if (sourceKey == destinationKey)
      return Result.BadRequest("Choose a different name.");

    if (size >= S3Layout.CopyLimit)
      return await CopyLargeAsync(client, bucket, sourceKey, destinationKey, size, progress, cancellationToken).ConfigureAwait(false);

    progress?.Invoke("Copying " + Leaf(sourceKey));
    await client.CopyObjectAsync(new CopyObjectRequest {
      SourceBucket = bucket,
      SourceKey = sourceKey,
      DestinationBucket = bucket,
      DestinationKey = destinationKey,
      MetadataDirective = S3MetadataDirective.COPY
    }, cancellationToken).ConfigureAwait(false);

    return Result.Ok();
  }

  private static async Task<Result> CopyLargeAsync(
    AmazonS3Client client,
    string bucket,
    string sourceKey,
    string destinationKey,
    long size,
    Action<string>? progress,
    CancellationToken cancellationToken) {
    var started = await client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest {
      BucketName = bucket,
      Key = destinationKey
    }, cancellationToken).ConfigureAwait(false);
    var uploadId = started.UploadId;

    try {
      var partSize = S3Layout.PartSize(size);
      var tags = new List<PartETag>();
      var part = 1;
      long offset = 0;

      while (offset < size) {
        var last = Math.Min(offset + partSize, size) - 1;
        progress?.Invoke("Copying " + Leaf(sourceKey) + " · part " + part);
        var copied = await client.CopyPartAsync(new CopyPartRequest {
          SourceBucket = bucket,
          SourceKey = sourceKey,
          DestinationBucket = bucket,
          DestinationKey = destinationKey,
          UploadId = uploadId,
          PartNumber = part,
          FirstByte = offset,
          LastByte = last
        }, cancellationToken).ConfigureAwait(false);
        tags.Add(new PartETag(copied));
        offset = last + 1;
        part++;
      }

      await client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest {
        BucketName = bucket,
        Key = destinationKey,
        UploadId = uploadId,
        PartETags = tags
      }, cancellationToken).ConfigureAwait(false);

      return Result.Ok();
    }
    catch {
      try {
        await client.AbortMultipartUploadAsync(bucket, destinationKey, uploadId, CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception) {
        // The copy error is the one to report.
      }

      throw;
    }
  }

  private static async Task<Result<int>> DeleteObjectsAsync(
    AmazonS3Client client,
    string bucket,
    string prefix,
    Action<string>? progress,
    CancellationToken cancellationToken) {
    var deleted = 0;
    string? continuation = null;

    do {
      var page = await client.ListObjectsV2Async(new ListObjectsV2Request {
        BucketName = bucket,
        Prefix = prefix,
        ContinuationToken = continuation
      }, cancellationToken).ConfigureAwait(false);
      var keys = page.S3Objects?.Select(item => item.Key).Where(key => !string.IsNullOrEmpty(key)).ToList() ?? [];

      if (keys.Count > 0) {
        progress?.Invoke("Deleting " + keys.Count + " objects…");
        var removed = await DeleteKeysAsync(client, bucket, keys, cancellationToken).ConfigureAwait(false);

        if (!removed.IsSuccess)
          return new Result<int>(deleted, false, new List<string>(removed.Messages), removed.StatusCode);

        deleted += keys.Count;
      }

      continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
    }
    while (!string.IsNullOrEmpty(continuation));

    return Result<int>.Ok(deleted);
  }

  private static async Task<Result<IReadOnlyList<S3ObjectInfo>>> ListKeysAsync(
    AmazonS3Client client,
    string bucket,
    string prefix,
    CancellationToken cancellationToken) {
    var objects = new List<S3ObjectInfo>();
    string? continuation = null;

    do {
      var page = await client.ListObjectsV2Async(new ListObjectsV2Request {
        BucketName = bucket,
        Prefix = prefix,
        ContinuationToken = continuation
      }, cancellationToken).ConfigureAwait(false);

      if (page.S3Objects is not null) {
        foreach (var item in page.S3Objects)
          objects.Add(Info(item));
      }

      continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
    }
    while (!string.IsNullOrEmpty(continuation));

    return Result<IReadOnlyList<S3ObjectInfo>>.Ok(objects);
  }

  private static async Task<Result> DeleteKeysAsync(
    AmazonS3Client client,
    string bucket,
    IReadOnlyList<string> keys,
    CancellationToken cancellationToken) {
    var request = new DeleteObjectsRequest {
      BucketName = bucket,
      Quiet = true
    };

    foreach (var key in keys)
      request.AddKey(key);

    var response = await client.DeleteObjectsAsync(request, cancellationToken).ConfigureAwait(false);
    var error = response.DeleteErrors?.FirstOrDefault();

    if (error is not null)
      return Result.BadRequest(error.Key + ": " + error.Message);

    return Result.Ok();
  }

  private static async Task<Result> Guard(
    S3Target target,
    Func<AmazonS3Client, CancellationToken, Task<Result>> work,
    CancellationToken cancellationToken) {
    try {
      using var client = Create(target);

      return await work(client, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return Fail(ex);
    }
  }

  private static async Task<Result<T>> Guard<T>(
    S3Target target,
    Func<AmazonS3Client, CancellationToken, Task<Result<T>>> work,
    CancellationToken cancellationToken) {
    try {
      using var client = Create(target);

      return await work(client, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return Fail<T>(ex);
    }
  }

  private static async Task<Result> Transfer(
    S3Target target,
    Func<TransferUtility, CancellationToken, Task<Result>> work,
    CancellationToken cancellationToken) {
    try {
      using var client = Create(target);
      using var transfer = new TransferUtility(client);

      return await work(transfer, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return Fail(ex);
    }
  }

  private static AmazonS3Client Create(S3Target target) {
    var endpoint = S3Layout.FirstEndpoint(target.Endpoint);

    if (endpoint.Length > 0
        && !endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("The endpoint must start with http:// or https://.");

    if (string.IsNullOrWhiteSpace(target.AccessKey) || string.IsNullOrWhiteSpace(target.SecretKey))
      throw new InvalidOperationException("This target has no access key or secret. Edit the target and save both.");

    var config = new AmazonS3Config {
      ForcePathStyle = target.PathStyle,
      Timeout = TimeSpan.FromMinutes(5),
      MaxErrorRetry = 2,
      RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
      ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
    };

    if (endpoint.Length > 0) {
      config.ServiceURL = endpoint;
      config.AuthenticationRegion = string.IsNullOrWhiteSpace(target.Region) ? "us-east-1" : target.Region;
    }
    else {
      config.RegionEndpoint = RegionEndpoint.GetBySystemName(target.Region);
    }

    if (!string.IsNullOrWhiteSpace(target.Certificate)) {
      try {
        config.HttpClientFactory = new PinnedCertificateFactory(target.Certificate);
      }
      catch (Exception ex) {
        throw new InvalidOperationException("The certificate could not be read. Paste the PEM text. " + ex.Message);
      }
    }

    return new AmazonS3Client(new BasicAWSCredentials(target.AccessKey, target.SecretKey), config);
  }

  private static S3ObjectInfo Info(S3Object item) =>
    new(item.Key, item.Size ?? 0, item.LastModified is { } modified ? new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc)) : null);

  private static string Leaf(string key) {
    var trimmed = key.TrimEnd('/');
    var slash = trimmed.LastIndexOf('/');

    return slash < 0 ? trimmed : trimmed[(slash + 1)..];
  }

  private static string LocalPath(string directory, string relative) {
    var path = directory;

    foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
      path = Path.Combine(path, S3Layout.SafeSegment(part));

    return path;
  }

  private static Result Refuse<T>(Result<T> result) {
    var message = result.Messages.Count == 0 ? "The request failed." : string.Join("; ", result.Messages);

    if (result.StatusCode == HttpStatusCode.NotFound)
      return Result.NotFound(message);

    if (result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
      return Result.Forbidden(message);

    if ((int)result.StatusCode >= 500)
      return Result.InternalServerError(message);

    return Result.BadRequest(message);
  }

  private static Result Fail(Exception ex) {
    if (ex is InvalidOperationException invalid)
      return Result.BadRequest(invalid.Message);

    if (ex is AmazonS3Exception s3) {
      var message = string.IsNullOrWhiteSpace(s3.Message) ? "S3 refused the request." : s3.Message;

      if (s3.StatusCode == HttpStatusCode.NotFound)
        return Result.NotFound(message);

      if (s3.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        return Result.Forbidden(message);

      if ((int)s3.StatusCode >= 500)
        return Result.InternalServerError(message);

      return Result.BadRequest(message);
    }

    if (ex is AmazonClientException client)
      return Result.BadRequest(string.IsNullOrWhiteSpace(client.Message) ? "S3 could not be reached." : client.Message);

    return Result.InternalServerError(ex.Message);
  }

  private static Result<T> Fail<T>(Exception ex) {
    var failed = Fail(ex);

    return new Result<T>(default, false, new List<string>(failed.Messages), failed.StatusCode);
  }

  private sealed class PinnedCertificateFactory : HttpClientFactory {
    private readonly X509Certificate2Collection _extra = [];

    public PinnedCertificateFactory(string pem) =>
      _extra.ImportFromPem(pem);

    public override string GetConfigUniqueString(IClientConfig clientConfig) =>
      _extra.Count == 0 ? "none" : _extra[0].Thumbprint;

    public override HttpClient CreateHttpClient(IClientConfig clientConfig) {
      var handler = new HttpClientHandler {
        ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) => Trusts(certificate, chain, errors)
      };

      return new HttpClient(handler);
    }

    private bool Trusts(X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors) {
      if (errors == SslPolicyErrors.None)
        return true;

      if (certificate is null)
        return false;

      foreach (var extra in _extra) {
        if (string.Equals(certificate.GetCertHashString(), extra.GetCertHashString(), StringComparison.OrdinalIgnoreCase))
          return true;
      }

      chain ??= new X509Chain();
      chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
      chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
      chain.ChainPolicy.CustomTrustStore.AddRange(_extra);

      return chain.Build(certificate);
    }
  }
}
