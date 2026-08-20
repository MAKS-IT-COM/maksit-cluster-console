using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class ResourceOwnership {
  public static bool Owns(JsonObject pod, JsonObject owner) {
    var ownerName = JsonPath.Name(owner);
    var refs = pod["metadata"]?["ownerReferences"] as JsonArray;
    if (refs?.OfType<JsonObject>().Any(r => r["name"]?.ToString() == ownerName) == true)
      return true;

    var matchLabels = SelectorLabels(owner);
    if (matchLabels is not null && matchLabels.Count > 0)
      return LabelsMatch(pod["metadata"]?["labels"] as JsonObject, matchLabels);

    var labels = pod["metadata"]?["labels"] as JsonObject;
    return labels?["app"]?.ToString() == ownerName
      || labels?[ApplicationManifest.NameKey]?.ToString() == ownerName
      || ApplicationManifest.SameInstance(pod, owner);
  }

  public static JsonObject? SelectorLabels(JsonObject? owner) {
    var selector = owner?["spec"]?["selector"];
    if (selector is not JsonObject obj)
      return null;
    if (obj["matchLabels"] is JsonObject match)
      return match;
    if (obj.ContainsKey("matchExpressions"))
      return null;

    return obj;
  }

  private static bool LabelsMatch(JsonObject? podLabels, JsonObject? required) {
    if (podLabels is null || required is null || required.Count == 0)
      return false;

    foreach (var pair in required) {
      if (podLabels[pair.Key]?.ToString() != pair.Value?.ToString())
        return false;
    }

    return true;
  }
}
