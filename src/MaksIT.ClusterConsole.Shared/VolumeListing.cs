namespace MaksIT.ClusterConsole.Shared;

public sealed record VolumeEntry(string Name, bool IsDirectory, long Size) {
  public string Display =>
    IsDirectory ? Name + "/" : Name;

  public string SizeText =>
    IsDirectory ? "" : FormatSize(Size);

  private static string FormatSize(long size) {
    if (size < 1024)
      return $"{size} B";
    if (size < 1024 * 1024)
      return $"{size / 1024.0:0.#} KB";

    return $"{size / (1024.0 * 1024.0):0.#} MB";
  }
}

public static class VolumeListing {
  public static IReadOnlyList<VolumeEntry> Parse(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return [];

    var items = new List<VolumeEntry>();
    foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
      var tab = line.IndexOf('\t');
      if (tab < 0) {
        var lsDir = line.EndsWith('/');
        var lsName = line.TrimEnd('/');
        if (string.IsNullOrEmpty(lsName) || lsName is "." or ".." || lsName.Contains('/'))
          continue;

        items.Add(new VolumeEntry(lsName, lsDir, 0));
        continue;
      }

      var type = line[..tab];
      var rest = line[(tab + 1)..];
      var tab2 = rest.IndexOf('\t');
      if (tab2 < 0)
        continue;

      var sizeText = rest[..tab2].Trim();
      var name = rest[(tab2 + 1)..];
      if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Contains('/'))
        continue;

      if (!long.TryParse(sizeText, out var size))
        size = 0;

      items.Add(new VolumeEntry(name, type.StartsWith('d'), size));
    }

    return items
      .OrderByDescending(e => e.IsDirectory)
      .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }
}
