using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public sealed record ClusterIssue(
  string Id,
  string Message,
  string ObjectName,
  string Kind,
  string Age,
  DateTimeOffset OccurredAt,
  string Severity,
  string State);

public sealed record ClusterIssueSet(
  IReadOnlyList<ClusterIssue> Warnings,
  IReadOnlyList<ClusterIssue> Errors) {
  public static ClusterIssueSet Empty { get; } = new([], []);
}

public static class ClusterIssues {
  public const string Active = "Active";
  public const string Resolved = "Resolved";

  public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

  private static readonly HashSet<string> HealthyNodeConditions = new(StringComparer.Ordinal) {
    "Ready",
    "HostUpgrades",
    "SchedulingDisabled"
  };

  public static ClusterIssueSet Collect(
    IEnumerable<JsonObject> nodes,
    IEnumerable<JsonObject> events,
    IEnumerable<JsonObject> pods,
    DateTimeOffset? utcNow = null) {
    var now = utcNow ?? DateTimeOffset.UtcNow;
    var podByUid = pods
      .Select(p => (Uid: JsonPath.Uid(p), Pod: p))
      .Where(p => !string.IsNullOrWhiteSpace(p.Uid))
      .ToDictionary(p => p.Uid, p => p.Pod, StringComparer.Ordinal);

    var warnings = new List<ClusterIssue>();
    var errors = new List<ClusterIssue>();

    foreach (var node in nodes)
      warnings.AddRange(NodeWarnings(node, now));

    foreach (var issue in EventIssues(events, podByUid, now)) {
      if (issue.Severity == "Error")
        errors.Add(issue);
      else
        warnings.Add(issue);
    }

    return new ClusterIssueSet(Rank(warnings), Rank(errors));
  }

  public static string Caption(string noun, IReadOnlyList<ClusterIssue> issues) {
    var resolved = 0;
    foreach (var issue in issues) {
      if (issue.State == Resolved)
        resolved++;
    }

    var active = issues.Count - resolved;
    if (resolved == 0)
      return $"{noun}: {active}";

    if (active == 0)
      return $"{noun}: {resolved} resolved";

    return $"{noun}: {active} ({resolved} resolved)";
  }

  private static List<ClusterIssue> Rank(IEnumerable<ClusterIssue> issues) =>
    issues
      .OrderBy(i => i.State == Resolved ? 1 : 0)
      .ThenByDescending(i => i.OccurredAt)
      .ToList();

  private static IEnumerable<ClusterIssue> NodeWarnings(JsonObject node, DateTimeOffset now) {
    var name = JsonPath.Name(node);
    JsonPath.TryTimestamp(node["metadata"]?["creationTimestamp"], out var created);
    var createdAt = created == default ? now : created;
    var nodeAge = JsonPath.Age(createdAt, now);
    var conditions = node["status"]?["conditions"] as JsonArray;
    if (conditions is null)
      yield break;

    foreach (var condition in conditions.OfType<JsonObject>()) {
      var type = Text(condition["type"]);
      if (!IsTrue(condition["status"]) || HealthyNodeConditions.Contains(type))
        continue;

      var message = Text(condition["message"]);
      if (string.IsNullOrWhiteSpace(message))
        message = type;

      yield return new ClusterIssue(
        $"node/{JsonPath.Uid(node)}/{type}",
        message,
        name,
        "Node",
        nodeAge,
        createdAt,
        "Warning",
        Active);
    }
  }

  private static IEnumerable<ClusterIssue> EventIssues(
    IEnumerable<JsonObject> events,
    IReadOnlyDictionary<string, JsonObject> pods,
    DateTimeOffset now) {
    var latestWarning = new Dictionary<string, (JsonObject Event, DateTimeOffset At)>(StringComparer.Ordinal);
    var latestNormal = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

    foreach (var ev in events) {
      var type = Text(ev["type"]);
      var involved = ev["involvedObject"] as JsonObject;
      var key = InvolvedKey(involved);
      var at = EventTime(ev);
      if (type.Equals("Normal", StringComparison.OrdinalIgnoreCase)) {
        var normalKey = $"{key}\0{Text(ev["reason"])}";
        if (!latestNormal.TryGetValue(normalKey, out var existing) || at > existing)
          latestNormal[normalKey] = at;
        continue;
      }

      if (!type.Equals("Warning", StringComparison.OrdinalIgnoreCase)
          && !type.Equals("Error", StringComparison.OrdinalIgnoreCase))
        continue;

      if (latestWarning.TryGetValue(key, out var previous) && previous.At >= at)
        continue;
      latestWarning[key] = (ev, at);
    }

    foreach (var (ev, at) in latestWarning.Values) {
      var involved = ev["involvedObject"] as JsonObject;
      var kind = Text(involved?["kind"]);
      var podStillUnhealthy = kind.Equals("Pod", StringComparison.OrdinalIgnoreCase)
        && ShouldKeepPodEvent(involved, pods);
      if (kind.Equals("Pod", StringComparison.OrdinalIgnoreCase) && !podStillUnhealthy)
        continue;

      var message = Text(ev["message"]);
      if (string.IsNullOrWhiteSpace(message))
        message = Text(ev["reason"]);

      var severity = Text(ev["type"]).Equals("Error", StringComparison.OrdinalIgnoreCase)
        ? "Error"
        : "Warning";
      var reasonKey = $"{InvolvedKey(involved)}\0{Text(ev["reason"])}";
      var resolvedByNormal = latestNormal.TryGetValue(reasonKey, out var normalAt) && normalAt >= at;
      var stale = now - at >= StaleAfter;
      var state = resolvedByNormal || (stale && !podStillUnhealthy)
        ? Resolved
        : Active;

      yield return new ClusterIssue(
        JsonPath.Uid(ev),
        message,
        Text(involved?["name"]),
        "Event",
        JsonPath.Age(at, now),
        at,
        severity,
        state);
    }
  }

  private static string InvolvedKey(JsonObject? involved) {
    var uid = Text(involved?["uid"]);
    if (!string.IsNullOrWhiteSpace(uid))
      return uid;

    return $"{Text(involved?["kind"])}/{Text(involved?["namespace"])}/{Text(involved?["name"])}";
  }

  private static bool ShouldKeepPodEvent(JsonObject? involved, IReadOnlyDictionary<string, JsonObject> pods) {
    var uid = Text(involved?["uid"]);
    if (string.IsNullOrWhiteSpace(uid) || !pods.TryGetValue(uid, out var pod))
      return false;

    if (PodHasIssues(pod))
      return true;

    var priority = pod["spec"]?["priority"] as JsonValue;
    return priority is not null
      && priority.TryGetValue<int>(out var value)
      && value >= 500_000;
  }

  private static bool PodHasIssues(JsonObject pod) {
    var phase = Text(pod["status"]?["phase"]);
    if (!phase.Equals("Running", StringComparison.OrdinalIgnoreCase))
      return true;

    var conditions = pod["status"]?["conditions"] as JsonArray;
    var ready = conditions?.OfType<JsonObject>()
      .FirstOrDefault(c => Text(c["type"]) == "Ready");
    if (ready is not null && !IsTrue(ready["status"]))
      return true;

    var statuses = pod["status"]?["containerStatuses"] as JsonArray;
    return statuses?.OfType<JsonObject>().Any(status => {
      var waiting = Text(status["state"]?["waiting"]?["reason"]);
      return waiting.Equals("CrashLoopBackOff", StringComparison.OrdinalIgnoreCase);
    }) == true;
  }

  private static DateTimeOffset EventTime(JsonObject ev) {
    foreach (var node in new[] {
               ev["lastTimestamp"],
               ev["eventTime"],
               ev["series"]?["lastObservedTime"],
               ev["metadata"]?["creationTimestamp"]
             }) {
      if (JsonPath.TryTimestamp(node, out var when))
        return when;
    }

    return DateTimeOffset.UnixEpoch;
  }

  private static string Text(JsonNode? node) =>
    node is JsonValue value
      ? value.TryGetValue<string>(out var text) ? text ?? string.Empty : value.ToString() ?? string.Empty
      : node?.ToString() ?? string.Empty;

  private static bool IsTrue(JsonNode? node) {
    if (node is not JsonValue value)
      return false;
    if (value.TryGetValue<bool>(out var flag))
      return flag;
    return value.TryGetValue<string>(out var text)
      && text.Equals("True", StringComparison.OrdinalIgnoreCase);
  }
}
