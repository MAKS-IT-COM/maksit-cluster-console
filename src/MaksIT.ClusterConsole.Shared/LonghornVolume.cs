using System.Globalization;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

/// <summary>
/// Longhorn volume columns and the leftover-attachment case the volume list is meant to show.
/// A backup or a reinstall can leave a degraded volume attached while the new volume stays detached.
/// </summary>
public static class LonghornVolume {
  public static string Column(JsonObject? item, string path) =>
    path switch {
      "longhorn.size" => Size(item?["spec"]?["size"]),
      "longhorn.actualSize" => ActualSize(item),
      "longhorn.state" => State(item),
      "longhorn.created" => When(item?["metadata"]?["creationTimestamp"]),
      "longhorn.dataEngine" => Engine(item),
      "longhorn.pv" => PvPvc(item),
      "longhorn.workloadNamespace" => Text(item?["status"]?["kubernetesStatus"]?["namespace"]),
      "longhorn.volumeName" => Text(item?["status"]?["kubernetesStatus"]?["pvcName"]),
      "longhorn.node" => Node(item),
      "longhorn.schedule" => Schedule(item),
      "longhorn.lastBackupAt" => When(item?["status"]?["lastBackupAt"]),
      _ => ""
    };

  public static IReadOnlyDictionary<string, string> Tips(JsonObject? item) {
    var tips = new Dictionary<string, string>(StringComparer.Ordinal);
    var pv = PvTip(item);

    if (pv.Length > 0)
      tips["PV/PVC"] = pv;

    var robustness = Text(item?["status"]?["robustness"]);

    if (robustness.Equals("degraded", StringComparison.OrdinalIgnoreCase)
        || robustness.Equals("faulted", StringComparison.OrdinalIgnoreCase))
      tips["State"] = "Robustness: " + robustness + ". A replica is not healthy.";

    var workloads = Workloads(item);

    if (workloads.Length > 0)
      tips["Node"] = "Workloads: " + workloads;

    return tips;
  }

  public static bool CanAttach(JsonObject volume) {
    var state = Text(volume["status"]?["state"]);

    return string.IsNullOrEmpty(state)
      || state.Equals("detached", StringComparison.OrdinalIgnoreCase);
  }

  public static bool CanDetach(JsonObject volume) {
    var state = Text(volume["status"]?["state"]);

    return state.Equals("attached", StringComparison.OrdinalIgnoreCase)
      || state.Equals("attaching", StringComparison.OrdinalIgnoreCase);
  }

  public static bool MaintenanceAttach(JsonObject volume) =>
    Text(volume["spec"]?["accessMode"]).Equals("rwx", StringComparison.OrdinalIgnoreCase);

  public static string? PodDetachWarning(JsonObject volume) {
    var status = volume["status"]?["kubernetesStatus"] as JsonObject;
    var workloads = status?["workloadsStatus"] as JsonArray;

    if (workloads is not { Count: > 0 })
      return null;

    if (!string.IsNullOrWhiteSpace(Text(status?["lastPodRefAt"])))
      return null;

    return "A pod is using "
      + JsonPath.Name(volume)
      + ". Detaching it can crash that pod, and Kubernetes cannot use the volume again until the pod is deleted.";
  }

  public static IReadOnlyList<string> ReadyNodes(IEnumerable<JsonObject> nodes) =>
    nodes
      .Where(IsReady)
      .Select(JsonPath.Name)
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal)
      .ToList();

  public static IReadOnlyList<ClusterIssue> Issues(
    IEnumerable<JsonObject> volumes,
    DateTimeOffset? utcNow = null,
    IEnumerable<JsonObject>? replicas = null) {
    var now = utcNow ?? DateTimeOffset.UtcNow;
    var parsed = volumes.Select(volume => SnapshotOf(volume, now)).ToList();
    var failedReplicas = FailedReplicas(replicas, parsed);
    var covered = new HashSet<string>(StringComparer.Ordinal);
    var pairs = new HashSet<string>(StringComparer.Ordinal);
    var issues = new List<ClusterIssue>();

    void Emit(Snapshot old, Snapshot fresh) {
      if (old.Uid == fresh.Uid)
        return;

      if (!pairs.Add(old.Uid + "|" + fresh.Uid))
        return;

      covered.Add(old.Uid);
      covered.Add(fresh.Uid);
      var faulted = IsFaulted(old.Robustness) || IsFaulted(fresh.Robustness);
      issues.Add(new ClusterIssue(
        $"longhorn/{old.Uid}/{fresh.Uid}/conflict",
        ConflictMessage(old, fresh, ReplicaClause(failedReplicas, old.Name)),
        old.Name + " → " + fresh.Name,
        "Volume",
        JsonPath.Age(old.Created, now),
        old.Created,
        faulted ? "Error" : "Warning",
        ClusterIssues.Active));
    }

    foreach (var group in parsed.Where(volume => volume.Claim.Length > 0).GroupBy(volume => volume.Claim, StringComparer.Ordinal)) {
      var blocking = group.Where(volume => IsUnhealthy(volume.Robustness) && IsBlocking(volume.State)).ToList();
      var detached = group
        .Where(volume => volume.State.Equals("detached", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(volume => volume.Created)
        .ToList();

      if (blocking.Count == 0 || detached.Count == 0)
        continue;

      foreach (var old in blocking)
        Emit(old, detached[0]);
    }

    foreach (var fresh in parsed.Where(volume => volume.HasBackup && IsDetached(volume.State)).OrderByDescending(volume => volume.Created)) {
      var named = fresh.BackupVolume.Length == 0
        ? null
        : parsed.FirstOrDefault(volume =>
          volume.Name.Equals(fresh.BackupVolume, StringComparison.Ordinal)
          && IsUnhealthy(volume.Robustness)
          && IsBlocking(volume.State));

      if (named is not null)
        Emit(named, fresh);
    }

    var stillBlocking = parsed.Where(volume => IsUnhealthy(volume.Robustness) && IsBlocking(volume.State) && !covered.Contains(volume.Uid)).ToList();
    var stillRestored = parsed
      .Where(volume => volume.HasBackup && volume.BackupVolume.Length == 0 && IsDetached(volume.State) && !covered.Contains(volume.Uid))
      .OrderByDescending(volume => volume.Created)
      .ToList();

    if (stillBlocking.Count == 1 && stillRestored.Count > 0)
      Emit(stillBlocking[0], stillRestored[0]);

    foreach (var volume in parsed) {
      if (!covered.Add(volume.Uid))
        continue;

      if (IsUnhealthy(volume.Robustness)) {
        issues.Add(new ClusterIssue(
          $"longhorn/{volume.Uid}/{volume.Robustness}",
          "Longhorn volume "
            + volume.Name
            + " is "
            + volume.State
            + " and "
            + volume.Robustness
            + "."
            + ReplicaClause(failedReplicas, volume.Name)
            + " If a newer volume for the same PVC, or one restored from this volume, is detached, detach this leftover first, then attach the new volume to a ready node. Do not delete the new volume. If detach leaves it attached, detach it again with force.",
          volume.Name,
          "Volume",
          JsonPath.Age(volume.Created, now),
          volume.Created,
          IsFaulted(volume.Robustness) ? "Error" : "Warning",
          ClusterIssues.Active));

        continue;
      }

      if (volume.Bound && IsDetached(volume.State)) {
        issues.Add(new ClusterIssue(
          $"longhorn/{volume.Uid}/detached",
          "Longhorn volume "
            + volume.Name
            + " is detached while PVC "
            + volume.Claim
            + " is still bound. Attach it to a ready Longhorn node. If a degraded volume for this PVC is still attached, detach that leftover first.",
          volume.Name,
          "Volume",
          JsonPath.Age(volume.Created, now),
          volume.Created,
          "Warning",
          ClusterIssues.Active));
      }
    }

    return issues;
  }

  private static string ConflictMessage(Snapshot old, Snapshot fresh, string replicaClause) {
    var link = fresh.Claim.Length > 0
      ? " for PVC " + fresh.Claim
      : fresh.BackupVolume.Length > 0
        ? " restored from " + fresh.BackupVolume
        : " restored from a backup";

    return "Longhorn volume "
      + old.Name
      + " in "
      + old.Namespace
      + " is "
      + old.State
      + " and "
      + old.Robustness
      + "."
      + replicaClause
      + " Volume "
      + fresh.Name
      + " in "
      + fresh.Namespace
      + " is detached"
      + link
      + ". A leftover from backup or a reinstall is blocking the new volume. Detach "
      + old.Name
      + ", then attach "
      + fresh.Name
      + " to a ready Longhorn node. Do not delete "
      + fresh.Name
      + ". If detach leaves it attached, detach it again with force.";
  }

  private static Snapshot SnapshotOf(JsonObject volume, DateTimeOffset now) {
    JsonPath.TryTimestamp(volume["metadata"]?["creationTimestamp"], out var created);
    var status = volume["status"]?["kubernetesStatus"] as JsonObject;
    var pvc = Text(status?["pvcName"]);
    var ns = Text(status?["namespace"]);
    var claim = !string.IsNullOrEmpty(pvc) && !string.IsNullOrEmpty(ns)
      ? ns + "/" + pvc
      : Text(status?["pvName"]);
    var bound = !string.IsNullOrEmpty(pvc)
      && !string.IsNullOrEmpty(ns)
      && string.IsNullOrWhiteSpace(Text(status?["lastPVCRefAt"]));

    return new Snapshot(
      JsonPath.Name(volume),
      JsonPath.Namespace(volume) ?? "",
      JsonPath.Uid(volume),
      Text(volume["status"]?["state"]),
      Text(volume["status"]?["robustness"]),
      claim,
      bound,
      created == default ? now : created,
      BackupVolumeName(volume),
      !string.IsNullOrWhiteSpace(Text(volume["spec"]?["fromBackup"])));
  }

  private static string BackupVolumeName(JsonObject volume) {
    var raw = Text(volume["spec"]?["fromBackup"]);

    if (string.IsNullOrEmpty(raw))
      return "";

    var query = raw.Contains('?', StringComparison.Ordinal) ? raw[(raw.IndexOf('?', StringComparison.Ordinal) + 1)..] : raw;

    foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries)) {
      var eq = part.IndexOf('=');

      if (eq <= 0)
        continue;

      if (part[..eq].Equals("volume", StringComparison.OrdinalIgnoreCase))
        return Uri.UnescapeDataString(part[(eq + 1)..]);
    }

    return "";
  }

  private static List<ReplicaSnapshot> FailedReplicas(IEnumerable<JsonObject>? replicas, List<Snapshot> volumes) {
    var failed = new List<ReplicaSnapshot>();

    if (replicas is null)
      return failed;

    foreach (var replica in replicas) {
      var volumeName = Text(replica["spec"]?["volumeName"]);
      var volume = volumes.FirstOrDefault(item => item.Name.Equals(volumeName, StringComparison.Ordinal));

      if (volume is null || !IsUnhealthy(volume.Robustness) || !IsBlocking(volume.State))
        continue;

      var state = Text(replica["status"]?["currentState"]);
      var failedAt = Text(replica["spec"]?["failedAt"]);
      var running = state.Equals("running", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(failedAt);

      if (running)
        continue;

      var condition = "not running";

      if (!string.IsNullOrWhiteSpace(failedAt))
        condition = "failed";
      else if (!string.IsNullOrEmpty(state))
        condition = state;

      failed.Add(new ReplicaSnapshot(JsonPath.Name(replica), volumeName, condition));
    }

    return failed;
  }

  private static string ReplicaClause(IReadOnlyList<ReplicaSnapshot> replicas, string volumeName) {
    var mine = replicas.Where(replica => replica.VolumeName.Equals(volumeName, StringComparison.Ordinal)).ToList();

    if (mine.Count == 0)
      return "";

    return " Replica "
      + string.Join(", ", mine.Select(replica => replica.Name + " is " + replica.Condition))
      + ".";
  }

  private static bool IsReady(JsonObject node) {
    var conditions = node["status"]?["conditions"];

    if (conditions is JsonArray array) {
      var condition = array.OfType<JsonObject>().FirstOrDefault(item => Text(item["type"]) == "Ready");

      return Text(condition?["status"]).Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    if (conditions is JsonObject map && map["Ready"] is JsonObject status)
      return Text(status["status"]).Equals("True", StringComparison.OrdinalIgnoreCase);

    return false;
  }

  private static bool IsDetached(string state) =>
    state.Equals("detached", StringComparison.OrdinalIgnoreCase);

  private static bool IsBlocking(string state) =>
    state.Equals("attached", StringComparison.OrdinalIgnoreCase)
    || state.Equals("attaching", StringComparison.OrdinalIgnoreCase);

  private static bool IsUnhealthy(string robustness) =>
    robustness.Equals("degraded", StringComparison.OrdinalIgnoreCase)
    || robustness.Equals("faulted", StringComparison.OrdinalIgnoreCase);

  private static bool IsFaulted(string robustness) =>
    robustness.Equals("faulted", StringComparison.OrdinalIgnoreCase);

  private static string State(JsonObject? item) {
    var state = Text(item?["status"]?["state"]);
    var robustness = Text(item?["status"]?["robustness"]);

    if (string.IsNullOrEmpty(robustness) || robustness.Equals("healthy", StringComparison.OrdinalIgnoreCase))
      return state;

    if (string.IsNullOrEmpty(state))
      return robustness;

    return state + " · " + robustness;
  }

  private static string Engine(JsonObject? item) {
    if (item is null)
      return "";

    var engine = Text(item["spec"]?["dataEngine"]);

    return string.IsNullOrWhiteSpace(engine) ? "v1" : engine;
  }

  private static string PvPvc(JsonObject? item) {
    var status = item?["status"]?["kubernetesStatus"] as JsonObject;

    if (status is null)
      return "";

    var pv = Text(status["pvName"]);

    if (string.IsNullOrEmpty(pv))
      return "";

    var pvc = Text(status["pvcName"]);
    var ns = Text(status["namespace"]);

    if (string.IsNullOrEmpty(pvc) || string.IsNullOrEmpty(ns))
      return "Available";

    if (!string.IsNullOrWhiteSpace(Text(status["lastPVCRefAt"])))
      return "Released";

    return "Bound";
  }

  private static string Node(JsonObject? item) {
    var node = Text(item?["status"]?["currentNodeID"]);

    if (string.IsNullOrEmpty(node))
      node = Text(item?["spec"]?["nodeID"]);

    return node;
  }

  private static string Workloads(JsonObject? item) {
    var pods = new List<string>();
    var workloads = item?["status"]?["kubernetesStatus"]?["workloadsStatus"] as JsonArray;

    if (workloads is not null) {
      foreach (var workload in workloads.OfType<JsonObject>()) {
        var pod = Text(workload["podName"]);

        if (!string.IsNullOrEmpty(pod))
          pods.Add(pod);
      }
    }

    var shown = pods.Take(3).ToList();
    var extra = pods.Count - shown.Count;
    var podsText = string.Join(", ", shown);

    if (extra > 0)
      podsText += " +" + extra.ToString(CultureInfo.InvariantCulture);

    return podsText;
  }

  private static string Schedule(JsonObject? item) {
    var names = new List<string>();
    AddJobNames(names, item?["spec"]?["recurringJobs"] as JsonArray);
    AddJobNames(names, item?["spec"]?["recurringJobSelector"] as JsonArray);
    AddScheduleLabels(names, item?["metadata"]?["labels"] as JsonObject);

    return string.Join(", ", names.Distinct(StringComparer.Ordinal));
  }

  private static void AddScheduleLabels(List<string> names, JsonObject? labels) {
    if (labels is null)
      return;

    const string prefix = "recurring-job.longhorn.io/";

    foreach (var pair in labels) {
      if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
        continue;

      var value = Text(pair.Value);

      if (value.Equals("disabled", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
        continue;

      var name = pair.Key[prefix.Length..];

      if (!string.IsNullOrWhiteSpace(name))
        names.Add(name);
    }
  }

  private static void AddJobNames(List<string> names, JsonArray? jobs) {
    if (jobs is null)
      return;

    foreach (var job in jobs) {
      var name = job is JsonObject obj ? Text(obj["name"]) : Text(job);

      if (!string.IsNullOrWhiteSpace(name))
        names.Add(name);
    }
  }

  private static string When(JsonNode? node) {
    if (!JsonPath.TryTimestamp(node, out var value))
      return "";

    return value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z";
  }

  private static string ActualSize(JsonObject? item) {
    var controllers = item?["controllers"] as JsonArray;
    var fromController = Size(controllers?.OfType<JsonObject>().FirstOrDefault()?["actualSize"]);

    if (fromController.Length > 0)
      return fromController;

    return Size(item?["status"]?["actualSize"]);
  }

  private static string PvTip(JsonObject? item) {
    var status = item?["status"]?["kubernetesStatus"] as JsonObject;

    if (status is null)
      return "";

    var lines = new List<string>();
    var pv = Text(status["pvName"]);
    var pvStatus = Text(status["pvStatus"]);
    var pvc = Text(status["pvcName"]);
    var ns = Text(status["namespace"]);
    var last = Text(status["lastPVCRefAt"]);

    if (pv.Length > 0)
      lines.Add("PV: " + pv);

    if (pvStatus.Length > 0)
      lines.Add("PV status: " + pvStatus);

    if (pvc.Length > 0)
      lines.Add("PVC: " + pvc);

    if (ns.Length > 0)
      lines.Add("Namespace: " + ns);

    if (last.Length > 0)
      lines.Add("Last PVC ref: " + last);

    return string.Join('\n', lines);
  }

  private static string Size(JsonNode? node) {
    var bytes = ReadBytes(node);

    return bytes <= 0 ? "" : KubeQuantity.FormatBytesCompact(bytes);
  }

  private static long ReadBytes(JsonNode? node) {
    if (node is not JsonValue value)
      return 0;

    if (value.TryGetValue<long>(out var number))
      return number;

    if (value.TryGetValue<int>(out var small))
      return small;

    if (value.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
      return parsed;

    return 0;
  }

  private static string Text(JsonNode? node) =>
    JsonPath.Text(node);

  private sealed record Snapshot(
    string Name,
    string Namespace,
    string Uid,
    string State,
    string Robustness,
    string Claim,
    bool Bound,
    DateTimeOffset Created,
    string BackupVolume,
    bool HasBackup);

  private sealed record ReplicaSnapshot(string Name, string VolumeName, string Condition);
}
