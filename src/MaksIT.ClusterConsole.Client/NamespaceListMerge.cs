using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Client;

internal static class NamespaceListMerge {
  public const string OrphanedPhase = "Orphaned";

  public static JsonObject Document(string name, string phase, DateTimeOffset? created = null) {
    var metadata = new JsonObject { ["name"] = name };
    if (created is not null)
      metadata["creationTimestamp"] = created.Value.ToUniversalTime().ToString("o");

    return new JsonObject {
      ["apiVersion"] = "v1",
      ["kind"] = "Namespace",
      ["metadata"] = metadata,
      ["status"] = new JsonObject { ["phase"] = phase }
    };
  }

  public static IReadOnlyList<JsonObject> WithOrphansFromPods(
    IReadOnlyList<JsonObject> namespaces,
    IEnumerable<(string Namespace, DateTimeOffset? Created)> pods) {
    var items = namespaces.Select(item => item.DeepClone()).OfType<JsonObject>().ToList();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var item in items) {
      var name = item["metadata"]?["name"]?.GetValue<string>();
      if (!string.IsNullOrEmpty(name))
        seen.Add(name);
    }

    foreach (var pod in pods) {
      if (string.IsNullOrEmpty(pod.Namespace) || !seen.Add(pod.Namespace))
        continue;
      items.Add(Document(pod.Namespace, OrphanedPhase, pod.Created));
    }

    return items;
  }
}
