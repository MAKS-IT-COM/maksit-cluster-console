namespace MaksIT.ClusterConsole.Shared;

public enum MarkupSyntax {
  None,
  Json,
  Yaml
}

public static class MarkupSyntaxDetector {
  public static MarkupSyntax Detect(string? fileName, bool preferYaml = false) {
    if (preferYaml)
      return MarkupSyntax.Yaml;

    return FromFileName(fileName);
  }

  public static MarkupSyntax FromFileName(string? fileName) {
    var name = fileName?.Replace('\\', '/');
    var slash = name?.LastIndexOf('/') ?? -1;
    if (slash >= 0)
      name = name![(slash + 1)..];

    if (name is null)
      return MarkupSyntax.None;
    if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
      return MarkupSyntax.Json;
    if (name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
      return MarkupSyntax.Yaml;

    return MarkupSyntax.None;
  }
}
