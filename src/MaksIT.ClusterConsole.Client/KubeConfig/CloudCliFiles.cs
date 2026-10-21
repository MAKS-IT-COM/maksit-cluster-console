using System.Net;
using System.Text;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Bcpg.OpenPgp;
using MaksIT.Results;



namespace MaksIT.ClusterConsole.Client.KubeConfig;

internal static class CloudCliFiles {
  public const int MaxBytes = 250 * 1024 * 1024;

  public static async Task<Result<string>> DownloadAsync(
    HttpClient http,
    string url,
    string destination,
    CancellationToken cancellationToken) {
    using var response = await GetAsync(http, url, cancellationToken).ConfigureAwait(false);

    if (response.StatusCode != HttpStatusCode.OK)
      return Result<string>.BadRequest(null, "Download failed (" + (int)response.StatusCode + ").");

    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    await using var output = File.Create(destination);
    var buffer = new byte[81920];
    long copied = 0;

    while (true) {
      var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

      if (read == 0)
        break;

      copied += read;

      if (copied > MaxBytes)
        return Result<string>.BadRequest(null, "The download is larger than 250 MB.");

      await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
    }

    return Result<string>.Ok(destination);
  }

  public static async Task<Result<string>> DownloadTextAsync(HttpClient http, string url, CancellationToken cancellationToken) {
    var path = Path.Combine(Path.GetTempPath(), "maksit-cloud-" + Guid.NewGuid().ToString("N"));
    var downloaded = await DownloadAsync(http, url, path, cancellationToken).ConfigureAwait(false);

    if (!downloaded.IsSuccess)
      return downloaded;

    try {
      return Result<string>.Ok(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
    }
    finally {
      File.Delete(path);
    }
  }

  public static async Task<string?> HeadTokenAsync(HttpClient http, string url, CancellationToken cancellationToken) {
    for (var hop = 0; hop < 5; hop++) {
      using var request = new HttpRequestMessage(HttpMethod.Head, url);
      using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      var status = (int)response.StatusCode;

      if (status is 301 or 302 or 303 or 307 or 308) {
        var next = response.Headers.Location;

        if (next is null)
          return null;

        if (!next.IsAbsoluteUri)
          next = new Uri(new Uri(url), next);

        if (next.Scheme != Uri.UriSchemeHttps)
          return null;

        url = next.AbsoluteUri;

        continue;
      }

      if (response.Headers.ETag is { } tag && tag.Tag.Length > 0)
        return tag.Tag;

      if (response.Content.Headers.ContentLength is > 0)
        return "len:" + response.Content.Headers.ContentLength.Value;

      return null;
    }

    return null;
  }

  public static async Task<Result<string>> LatestGitHubTagAsync(
    HttpClient http,
    string owner,
    string repo,
    CancellationToken cancellationToken) {
    var url = "https://github.com/" + owner + "/" + repo + "/releases/latest";
    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    var status = (int)response.StatusCode;

    if (status is not (301 or 302 or 303 or 307 or 308))
      return Result<string>.BadRequest(null, "Could not read the latest " + repo + " release.");

    var next = response.Headers.Location;

    if (next is null)
      return Result<string>.BadRequest(null, "Could not read the latest " + repo + " release.");

    if (!next.IsAbsoluteUri)
      next = new Uri(new Uri(url), next);

    var parts = next.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var index = Array.IndexOf(parts, "tag");

    if (index < 0 || index + 1 >= parts.Length)
      return Result<string>.BadRequest(null, "Could not read the latest " + repo + " release.");

    return Result<string>.Ok(Uri.UnescapeDataString(parts[index + 1]));
  }

  public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken) {
    using var sha = SHA256.Create();
    await using var stream = File.OpenRead(path);
    var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);

    return Convert.ToHexString(hash);
  }

  public static string? HashFor(string checksumText, string assetName) {
    string? only = null;
    var hexLines = 0;

    foreach (var raw in checksumText.Split('\n', '\r')) {
      var line = raw.Trim();

      if (line.Length == 0 || line.StartsWith('#'))
        continue;

      var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

      if (parts.Length == 1 && IsHex(parts[0])) {
        hexLines++;
        only = parts[0];

        continue;
      }

      if (parts.Length < 2 || !IsHex(parts[0]))
        continue;

      var name = Path.GetFileName(parts[^1].TrimStart('*').Replace('\\', '/'));

      if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
        return parts[0];
    }

    return hexLines == 1 ? only : null;
  }

  public static string? HashNear(string text, string fileName) {
    var index = text.IndexOf(fileName, StringComparison.Ordinal);

    if (index < 0)
      return null;

    var start = Math.Max(0, index - 800);
    var length = Math.Min(2300, text.Length - start);
    var match = Regex.Match(text.Substring(start, length), "[a-fA-F0-9]{64}");

    return match.Success ? match.Value : null;
  }

  public static void ExtractNamed(string archivePath, string fileName, string destinationFile) {
    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

    if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
      ExtractZipNamed(archivePath, fileName, destinationFile);

      return;
    }

    ExtractTarNamed(archivePath, fileName, destinationFile);
  }

  public static void ExtractAll(string archivePath, string destinationDirectory) {
    Directory.CreateDirectory(destinationDirectory);

    if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
      using var stream = File.OpenRead(archivePath);
      using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

      foreach (var entry in zip.Entries) {
        if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
          continue;

        if (!SafeName(entry.FullName))
          throw new InvalidDataException("The archive has an unsafe path.");

        var target = Path.Combine(destinationDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, overwrite: true);
      }

      return;
    }

    using var file = File.OpenRead(archivePath);
    using var gzip = new GZipStream(file, CompressionMode.Decompress);
    using var tar = new TarReader(gzip);

    while (tar.GetNextEntry() is { } entry) {
      if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
        continue;

      if (!SafeName(entry.Name))
        throw new InvalidDataException("The archive has an unsafe path.");

      if (entry.DataStream is null)
        continue;

      var target = Path.Combine(destinationDirectory, entry.Name.Replace('/', Path.DirectorySeparatorChar));
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      using var output = File.Create(target);
      entry.DataStream.CopyTo(output);
    }
  }

  public static string? FindNamed(string root, string fileName) {
    if (!Directory.Exists(root))
      return null;

    return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
      .OrderBy(path => path.Length)
      .FirstOrDefault();
  }

  public static bool SignatureMatches(string dataPath, byte[] signature) {
    using var keyStream = new MemoryStream(Encoding.ASCII.GetBytes(AwsCliPublicKey.Armored));
    using var keyDecoded = PgpUtilities.GetDecoderStream(keyStream);
    var rings = new PgpPublicKeyRingBundle(keyDecoded);
    using var sigStream = new MemoryStream(signature);
    using var sigDecoded = PgpUtilities.GetDecoderStream(sigStream);
    var factory = new PgpObjectFactory(sigDecoded);
    var list = SignatureList(factory.NextPgpObject());

    if (list is null)
      return false;

    var sig = list[0];
    var key = rings.GetPublicKey(sig.KeyId);

    if (key is null)
      return false;

    sig.InitVerify(key);
    var buffer = new byte[81920];
    using var data = File.OpenRead(dataPath);
    int read;

    while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
      sig.Update(buffer, 0, read);

    return sig.Verify();
  }

  public static string MasterKeyId() {
    using var keyStream = new MemoryStream(Encoding.ASCII.GetBytes(AwsCliPublicKey.Armored));
    using var keyDecoded = PgpUtilities.GetDecoderStream(keyStream);
    var rings = new PgpPublicKeyRingBundle(keyDecoded);

    foreach (PgpPublicKeyRing ring in rings.GetKeyRings()) {
      foreach (PgpPublicKey key in ring.GetPublicKeys()) {
        if (key.IsMasterKey)
          return key.KeyId.ToString("X16");
      }
    }

    return "";
  }

  private static PgpSignatureList? SignatureList(PgpObject? obj) {
    if (obj is PgpSignatureList list)
      return list;

    if (obj is not PgpCompressedData compressed)
      return null;

    var inner = new PgpObjectFactory(compressed.GetDataStream());

    return inner.NextPgpObject() as PgpSignatureList;
  }

  private static void ExtractZipNamed(string archivePath, string fileName, string destinationFile) {
    using var stream = File.OpenRead(archivePath);
    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
    var entry = zip.Entries
      .Where(item => SafeName(item.FullName))
      .Where(item => string.Equals(Path.GetFileName(item.FullName.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase))
      .OrderBy(item => item.FullName.Length)
      .FirstOrDefault();

    if (entry is null)
      throw new InvalidDataException("The archive does not contain " + fileName + ".");

    entry.ExtractToFile(destinationFile, overwrite: true);
  }

  private static void ExtractTarNamed(string archivePath, string fileName, string destinationFile) {
    using var stream = File.OpenRead(archivePath);
    using var gzip = new GZipStream(stream, CompressionMode.Decompress);
    using var tar = new TarReader(gzip);

    while (tar.GetNextEntry() is { } entry) {
      if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
        continue;

      if (!SafeName(entry.Name))
        throw new InvalidDataException("The archive has an unsafe path.");

      if (entry.DataStream is null)
        continue;

      if (!string.Equals(Path.GetFileName(entry.Name.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase))
        continue;

      using var output = File.Create(destinationFile);
      entry.DataStream.CopyTo(output);

      return;
    }

    throw new InvalidDataException("The archive does not contain " + fileName + ".");
  }

  private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string url, CancellationToken cancellationToken) {
    for (var hop = 0; hop < 5; hop++) {
      var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      var status = (int)response.StatusCode;

      if (status is not (301 or 302 or 303 or 307 or 308))
        return response;

      var next = response.Headers.Location;
      response.Dispose();

      if (next is null)
        return new HttpResponseMessage(HttpStatusCode.BadGateway);

      if (!next.IsAbsoluteUri)
        next = new Uri(new Uri(url), next);

      if (next.Scheme != Uri.UriSchemeHttps)
        return new HttpResponseMessage(HttpStatusCode.BadGateway);

      url = next.AbsoluteUri;
    }

    return new HttpResponseMessage(HttpStatusCode.BadGateway);
  }

  internal static bool SafeName(string name) {
    var normalized = name.Replace('\\', '/');

    if (normalized.StartsWith('/') || normalized.Contains("..", StringComparison.Ordinal))
      return false;

    return true;
  }

  private static bool IsHex(string value) =>
    value.Length == 64 && value.All(Uri.IsHexDigit);
}
