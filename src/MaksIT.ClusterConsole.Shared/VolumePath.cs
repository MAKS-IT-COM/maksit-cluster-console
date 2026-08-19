using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public static class VolumePath {
  public static Result<string> Resolve(string root, string? relative) {
    if (string.IsNullOrWhiteSpace(root) || root.Contains('\0') || !root.StartsWith('/'))
      return Result<string>.BadRequest(null, "Invalid volume mount path.");

    relative ??= "";
    if (relative.Contains('\0') || relative.Contains('\\'))
      return Result<string>.BadRequest(null, "Invalid path.");

    var combined = string.IsNullOrEmpty(relative) || relative == "."
      ? root.TrimEnd('/')
      : $"{root.TrimEnd('/')}/{relative.TrimStart('/')}";
    if (string.IsNullOrEmpty(combined))
      combined = "/";

    var parts = new List<string>();
    foreach (var part in combined.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
      if (part == ".")
        continue;
      if (part == "..") {
        if (parts.Count == 0)
          return Result<string>.BadRequest(null, "Path escapes the volume mount.");

        parts.RemoveAt(parts.Count - 1);
        continue;
      }

      parts.Add(part);
    }

    var full = parts.Count == 0 ? "/" : "/" + string.Join('/', parts);
    var prefix = root.TrimEnd('/');
    if (prefix.Length == 0)
      prefix = "/";

    if (full != prefix && !full.StartsWith(prefix + "/", StringComparison.Ordinal))
      return Result<string>.BadRequest(null, "Path escapes the volume mount.");

    return Result<string>.Ok(full);
  }

  public static string CombineRelative(string current, string name) {
    if (string.IsNullOrEmpty(current) || current == ".")
      return name;

    return $"{current.Trim('/')}/{name}";
  }

  public static string ParentRelative(string current) {
    if (string.IsNullOrEmpty(current))
      return "";

    var trimmed = current.Replace('\\', '/').Trim('/');
    var i = trimmed.LastIndexOf('/');
    return i < 0 ? "" : trimmed[..i];
  }
}
