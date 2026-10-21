namespace MaksIT.ClusterConsole.Client.S3;

/// <summary>
/// Backup-target URLs, object keys, and one-folder listings.
/// </summary>
public static class S3Layout {
  public const int PreviewLimit = 512 * 1024;

  public const long CopyLimit = 5L * 1024 * 1024 * 1024;

  public static bool TryParseUrl(string? url, out string bucket, out string region, out string prefix) {
    bucket = "";
    region = "";
    prefix = "";

    if (string.IsNullOrWhiteSpace(url))
      return false;

    var text = url.Trim();

    if (!text.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
      return false;

    var rest = text[5..];
    var at = rest.IndexOf('@');

    if (at <= 0)
      return false;

    bucket = rest[..at];
    var after = rest[(at + 1)..];
    var slash = after.IndexOf('/');

    if (slash < 0) {
      region = after;
    }
    else {
      region = after[..slash];
      prefix = after[(slash + 1)..].Trim('/');
      var query = prefix.IndexOf('?');

      if (query >= 0)
        prefix = prefix[..query].Trim('/');
    }

    return IsBucket(bucket) && IsRegion(region) && IsPrefix(prefix);
  }

  public static string FormatUrl(string bucket, string region, string prefix) {
    var path = prefix.Trim().Trim('/');
    var root = "s3://" + bucket.Trim() + "@" + region.Trim() + "/";

    return path.Length == 0 ? root : root + path;
  }

  public static string FirstEndpoint(string? endpoints) {
    if (string.IsNullOrWhiteSpace(endpoints))
      return "";

    var first = endpoints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    return string.IsNullOrWhiteSpace(first) ? "" : first.TrimEnd('/');
  }

  public static bool PathStyle(string endpoints, string virtualHosted) {
    var custom = FirstEndpoint(endpoints).Length > 0;

    if (custom)
      return !virtualHosted.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    return virtualHosted.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
  }

  public static bool IsResourceName(string? name) {
    if (string.IsNullOrWhiteSpace(name) || name.Length > 63)
      return false;

    if (!char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
      return false;

    foreach (var character in name) {
      if (char.IsAsciiLetter(character) && !char.IsAsciiLetterLower(character))
        return false;

      if (!char.IsAsciiLetterOrDigit(character) && character != '-')
        return false;
    }

    return true;
  }

  public static bool IsBucket(string? bucket) {
    if (string.IsNullOrWhiteSpace(bucket) || bucket.Length is < 3 or > 63)
      return false;

    if (bucket.Contains("..", StringComparison.Ordinal) || bucket.StartsWith('.') || bucket.EndsWith('.') || bucket.StartsWith('-') || bucket.EndsWith('-'))
      return false;

    foreach (var character in bucket) {
      if (char.IsAsciiLetter(character) && !char.IsAsciiLetterLower(character))
        return false;

      if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-')
        return false;
    }

    return true;
  }

  public static bool IsRegion(string? region) {
    if (string.IsNullOrWhiteSpace(region) || region.Length > 64)
      return false;

    foreach (var character in region) {
      if (!char.IsAsciiLetterOrDigit(character) && character != '-')
        return false;
    }

    return true;
  }

  public static bool IsPrefix(string? prefix) {
    if (string.IsNullOrWhiteSpace(prefix))
      return true;

    if (prefix.Contains('\\') || prefix.Contains("//", StringComparison.Ordinal))
      return false;

    var text = prefix.Trim().Trim('/');

    if (text.Length == 0)
      return true;

    foreach (var part in text.Split('/')) {
      if (part.Length == 0 || part is "." or "..")
        return false;
    }

    return true;
  }

  public static bool IsFolderName(string? name) {
    if (string.IsNullOrWhiteSpace(name))
      return false;

    var text = name.Trim();

    return !text.Contains('/') && !text.Contains('\\') && text is not "." and not "..";
  }

  public static string NormalizePrefix(string? prefix) {
    if (string.IsNullOrWhiteSpace(prefix))
      return "";

    var text = prefix.Replace('\\', '/').Trim().Trim('/');

    return text.Length == 0 ? "" : text + "/";
  }

  public static string Parent(string? prefix) {
    var text = NormalizePrefix(prefix).TrimEnd('/');

    if (text.Length == 0)
      return "";

    var slash = text.LastIndexOf('/');

    return slash < 0 ? "" : text[..(slash + 1)];
  }

  public static string Child(string? prefix, string name) =>
    NormalizePrefix(prefix) + name.Trim().Trim('/');

  public static string Display(string? prefix) =>
    NormalizePrefix(prefix).TrimEnd('/');

  public static string Sibling(string key, string newName, bool folder) {
    var trimmed = key.TrimEnd('/');
    var slash = trimmed.LastIndexOf('/');
    var parent = slash < 0 ? "" : trimmed[..(slash + 1)];

    return parent + newName.Trim().Trim('/') + (folder ? "/" : "");
  }

  public static string CopyName(string name, bool folder) {
    if (folder)
      return name + "-copy";

    var dot = name.LastIndexOf('.');

    if (dot <= 0)
      return name + "-copy";

    return name[..dot] + "-copy" + name[dot..];
  }

  public static bool ContainsPrefix(string parent, string child) {
    var root = NormalizePrefix(parent);
    var next = NormalizePrefix(child);

    if (root.Length == 0)
      return false;

    return next.StartsWith(root, StringComparison.Ordinal) && next.Length > root.Length;
  }

  public static long PartSize(long size) {
    const long preferred = 64L * 1024 * 1024;
    const long maxParts = 10000;
    const long minimum = 5L * 1024 * 1024;

    if (size <= 0)
      return preferred;

    var parts = (size + preferred - 1) / preferred;

    if (parts <= maxParts)
      return preferred;

    var sized = (size + maxParts - 1) / maxParts;

    return sized < minimum ? minimum : sized;
  }

  public static string UploadKey(string? prefix, string relative) {
    var clean = relative.Replace('\\', '/').TrimStart('/');

    return NormalizePrefix(prefix) + clean;
  }

  public static string SafeSegment(string name) {
    var invalid = Path.GetInvalidFileNameChars();
    var buffer = name.ToCharArray();

    for (var i = 0; i < buffer.Length; i++) {
      if (invalid.Contains(buffer[i]))
        buffer[i] = '_';
    }

    var text = new string(buffer).Trim();

    return text.Length == 0 ? "object" : text;
  }

  public static IReadOnlyList<S3Bucket> Buckets(IReadOnlyList<S3Bucket> listed, string? configured) {
    var items = new List<S3Bucket>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var bucket in listed) {
      if (string.IsNullOrWhiteSpace(bucket.Name) || !seen.Add(bucket.Name))
        continue;

      items.Add(bucket);
    }

    if (!string.IsNullOrWhiteSpace(configured) && seen.Add(configured))
      items.Add(new S3Bucket(configured, null));

    items.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

    return items;
  }

  public static IReadOnlyList<S3Bucket> MarkBackups(IReadOnlyList<S3Bucket> buckets, IEnumerable<string> backupNames) {
    var names = new HashSet<string>(StringComparer.Ordinal);

    foreach (var name in backupNames) {
      if (!string.IsNullOrWhiteSpace(name))
        names.Add(name);
    }

    var marked = new List<S3Bucket>(buckets.Count);

    foreach (var bucket in buckets)
      marked.Add(names.Contains(bucket.Name) ? bucket with { Backup = true } : bucket);

    return marked;
  }

  public static IReadOnlyList<S3Entry> List(string? prefix, IReadOnlyList<string> folders, IReadOnlyList<S3ObjectInfo> objects) {
    var root = NormalizePrefix(prefix);
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var entries = new List<S3Entry>();

    foreach (var folder in folders) {
      if (!TryName(root, folder, out var name))
        continue;

      if (!seen.Add(name))
        continue;

      entries.Add(new S3Entry(name, NormalizePrefix(folder), true, 0, null));
    }

    foreach (var item in objects) {
      if (item.Key == root || !TryName(root, item.Key, out var name))
        continue;

      var folder = item.Key.EndsWith('/');

      if (folder && !seen.Add(name))
        continue;

      if (!folder && !seen.Add("file:" + name))
        continue;

      entries.Add(new S3Entry(name, folder ? NormalizePrefix(item.Key) : item.Key, folder, item.Size, item.Modified));
    }

    entries.Sort(static (left, right) => {
      if (left.IsDirectory != right.IsDirectory)
        return left.IsDirectory ? -1 : 1;

      return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    });

    return entries;
  }

  private static bool TryName(string root, string key, out string name) {
    name = "";

    if (!key.StartsWith(root, StringComparison.Ordinal))
      return false;

    var rest = key[root.Length..].TrimEnd('/');

    if (rest.Length == 0 || rest.Contains('/'))
      return false;

    name = rest;

    return true;
  }
}
