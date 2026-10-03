using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class NodeScheduling {
  public static bool IsAvailable(JsonObject node) =>
    IsReady(node) && !IsUnschedulable(node);

  public static bool LeavesAvailableNode(IEnumerable<JsonObject> nodes, IEnumerable<string> names) {
    var taking = new HashSet<string>(names, StringComparer.Ordinal);

    foreach (var node in nodes) {
      if (!IsAvailable(node))
        continue;

      if (taking.Contains(JsonPath.Name(node)))
        continue;

      return true;
    }

    return false;
  }

  private static bool IsReady(JsonObject node) {
    var conditions = node["status"]?["conditions"] as JsonArray;
    var ready = conditions?.OfType<JsonObject>().FirstOrDefault(condition =>
      string.Equals(JsonPath.Text(condition["type"]), "Ready", StringComparison.Ordinal));

    return string.Equals(JsonPath.Text(ready?["status"]), "True", StringComparison.Ordinal);
  }

  private static bool IsUnschedulable(JsonObject node) {
    var flag = node["spec"]?["unschedulable"];

    return flag is JsonValue value && value.TryGetValue<bool>(out var unschedulable) && unschedulable;
  }
}
