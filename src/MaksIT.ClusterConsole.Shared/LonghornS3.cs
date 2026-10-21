using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.Client.S3;


namespace MaksIT.ClusterConsole.Shared;

/// <summary>
/// Longhorn backup targets that point at S3, and the credential secret beside each one.
/// </summary>
public static class LonghornS3 {
  public const string DefaultPoll = "5m0s";

  public static readonly ResourceRef TargetRef = new("longhorn.io", "v1beta2", "backuptargets", "BackupTarget", true);

  public static readonly ResourceRef SecretRef = new("", "v1", "secrets", "Secret", true);

  public static string SecretNameFor(string targetName) =>
    targetName == "default" ? "longhorn-backup-target" : "longhorn-backup-target-" + targetName;

  public static async Task<Result<S3TargetSet>> LoadAsync(IClusterSession session, CancellationToken cancellationToken = default) {
    var listed = await session.ListAsync(TargetRef, Configuration.AllNamespaces, cancellationToken).ConfigureAwait(false);

    if (!listed.IsSuccess || listed.Value is null)
      return new Result<S3TargetSet>(null, false, new List<string>(listed.Messages), listed.StatusCode);

    var targets = new List<S3Target>();
    var other = 0;
    var warnings = new List<string>();

    foreach (var item in listed.Value) {
      var url = JsonPath.Text(item["spec"]?["backupTargetURL"]);

      if (url.Length > 0 && !url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase)) {
        other++;

        continue;
      }

      var name = JsonPath.Name(item);
      var ns = JsonPath.Namespace(item) ?? "longhorn-system";
      var credential = JsonPath.Text(item["spec"]?["credentialSecret"]);

      if (credential.Length == 0)
        credential = SecretNameFor(name);

      JsonObject? secret = null;

      if (credential.Length > 0) {
        var got = await session.GetAsync(SecretRef, credential, ns, cancellationToken).ConfigureAwait(false);

        if (got.IsSuccess)
          secret = got.Value;
        else if (got.StatusCode != HttpStatusCode.NotFound)
          warnings.Add(name + ": " + Text(got));
      }

      var target = Read(item, secret, credential);

      if (target is not null)
        targets.Add(target);
    }

    targets.Sort(static (left, right) => {
      if (left.IsDefault != right.IsDefault)
        return left.IsDefault ? -1 : 1;

      return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
    });

    return Result<S3TargetSet>.Ok(new S3TargetSet(targets, other, string.Join("; ", warnings)));
  }

  public static async Task<Result> SaveAsync(IClusterSession session, S3Target target, CancellationToken cancellationToken = default) {
    var secret = await session.ApplyAsync(SecretDocument(target), SecretRef, cancellationToken).ConfigureAwait(false);

    if (!secret.IsSuccess)
      return secret.ToResult();

    var backup = await session.ApplyAsync(TargetDocument(target), TargetRef, cancellationToken).ConfigureAwait(false);

    return backup.IsSuccess ? Result.Ok() : backup.ToResult();
  }

  public static Task<Result> DeleteAsync(IClusterSession session, S3Target target, CancellationToken cancellationToken = default) =>
    session.DeleteAsync(TargetRef, target.Name, target.Namespace, false, cancellationToken);

  public static S3Target? Read(JsonObject item, JsonObject? secret, string? credential = null) {
    var url = JsonPath.Text(item["spec"]?["backupTargetURL"]);

    if (!S3Layout.TryParseUrl(url, out var bucket, out var region, out var prefix))
      return null;

    var name = JsonPath.Name(item);
    var secretName = string.IsNullOrWhiteSpace(credential) ? JsonPath.Text(item["spec"]?["credentialSecret"]) : credential;

    if (secretName.Length == 0)
      secretName = SecretNameFor(name);

    var poll = JsonPath.Text(item["spec"]?["pollInterval"]);
    var endpoints = Decode(secret, "AWS_ENDPOINTS");
    var hosted = Decode(secret, "VIRTUAL_HOSTED_STYLE");

    return new S3Target(
      name,
      JsonPath.Namespace(item) ?? "longhorn-system",
      bucket,
      region,
      prefix,
      endpoints,
      Decode(secret, "AWS_ACCESS_KEY_ID"),
      Decode(secret, "AWS_SECRET_ACCESS_KEY"),
      S3Layout.PathStyle(endpoints, hosted),
      Decode(secret, "AWS_CERT"),
      secretName,
      poll.Length == 0 ? DefaultPoll : poll);
  }

  public static JsonObject SecretDocument(S3Target target) {
    var data = new JsonObject {
      ["AWS_ACCESS_KEY_ID"] = Encode(target.AccessKey),
      ["AWS_SECRET_ACCESS_KEY"] = Encode(target.SecretKey),
      ["AWS_ENDPOINTS"] = Encode(target.Endpoint),
      ["VIRTUAL_HOSTED_STYLE"] = Encode(target.PathStyle ? "false" : "true")
    };

    if (!string.IsNullOrEmpty(target.Certificate))
      data["AWS_CERT"] = Encode(target.Certificate);

    return new JsonObject {
      ["apiVersion"] = "v1",
      ["kind"] = "Secret",
      ["type"] = "Opaque",
      ["metadata"] = new JsonObject {
        ["name"] = target.CredentialSecret,
        ["namespace"] = target.Namespace
      },
      ["data"] = data
    };
  }

  public static JsonObject TargetDocument(S3Target target) =>
    new() {
      ["apiVersion"] = "longhorn.io/v1beta2",
      ["kind"] = "BackupTarget",
      ["metadata"] = new JsonObject {
        ["name"] = target.Name,
        ["namespace"] = target.Namespace
      },
      ["spec"] = new JsonObject {
        ["backupTargetURL"] = S3Layout.FormatUrl(target.Bucket, target.Region, target.Prefix),
        ["credentialSecret"] = target.CredentialSecret,
        ["pollInterval"] = string.IsNullOrWhiteSpace(target.PollInterval) ? DefaultPoll : target.PollInterval
      }
    };

  public static string Decode(JsonObject? secret, string key) {
    var raw = JsonPath.Text(secret?["data"]?[key]).Trim();

    if (raw.Length == 0)
      return "";

    try {
      return Encoding.UTF8.GetString(Convert.FromBase64String(raw));
    }
    catch (FormatException) {
      return raw;
    }
  }

  private static string Encode(string value) =>
    Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

  private static string Text(Result result) =>
    result.Messages.Count == 0 ? "The credential secret could not be read." : string.Join("; ", result.Messages);
}

/// <summary>
/// S3 backup targets loaded from the cluster, plus targets that are not S3.
/// </summary>
public sealed record S3TargetSet(IReadOnlyList<S3Target> Targets, int OtherTargets, string Warning);
