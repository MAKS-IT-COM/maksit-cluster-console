using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class VolumeClaim {
  public static bool TryGet(JsonObject? document, out string @namespace, out string name) {
    @namespace = "";
    name = "";
    if (document is null)
      return false;

    var kind = document["kind"]?.GetValue<string>();
    var metaName = JsonPath.Name(document);
    var metaNs = JsonPath.Namespace(document) ?? "";
    if (IsPersistentVolumeClaim(kind, document, metaNs)) {
      name = metaName;
      @namespace = metaNs;
      return name.Length > 0 && @namespace.Length > 0;
    }

    var claim = JsonPath.VolumeClaim(document);
    var slash = claim.IndexOf('/');
    if (slash <= 0 || slash == claim.Length - 1)
      return false;

    @namespace = claim[..slash];
    name = claim[(slash + 1)..];
    return @namespace.Length > 0 && name.Length > 0;
  }

  private static bool IsPersistentVolumeClaim(string? kind, JsonObject document, string metaNs) {
    if (string.Equals(kind, "PersistentVolumeClaim", StringComparison.OrdinalIgnoreCase))
      return true;

    if (!string.IsNullOrEmpty(kind))
      return false;

    return metaNs.Length > 0 && document["spec"]?["claimRef"] is null;
  }
}
