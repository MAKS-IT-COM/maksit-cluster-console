using System.Globalization;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class PodStatus {
  public static string Of(JsonObject? pod) {
    if (pod is null)
      return string.Empty;

    var status = pod["status"] as JsonObject;
    var phase = Text(status?["phase"]);
    var reason = Text(status?["reason"]);
    if (string.IsNullOrEmpty(reason))
      reason = phase;

    var conditions = status?["conditions"] as JsonArray;
    if (HasCondition(conditions, "PodScheduled", reason: "SchedulingGated"))
      reason = "SchedulingGated";

    var spec = pod["spec"] as JsonObject;
    var initSpecs = spec?["initContainers"] as JsonArray;
    var initializing = false;
    var initStatuses = status?["initContainerStatuses"] as JsonArray;
    if (initStatuses is not null) {
      var index = 0;
      foreach (var container in initStatuses.OfType<JsonObject>()) {
        var initSpec = FindNamed(initSpecs, Text(container["name"]));
        var state = container["state"] as JsonObject;
        var terminated = state?["terminated"] as JsonObject;
        var waiting = state?["waiting"] as JsonObject;

        if (terminated is not null && Int(terminated["exitCode"]) == 0) {
          index++;
          continue;
        }

        if (IsRestartableInit(initSpec) && IsTrue(container["started"])) {
          index++;
          continue;
        }

        if (terminated is not null) {
          reason = PrefixInit(TerminatedReason(terminated));
          initializing = true;
          break;
        }

        var waitingReason = Text(waiting?["reason"]);
        if (!string.IsNullOrEmpty(waitingReason) && waitingReason != "PodInitializing") {
          reason = PrefixInit(waitingReason);
          initializing = true;
          break;
        }

        var total = initSpecs?.Count ?? initStatuses.Count;
        reason = $"Init:{index}/{total}";
        initializing = true;
        break;
      }
    }

    if (!initializing || HasCondition(conditions, "Initialized", statusTrue: true)) {
      var hasRunning = false;
      string? errorReason = null;
      var containers = status?["containerStatuses"] as JsonArray;
      if (containers is not null) {
        var listed = containers.OfType<JsonObject>().ToList();
        for (var i = listed.Count - 1; i >= 0; i--) {
          var container = listed[i];
          var state = container["state"] as JsonObject;
          var waiting = state?["waiting"] as JsonObject;
          var terminated = state?["terminated"] as JsonObject;
          var waitingReason = Text(waiting?["reason"]);
          if (!string.IsNullOrEmpty(waitingReason)) {
            reason = waitingReason;
            continue;
          }

          if (terminated is not null) {
            reason = TerminatedReason(terminated);
            if (Int(terminated["exitCode"]) != 0)
              errorReason = reason;
            continue;
          }

          if (IsTrue(container["ready"]) && state?["running"] is not null)
            hasRunning = true;
        }
      }

      if (reason == "Completed") {
        if (hasRunning && HasCondition(conditions, "Ready", statusTrue: true))
          reason = "Running";
        else if (!string.IsNullOrEmpty(errorReason))
          reason = errorReason;
        else if (hasRunning)
          reason = "NotReady";
      }
      else if (string.IsNullOrEmpty(reason) && hasRunning)
        reason = "Running";
    }

    if (pod["metadata"]?["deletionTimestamp"] is not null) {
      if (Text(status?["reason"]) is "NodeLost" or "NodeUnreachable")
        reason = "Unknown";
      else if (!IsTerminal(phase))
        reason = "Terminating";
    }

    if (reason.Equals("Running", StringComparison.OrdinalIgnoreCase)
        && !HasCondition(conditions, "Ready", statusTrue: true)) {
      var crash = CrashHint(status);
      if (!string.IsNullOrEmpty(crash))
        return crash;

      if (HasCondition(conditions, "Ready", statusTrue: false))
        return "NotReady";
    }

    return string.IsNullOrEmpty(reason) ? phase : reason;
  }

  private static string? CrashHint(JsonObject? status) {
    foreach (var container in AllStatuses(status)) {
      var waiting = Text(container["state"]?["waiting"]?["reason"]);
      if (!string.IsNullOrEmpty(waiting))
        return waiting;

      var restarts = Int(container["restartCount"]);
      var last = container["lastState"]?["terminated"] as JsonObject;
      if (restarts <= 0 || last is null || Int(last["exitCode"]) == 0)
        continue;

      var lastReason = Text(last["reason"]);
      if (lastReason.Equals("OOMKilled", StringComparison.OrdinalIgnoreCase))
        return "OOMKilled";
      if (restarts >= 2)
        return "CrashLoopBackOff";
      return string.IsNullOrEmpty(lastReason) ? "Error" : lastReason;
    }

    return null;
  }

  private static IEnumerable<JsonObject> AllStatuses(JsonObject? status) {
    foreach (var field in new[] { "containerStatuses", "initContainerStatuses", "ephemeralContainerStatuses" }) {
      if (status?[field] is not JsonArray array)
        continue;

      foreach (var container in array.OfType<JsonObject>())
        yield return container;
    }
  }

  private static string TerminatedReason(JsonObject terminated) {
    var reason = Text(terminated["reason"]);
    if (!string.IsNullOrEmpty(reason))
      return reason;

    var signal = Int(terminated["signal"]);
    if (signal != 0)
      return $"Signal:{signal}";

    return $"ExitCode:{Int(terminated["exitCode"])}";
  }

  private static string PrefixInit(string reason) =>
    reason.StartsWith("Init:", StringComparison.Ordinal) ? reason : "Init:" + reason;

  private static JsonObject? FindNamed(JsonArray? items, string name) {
    if (items is null || string.IsNullOrEmpty(name))
      return null;

    return items.OfType<JsonObject>().FirstOrDefault(item => Text(item["name"]) == name);
  }

  private static bool IsRestartableInit(JsonObject? spec) =>
    Text(spec?["restartPolicy"]).Equals("Always", StringComparison.OrdinalIgnoreCase);

  private static bool IsTerminal(string phase) =>
    phase.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
    || phase.Equals("Failed", StringComparison.OrdinalIgnoreCase);

  private static bool HasCondition(JsonArray? conditions, string type, string? reason = null, bool? statusTrue = null) {
    var match = conditions?.OfType<JsonObject>().FirstOrDefault(c => Text(c["type"]) == type);
    if (match is null)
      return false;
    if (reason is not null && Text(match["reason"]) != reason)
      return false;
    if (statusTrue is true)
      return IsTrue(match["status"]);
    if (statusTrue is false)
      return !IsTrue(match["status"]);
    return true;
  }

  private static bool IsTrue(JsonNode? node) {
    if (node is not JsonValue value)
      return false;
    if (value.TryGetValue<bool>(out var flag))
      return flag;
    return value.TryGetValue<string>(out var text)
      && text.Equals("True", StringComparison.OrdinalIgnoreCase);
  }

  private static int Int(JsonNode? node) {
    if (node is JsonValue value) {
      if (value.TryGetValue<int>(out var number))
        return number;
      if (value.TryGetValue<long>(out var longer))
        return (int)longer;
    }

    return int.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
      ? parsed
      : 0;
  }

  private static string Text(JsonNode? node) {
    if (node is null)
      return string.Empty;
    if (node is JsonValue value) {
      if (value.TryGetValue<string>(out var text))
        return text ?? string.Empty;
      return value.ToString() ?? string.Empty;
    }

    return node.ToString() ?? string.Empty;
  }
}
