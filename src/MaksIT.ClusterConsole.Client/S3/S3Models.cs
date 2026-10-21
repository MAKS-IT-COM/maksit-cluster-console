using System.Globalization;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Client.S3;

/// <summary>
/// One S3 location behind a Longhorn backup target.
/// </summary>
public sealed record S3Target(
  string Name,
  string Namespace,
  string Bucket,
  string Region,
  string Prefix,
  string Endpoint,
  string AccessKey,
  string SecretKey,
  bool PathStyle,
  string Certificate,
  string CredentialSecret,
  string PollInterval) {
  public bool IsDefault =>
    string.Equals(Name, "default", StringComparison.Ordinal);

  public string Caption {
    get {
      var where = string.IsNullOrEmpty(Prefix) ? Bucket : Bucket + "/" + Prefix;

      return Name + " · " + where;
    }
  }
}

/// <summary>
/// One bucket the credentials can see on this endpoint.
/// Backup is set when a Longhorn backup target uses the bucket.
/// </summary>
public sealed record S3Bucket(string Name, DateTimeOffset? Created) {
  public bool Backup { get; init; }

  public string Caption =>
    Backup ? Name + " · Longhorn" : Name;
}

/// <summary>
/// One object or folder in a bucket listing.
/// </summary>
public sealed record S3Entry(string Name, string Key, bool IsDirectory, long Size, DateTimeOffset? Modified) {
  public string SizeText =>
    IsDirectory ? "" : KubeQuantity.FormatBytesCompact(Size);

  public string ModifiedText =>
    Modified is null ? "" : Modified.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

  public long ModifiedSort =>
    Modified?.UtcTicks ?? 0;

  public string KindText =>
    IsDirectory ? "Folder" : "File";
}

/// <summary>
/// An object returned by a bucket listing, before folder names are applied.
/// </summary>
public readonly record struct S3ObjectInfo(string Key, long Size, DateTimeOffset? Modified);

/// <summary>
/// Text loaded from an object for the preview editor.
/// </summary>
public sealed record S3TextFile(string Text, bool Binary);
