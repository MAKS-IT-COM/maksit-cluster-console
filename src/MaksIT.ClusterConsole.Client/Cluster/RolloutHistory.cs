using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Client.Cluster;

public static class RolloutHistory {
  public const string RevisionAnnotation = "deployment.kubernetes.io/revision";

  public static string? LabelSelector(JsonObject deployment) {
    if (deployment["spec"]?["selector"]?["matchLabels"] is not JsonObject labels || labels.Count == 0)
      return null;

    return string.Join(',', labels.Select(pair => pair.Key + "=" + Text(pair.Value)));
  }

  public static int Revision(JsonObject item) {
    var text = Text(item["metadata"]?["annotations"]?[RevisionAnnotation]);
    return int.TryParse(text, out var revision) ? revision : 0;
  }

  public static IReadOnlyList<string> Lines(IEnumerable<JsonObject> replicaSets) =>
    replicaSets
      .Select(item => (Revision: Revision(item), Item: item))
      .Where(item => item.Revision > 0)
      .OrderByDescending(item => item.Revision)
      .Select(item => {
        var ready = Text(item.Item["status"]?["readyReplicas"]);
        var replicas = Text(item.Item["status"]?["replicas"]);
        if (string.IsNullOrEmpty(ready))
          ready = "0";
        if (string.IsNullOrEmpty(replicas))
          replicas = "0";
        return $"{item.Revision}  {Text(item.Item["metadata"]?["name"])}  {ready}/{replicas}";
      })
      .ToList();

  public static JsonObject? PreviousTemplate(JsonObject deployment, IEnumerable<JsonObject> replicaSets) {
    var name = Text(deployment["metadata"]?["name"]);
    var current = Revision(deployment);
    var owned = replicaSets
      .Where(item => OwnedBy(item, name))
      .Select(item => (Revision: Revision(item), Item: item))
      .Where(item => item.Revision > 0 && (current == 0 || item.Revision < current))
      .OrderByDescending(item => item.Revision)
      .ToList();
    var template = owned.FirstOrDefault().Item?["spec"]?["template"];
    return template?.DeepClone() as JsonObject;
  }

  private static bool OwnedBy(JsonObject item, string deploymentName) {
    if (item["metadata"]?["ownerReferences"] is not JsonArray owners)
      return false;

    return owners.OfType<JsonObject>().Any(owner =>
      string.Equals(Text(owner["kind"]), "Deployment", StringComparison.Ordinal)
      && string.Equals(Text(owner["name"]), deploymentName, StringComparison.Ordinal));
  }

  private static string Text(JsonNode? node) {
    if (node is JsonValue value && value.TryGetValue<string>(out var text))
      return text ?? string.Empty;

    return node?.ToString() ?? string.Empty;
  }
}
