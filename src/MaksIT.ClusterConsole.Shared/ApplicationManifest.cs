using System.Globalization;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class ApplicationManifest {
  public const string NameKey = "app.kubernetes.io/name";
  public const string InstanceKey = "app.kubernetes.io/instance";
  public const string VersionKey = "app.kubernetes.io/version";
  public const string ManagedByKey = "app.kubernetes.io/managed-by";

  public static bool HasManifest(JsonObject item) {
    var labels = Labels(item);
    return !string.IsNullOrWhiteSpace(Read(labels, NameKey))
      || !string.IsNullOrWhiteSpace(Read(labels, InstanceKey));
  }

  public static string GroupKey(JsonObject item) {
    var ns = JsonPath.Namespace(item) ?? "";
    var labels = Labels(item);
    var instance = Read(labels, InstanceKey) ?? Read(labels, NameKey) ?? JsonPath.Name(item);
    return $"{ns}\0{instance}";
  }

  public static IReadOnlyList<JsonObject> Collapse(IEnumerable<JsonObject> items) =>
    items
      .Where(HasManifest)
      .GroupBy(GroupKey, StringComparer.Ordinal)
      .Select(CollapseGroup)
      .ToList();

  public static bool SameInstance(JsonObject left, JsonObject right) {
    var leftNs = JsonPath.Namespace(left);
    var rightNs = JsonPath.Namespace(right);
    if (!string.Equals(leftNs, rightNs, StringComparison.Ordinal))
      return false;

    var leftLabels = Labels(left);
    var rightLabels = Labels(right);
    var instance = Read(leftLabels, InstanceKey);
    if (!string.IsNullOrWhiteSpace(instance) && instance == Read(rightLabels, InstanceKey))
      return true;

    var name = Read(leftLabels, NameKey);
    return !string.IsNullOrWhiteSpace(name) && name == Read(rightLabels, NameKey);
  }

  public static IReadOnlyDictionary<string, string> Cells(JsonObject item) {
    var labels = Labels(item);
    var kind = item["kind"]?.GetValue<string>() ?? "Application";
    var instance = Read(labels, InstanceKey)
      ?? Read(labels, NameKey)
      ?? JsonPath.Name(item);
    return new Dictionary<string, string>(StringComparer.Ordinal) {
      ["Instance"] = instance,
      ["Namespace"] = JsonPath.Namespace(item) ?? "",
      ["Managed by"] = Read(labels, ManagedByKey) ?? "",
      ["Version"] = Read(labels, VersionKey) ?? "",
      ["Ready"] = Ready(item, kind),
      ["Status"] = Status(item, kind),
      ["Age"] = JsonPath.Read(item, "metadata.creationTimestamp")
    };
  }

  public static JsonObject? Labels(JsonObject item) {
    var meta = item["metadata"]?["labels"] as JsonObject;
    var template = item["spec"]?["template"]?["metadata"]?["labels"] as JsonObject;
    if (meta is null)
      return template;
    if (template is null)
      return meta;

    var merged = new JsonObject();
    Copy(template, merged);
    Copy(meta, merged);
    return merged;
  }

  public static IReadOnlyList<(string Kind, string Name)> Workloads(JsonObject? item) {
    var listed = new List<(string Kind, string Name)>();
    var workloads = item?["spec"]?["workloads"] as JsonArray;
    if (workloads is null)
      return listed;

    foreach (var workload in workloads.OfType<JsonObject>()) {
      var kind = JsonPath.Text(workload["kind"]);
      if (string.IsNullOrWhiteSpace(kind))
        kind = "Workload";

      listed.Add((kind, JsonPath.Text(workload["name"])));
    }

    return listed;
  }

  public static IReadOnlyList<string> WorkloadNames(JsonObject? item) =>
    Workloads(item)
      .Select(workload => workload.Name)
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .ToList();

  private static JsonObject CollapseGroup(IGrouping<string, JsonObject> group) {
    var members = group
      .OrderBy(m => JsonPath.Name(m), StringComparer.OrdinalIgnoreCase)
      .ToList();
    var first = members[0];
    var labels = Labels(first);
    var instance = Read(labels, InstanceKey) ?? Read(labels, NameKey) ?? JsonPath.Name(first);
    var ns = JsonPath.Namespace(first) ?? "";
    var names = DistinctLabels(members, NameKey);
    var managed = DistinctLabels(members, ManagedByKey);
    var versions = DistinctLabels(members, VersionKey);

    var ready = 0;
    var desired = 0;
    string? oldestRaw = null;
    DateTimeOffset? oldest = null;
    var workloads = new JsonArray();
    foreach (var member in members) {
      AddReady(member, ref ready, ref desired);
      var stamp = member["metadata"]?["creationTimestamp"]?.ToString();
      if (DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created)
          && (oldest is null || created < oldest)) {
        oldest = created;
        oldestRaw = stamp;
      }

      workloads.Add(new JsonObject {
        ["apiVersion"] = member["apiVersion"]?.DeepClone(),
        ["kind"] = member["kind"]?.DeepClone(),
        ["name"] = JsonPath.Name(member),
        ["namespace"] = JsonPath.Namespace(member)
      });
    }

    var appLabels = new JsonObject { [InstanceKey] = instance };
    if (names.Count == 1)
      appLabels[NameKey] = names[0];
    if (managed.Count > 0)
      appLabels[ManagedByKey] = string.Join(", ", managed);
    if (versions.Count > 0)
      appLabels[VersionKey] = string.Join(", ", versions);

    return new JsonObject {
      ["apiVersion"] = "v1",
      ["kind"] = "Application",
      ["metadata"] = new JsonObject {
        ["name"] = instance,
        ["namespace"] = ns,
        ["uid"] = $"app:{ns}/{instance}",
        ["creationTimestamp"] = oldestRaw,
        ["labels"] = appLabels
      },
      ["spec"] = new JsonObject {
        ["replicas"] = desired,
        ["workloads"] = workloads
      },
      ["status"] = new JsonObject {
        ["readyReplicas"] = ready
      }
    };
  }

  private static IReadOnlyList<string> DistinctLabels(IEnumerable<JsonObject> members, string key) {
    var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var member in members) {
      var value = Read(Labels(member), key);
      if (!string.IsNullOrWhiteSpace(value))
        values.Add(value);
    }

    return values.ToList();
  }

  private static void AddReady(JsonObject item, ref int ready, ref int desired) {
    var kind = item["kind"]?.GetValue<string>() ?? "";
    if (kind.Equals("DaemonSet", StringComparison.OrdinalIgnoreCase)) {
      ready += Int(item["status"]?["numberReady"]);
      desired += Int(item["status"]?["desiredNumberScheduled"]);
      return;
    }

    ready += Int(item["status"]?["readyReplicas"]);
    desired += Int(item["spec"]?["replicas"], 1);
  }

  private static void Copy(JsonObject source, JsonObject dest) {
    foreach (var pair in source) {
      if (pair.Value is not null)
        dest[pair.Key] = pair.Value.DeepClone();
    }
  }

  private static string? Read(JsonObject? labels, string key) {
    var node = labels?[key];
    if (node is null)
      return null;

    var text = node is JsonValue value && value.TryGetValue<string>(out var typed)
      ? typed
      : node.ToString();
    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
  }

  private static string Ready(JsonObject item, string kind) {
    if (kind.Equals("DaemonSet", StringComparison.OrdinalIgnoreCase))
      return $"{Int(item["status"]?["numberReady"])}/{Int(item["status"]?["desiredNumberScheduled"])}";

    var ready = Int(item["status"]?["readyReplicas"]);
    var desired = Int(item["spec"]?["replicas"], 1);
    return $"{ready}/{desired}";
  }

  private static string Status(JsonObject item, string kind) {
    if (kind.Equals("DaemonSet", StringComparison.OrdinalIgnoreCase)) {
      var ready = Int(item["status"]?["numberReady"]);
      var desired = Int(item["status"]?["desiredNumberScheduled"]);
      if (desired <= 0)
        return "Pending";
      if (ready >= desired)
        return "Running";
      if (ready == 0)
        return "Pending";
      return "Progressing";
    }

    var readyReplicas = Int(item["status"]?["readyReplicas"]);
    var replicas = Int(item["spec"]?["replicas"], 1);
    if (replicas <= 0)
      return "Stopped";
    if (readyReplicas >= replicas)
      return "Running";
    if (readyReplicas == 0)
      return "Pending";
    return "Progressing";
  }

  private static int Int(JsonNode? node, int fallback = 0) {
    if (node is JsonValue value) {
      if (value.TryGetValue<int>(out var number))
        return number;
      if (value.TryGetValue<long>(out var longer))
        return (int)longer;
    }

    return int.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
      ? parsed
      : fallback;
  }
}
