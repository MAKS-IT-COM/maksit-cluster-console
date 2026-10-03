using System.Text;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Client.Cluster;

public sealed record DrainPodAction(string Namespace, string Name, string Action, string Reason);

public sealed record DrainNodePlan(string Node, IReadOnlyList<DrainPodAction> Pods) {
  public int EvictCount => Pods.Count(pod => pod.Action == DrainPlan.Evict);
}

public sealed record DrainPreview(IReadOnlyList<DrainNodePlan> Nodes);

public static class DrainPlan {
  public const string Evict = "Evict";
  public const string Skip = "Skip";
  public const string Blocked = "Blocked";

  public static DrainNodePlan ForNode(
    string nodeName,
    IEnumerable<JsonObject> pods,
    IEnumerable<JsonObject> disruptionBudgets) {
    var onNode = pods.Where(pod => NodeName(pod) == nodeName).ToList();
    var actions = new Dictionary<string, DrainPodAction>(StringComparer.Ordinal);
    var candidates = new List<JsonObject>();

    foreach (var pod in onNode) {
      var name = Name(pod);
      var ns = Namespace(pod);
      var key = $"{ns}/{name}";

      if (IsCompleted(pod))
        actions[key] = new DrainPodAction(ns, name, Skip, "completed");
      else if (IsMirror(pod))
        actions[key] = new DrainPodAction(ns, name, Skip, "mirror pod");
      else if (IsDaemonSet(pod))
        actions[key] = new DrainPodAction(ns, name, Skip, "DaemonSet");
      else if (IsTerminating(pod))
        actions[key] = new DrainPodAction(ns, name, Skip, "terminating");
      else if (!IsManaged(pod))
        actions[key] = new DrainPodAction(ns, name, Skip, "no controller");
      else
        candidates.Add(pod);
    }

    var blocked = BlockedByBudgets(candidates, disruptionBudgets);

    foreach (var pod in candidates) {
      var name = Name(pod);
      var ns = Namespace(pod);
      var key = $"{ns}/{name}";
      actions[key] = blocked.TryGetValue(key, out var reason)
        ? new DrainPodAction(ns, name, Blocked, reason)
        : new DrainPodAction(ns, name, Evict, "");
    }

    var ordered = actions.Values
      .OrderBy(pod => Rank(pod.Action))
      .ThenBy(pod => pod.Namespace, StringComparer.Ordinal)
      .ThenBy(pod => pod.Name, StringComparer.Ordinal)
      .ToList();

    return new DrainNodePlan(nodeName, ordered);
  }

  public static string Format(IReadOnlyList<DrainNodePlan> nodes) {
    var sb = new StringBuilder();

    foreach (var node in nodes) {
      if (sb.Length > 0)
        sb.AppendLine();

      sb.AppendLine(node.Node);
      AppendGroup(sb, "Will move", node.Pods.Where(pod => pod.Action == Evict));
      AppendGroup(sb, "Will remain", node.Pods.Where(pod => pod.Action != Evict));
    }

    sb.AppendLine();
    sb.Append("Nothing changes until you press Drain. Cancel leaves the node as it is. Drain cordons the node, then moves only the pods under Will move. Pods under Will remain stay. A PodDisruptionBudget that would deny eviction is left in place; those pods are not deleted.");

    return sb.ToString();
  }

  private static void AppendGroup(StringBuilder sb, string heading, IEnumerable<DrainPodAction> pods) {
    var list = pods.ToList();
    sb.Append(heading);
    sb.Append(" (");
    sb.Append(list.Count);
    sb.AppendLine(")");

    if (list.Count == 0) {
      sb.AppendLine("  None");

      return;
    }

    foreach (var pod in list) {
      sb.Append("  ");
      sb.Append(pod.Namespace);
      sb.Append('/');
      sb.Append(pod.Name);

      if (!string.IsNullOrEmpty(pod.Reason)) {
        sb.Append("  ");
        sb.Append(pod.Reason);
      }

      sb.AppendLine();
    }
  }

  private static Dictionary<string, string> BlockedByBudgets(
    IReadOnlyList<JsonObject> candidates,
    IEnumerable<JsonObject> disruptionBudgets) {
    var blocked = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var budget in disruptionBudgets) {
      var allowed = ReadInt(budget["status"]?["disruptionsAllowed"]);

      if (allowed is null)
        continue;

      var matching = candidates
        .Where(pod => Matches(pod, budget))
        .OrderBy(pod => Namespace(pod), StringComparer.Ordinal)
        .ThenBy(pod => Name(pod), StringComparer.Ordinal)
        .ToList();

      for (var i = Math.Max(0, allowed.Value); i < matching.Count; i++) {
        var pod = matching[i];
        var key = $"{Namespace(pod)}/{Name(pod)}";
        blocked.TryAdd(key, $"PDB {Namespace(budget)}/{Name(budget)} allows {allowed.Value} disruption(s)");
      }
    }

    return blocked;
  }

  private static bool Matches(JsonObject pod, JsonObject budget) {
    if (!string.Equals(Namespace(pod), Namespace(budget), StringComparison.Ordinal))
      return false;

    var selector = budget["spec"]?["selector"] as JsonObject;

    if (selector is null)
      return false;

    var labels = pod["metadata"]?["labels"] as JsonObject;
    var matchLabels = selector["matchLabels"] as JsonObject;
    var expressions = selector["matchExpressions"] as JsonArray;
    var hasLabels = matchLabels is { Count: > 0 };
    var hasExpressions = expressions is { Count: > 0 };

    if (!hasLabels && !hasExpressions)
      return true;

    if (hasLabels) {
      foreach (var pair in matchLabels!) {
        if (!string.Equals(JsonPathText(labels?[pair.Key]), JsonPathText(pair.Value), StringComparison.Ordinal))
          return false;
      }
    }

    if (!hasExpressions)
      return true;

    foreach (var expression in expressions!.OfType<JsonObject>()) {
      if (!MatchExpression(labels, expression))
        return false;
    }

    return true;
  }

  private static bool MatchExpression(JsonObject? labels, JsonObject expression) {
    var key = JsonPathText(expression["key"]);
    var op = JsonPathText(expression["operator"]);
    var values = (expression["values"] as JsonArray)?.Select(JsonPathText).ToList() ?? [];
    var present = labels?[key] is not null;
    var value = JsonPathText(labels?[key]);

    return op switch {
      "In" => present && values.Contains(value, StringComparer.Ordinal),
      "NotIn" => !present || !values.Contains(value, StringComparer.Ordinal),
      "Exists" => present,
      "DoesNotExist" => !present,
      _ => false
    };
  }

  private static bool IsCompleted(JsonObject pod) {
    var phase = JsonPathText(pod["status"]?["phase"]);

    return phase is "Succeeded" or "Failed";
  }

  private static bool IsMirror(JsonObject pod) =>
    pod["metadata"]?["annotations"]?["kubernetes.io/config.mirror"] is not null;

  private static bool IsTerminating(JsonObject pod) =>
    pod["metadata"]?["deletionTimestamp"] is not null;

  private static bool IsDaemonSet(JsonObject pod) =>
    Owners(pod).Any(owner => JsonPathText(owner["kind"]) == "DaemonSet");

  private static bool IsManaged(JsonObject pod) {
    foreach (var owner in Owners(pod)) {
      if (IsTrue(owner["controller"]))
        return true;

      var kind = JsonPathText(owner["kind"]);

      if (kind is "ReplicaSet" or "StatefulSet" or "Job" or "ReplicationController" or "Deployment")
        return true;
    }

    return false;
  }

  private static IEnumerable<JsonObject> Owners(JsonObject pod) =>
    (pod["metadata"]?["ownerReferences"] as JsonArray)?.OfType<JsonObject>() ?? [];

  private static string NodeName(JsonObject pod) => JsonPathText(pod["spec"]?["nodeName"]);

  private static string Name(JsonObject pod) => JsonPathText(pod["metadata"]?["name"]);

  private static string Namespace(JsonObject pod) => JsonPathText(pod["metadata"]?["namespace"]);

  private static int Rank(string action) =>
    action switch {
      Evict => 0,
      Blocked => 1,
      _ => 2
    };

  private static int? ReadInt(JsonNode? node) {
    if (node is not JsonValue value)
      return null;

    if (value.TryGetValue<int>(out var number))
      return number;

    if (value.TryGetValue<long>(out var wide))
      return (int)wide;

    if (value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
      return parsed;

    return null;
  }

  private static bool IsTrue(JsonNode? node) =>
    node is JsonValue value
    && (value.TryGetValue<bool>(out var flag) && flag
        || value.TryGetValue<string>(out var text) && text.Equals("true", StringComparison.OrdinalIgnoreCase));

  private static string JsonPathText(JsonNode? node) {
    if (node is JsonValue value)
      return value.TryGetValue<string>(out var text) ? text ?? "" : value.ToString() ?? "";

    return node?.ToString() ?? "";
  }
}
