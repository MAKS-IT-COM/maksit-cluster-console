using System.Globalization;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class JsonPath {
  public static string Read(JsonNode? root, string path) {
    if (root is null || string.IsNullOrWhiteSpace(path))
      return string.Empty;

    if (path == "crd.storageVersion")
      return CrdStorageVersion(root as JsonObject);

    if (path == "pod.status")
      return PodStatus.Of(root as JsonObject);

    if (path == "service.externalIP")
      return ServiceExternalIp(root as JsonObject);

    if (path == "pv.claim")
      return VolumeClaim(root as JsonObject);

    if (path == "metadata.creationTimestamp")
      return Age(Walk(root, path)?.ToString());

    if (path == "status.conditions")
      return NodeCondition(root);

    if (path == "metadata.labels")
      return NodeRoles(root);

    if (path == "status.containerStatuses" && path.Contains("Ready", StringComparison.Ordinal) == false)
      return string.Empty;

    var node = Walk(root, path);
    return Format(node, path);
  }

  public static JsonNode? Walk(JsonNode? node, string path) {
    foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
      if (node is JsonObject obj)
        node = obj[part];
      else if (node is JsonArray arr && int.TryParse(part, out var i) && i >= 0 && i < arr.Count)
        node = arr[i];
      else
        return null;
    }

    return node;
  }

  public static string Text(JsonNode? node) {
    if (node is JsonValue value)
      return value.TryGetValue<string>(out var text) ? text ?? string.Empty : value.ToString() ?? string.Empty;

    return node?.ToString() ?? string.Empty;
  }

  public static string Name(JsonObject item) =>
    Text(Property(item["metadata"] as JsonObject ?? item["Metadata"] as JsonObject, "name"));

  public static string? Namespace(JsonObject item) {
    var text = Text(Property(item["metadata"] as JsonObject ?? item["Metadata"] as JsonObject, "namespace"));
    return string.IsNullOrEmpty(text) ? null : text;
  }

  public static string Uid(JsonObject item) {
    var text = Text(Property(item["metadata"] as JsonObject ?? item["Metadata"] as JsonObject, "uid"));
    return string.IsNullOrEmpty(text) ? Name(item) : text;
  }

  private static JsonNode? Property(JsonObject? root, string name) {
    if (root is null)
      return null;
    if (root[name] is { } exact)
      return exact;

    foreach (var kv in root) {
      if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
        return kv.Value;
    }

    return null;
  }

  public static string VolumeClaim(JsonObject? item) {
    if (item is null)
      return string.Empty;

    var claim = item["spec"]?["claimRef"] as JsonObject;
    if (claim is null)
      return string.Empty;

    var name = claim["name"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(name))
      return string.Empty;

    var ns = claim["namespace"]?.GetValue<string>();
    return string.IsNullOrWhiteSpace(ns) ? name : $"{ns}/{name}";
  }

  public static string ServiceExternalIp(JsonObject? item) {
    if (item is null)
      return string.Empty;

    var ips = new List<string>();
    AddUnique(ips, item["spec"]?["loadBalancerIP"]?.GetValue<string>());

    if (item["spec"]?["externalIPs"] is JsonArray external) {
      foreach (var ip in external)
        AddUnique(ips, ip?.GetValue<string>());
    }

    if (item["status"]?["loadBalancer"]?["ingress"] is JsonArray ingress) {
      foreach (var entry in ingress.OfType<JsonObject>()) {
        AddUnique(ips, entry["ip"]?.GetValue<string>());
        AddUnique(ips, entry["hostname"]?.GetValue<string>());
      }
    }

    var annotation = item["metadata"]?["annotations"]?["lbipam.cilium.io/ips"]?.GetValue<string>();
    if (!string.IsNullOrWhiteSpace(annotation)) {
      foreach (var ip in annotation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        AddUnique(ips, ip);
    }

    return string.Join(",", ips);
  }

  private static void AddUnique(List<string> ips, string? value) {
    if (string.IsNullOrWhiteSpace(value))
      return;
    if (!ips.Contains(value, StringComparer.OrdinalIgnoreCase))
      ips.Add(value);
  }

  public static string PodReady(JsonObject item) {
    var statuses = item["status"]?["containerStatuses"] as JsonArray;
    if (statuses is null || statuses.Count == 0)
      return "0/0";

    var ready = statuses.OfType<JsonObject>().Count(c => c["ready"]?.GetValue<bool>() == true);
    return $"{ready}/{statuses.Count}";
  }

  public static string CrdStorageVersion(JsonObject? crd) {
    var versions = crd?["spec"]?["versions"] as JsonArray;
    if (versions is null)
      return string.Empty;

    var stored = versions.OfType<JsonObject>().FirstOrDefault(v => IsTrue(v["storage"]));
    var served = versions.OfType<JsonObject>().FirstOrDefault(v => IsTrue(v["served"]));
    return stored?["name"]?.GetValue<string>()
      ?? served?["name"]?.GetValue<string>()
      ?? versions.OfType<JsonObject>().FirstOrDefault()?["name"]?.GetValue<string>()
      ?? string.Empty;
  }

  public static string PodRestarts(JsonObject item) {
    var statuses = item["status"]?["containerStatuses"] as JsonArray;
    if (statuses is null)
      return "0";

    var sum = statuses.OfType<JsonObject>().Sum(c => c["restartCount"]?.GetValue<int>() ?? 0);
    return sum.ToString(CultureInfo.InvariantCulture);
  }

  public static IReadOnlyList<PodContainer> ListPodContainers(JsonObject? pod) {
    if (pod is null)
      return [];

    var spec = pod["spec"] as JsonObject;
    var status = pod["status"] as JsonObject;
    var items = new List<PodContainer>();
    AddContainers(items, spec?["initContainers"] as JsonArray, status?["initContainerStatuses"] as JsonArray, "Init");
    AddContainers(items, spec?["containers"] as JsonArray, status?["containerStatuses"] as JsonArray, "Container");
    AddContainers(items, spec?["ephemeralContainers"] as JsonArray, status?["ephemeralContainerStatuses"] as JsonArray, "Ephemeral");
    return items;
  }

  private static void AddContainers(
    List<PodContainer> items,
    JsonArray? spec,
    JsonArray? statuses,
    string kind) {
    if (spec is null)
      return;

    var statusByName = (statuses ?? [])
      .OfType<JsonObject>()
      .Select(s => (Name: s["name"]?.GetValue<string>(), Status: s))
      .Where(s => !string.IsNullOrEmpty(s.Name))
      .GroupBy(s => s.Name!, StringComparer.Ordinal)
      .ToDictionary(g => g.Key, g => g.First().Status, StringComparer.Ordinal);

    foreach (var container in spec.OfType<JsonObject>()) {
      var name = container["name"]?.GetValue<string>();
      if (string.IsNullOrEmpty(name))
        continue;

      var restartPolicy = container["restartPolicy"]?.GetValue<string>();
      var resolvedKind = kind == "Init"
        && string.Equals(restartPolicy, "Always", StringComparison.OrdinalIgnoreCase)
        ? "Sidecar"
        : kind;
      statusByName.TryGetValue(name, out var status);
      items.Add(new PodContainer(
        name,
        container["image"]?.ToString() ?? string.Empty,
        resolvedKind,
        status?["ready"]?.GetValue<bool>() == true,
        status?["restartCount"]?.GetValue<int>() ?? 0,
        ContainerState(status)));
    }
  }

  private static string ContainerState(JsonObject? status) {
    var state = status?["state"] as JsonObject;
    if (state is null)
      return string.Empty;

    if (state["running"] is not null)
      return "Running";

    if (state["waiting"] is JsonObject waiting)
      return waiting["reason"]?.ToString() ?? "Waiting";

    if (state["terminated"] is JsonObject terminated)
      return terminated["reason"]?.ToString() ?? "Terminated";

    return string.Empty;
  }

  private static bool IsTrue(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

  private static string Format(JsonNode? node, string path) {
    if (node is null)
      return string.Empty;

    if (path.EndsWith("containerStatuses", StringComparison.Ordinal))
      return string.Empty;

    if (node is JsonValue value)
      return value.ToString() ?? string.Empty;

    if (node is JsonArray array) {
      if (path.EndsWith("ports", StringComparison.Ordinal))
        return string.Join(",", array.OfType<JsonObject>().Select(p => p["port"]?.ToString()).Where(s => !string.IsNullOrEmpty(s)));

      if (path.EndsWith("rules", StringComparison.Ordinal))
        return string.Join(",", array.OfType<JsonObject>().Select(r => r["host"]?.ToString()).Where(s => !string.IsNullOrEmpty(s)));

      if (path.EndsWith("accessModes", StringComparison.Ordinal))
        return string.Join(",", array.Select(a => a?.ToString()));

      if (path.EndsWith("active", StringComparison.Ordinal))
        return array.Count.ToString(CultureInfo.InvariantCulture);

      return array.Count.ToString(CultureInfo.InvariantCulture);
    }

    return node.ToJsonString();
  }

  public static string Age(string? timestamp) =>
    TryTimestamp(timestamp, out var when) ? Age(when) : timestamp ?? string.Empty;

  public static string Age(DateTimeOffset when) =>
    Age(when, DateTimeOffset.UtcNow);

  public static string Age(DateTimeOffset when, DateTimeOffset utcNow) {
    var age = utcNow - when.ToUniversalTime();
    if (age.TotalDays >= 1)
      return $"{(int)age.TotalDays}d";
    if (age.TotalHours >= 1) {
      var hours = (int)age.TotalHours;
      var minutes = age.Minutes;
      return minutes > 0 ? $"{hours}h{minutes}m" : $"{hours}h";
    }

    if (age.TotalMinutes >= 1)
      return $"{(int)age.TotalMinutes}m";
    return $"{Math.Max(0, (int)age.TotalSeconds)}s";
  }

  public static bool TryTimestamp(JsonNode? node, out DateTimeOffset value) {
    value = default;
    return node is not null && TryTimestamp(node.ToString(), out value);
  }

  public static bool TryTimestamp(string? timestamp, out DateTimeOffset value) =>
    DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);

  private static string NodeCondition(JsonNode root) {
    var conditions = Walk(root, "status.conditions") as JsonArray;
    var ready = conditions?.OfType<JsonObject>().FirstOrDefault(c => c["type"]?.ToString() == "Ready");
    return ready?["status"]?.ToString() == "True" ? "Ready" : "NotReady";
  }

  private static string NodeRoles(JsonNode root) {
    var labels = Walk(root, "metadata.labels") as JsonObject;
    if (labels is null)
      return string.Empty;

    var roles = labels
      .Where(p => p.Key.StartsWith("node-role.kubernetes.io/", StringComparison.Ordinal))
      .Select(p => p.Key["node-role.kubernetes.io/".Length..])
      .ToList();
    return roles.Count == 0 ? "worker" : string.Join(",", roles);
  }
}
