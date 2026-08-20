using System.Globalization;
using System.Text.Json.Nodes;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public sealed record PortForwardTarget(string PodName, string Namespace, int ContainerPort, int RequestedPort) {
  public PortForwardTarget(string podName, string @namespace, int containerPort)
    : this(podName, @namespace, containerPort, containerPort) {
  }
}

public static class ServicePortForward {
  public static bool IsService(JsonObject? document) =>
    string.Equals(document?["kind"]?.GetValue<string>(), "Service", StringComparison.OrdinalIgnoreCase);

  public static int? DefaultPort(JsonObject? service) {
    var first = (service?["spec"]?["ports"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
    return first is not null && TryNumber(first["port"], out var port) ? port : null;
  }

  private static readonly HashSet<string> VolatileLabelKeys = new(StringComparer.Ordinal) {
    "pod-template-hash",
    "controller-revision-hash",
    "statefulset.kubernetes.io/pod-name",
    "apps.kubernetes.io/pod-index",
    "batch.kubernetes.io/job-completion-index"
  };

  public static ResourceRow? PickPod(IReadOnlyList<ResourceRow> pods, ResourceRow? preferred) {
    if (pods.Count == 0)
      return null;

    if (preferred is not null
        && IsRunning(preferred)
        && pods.Any(p => IsSamePod(p, preferred)))
      return preferred;

    return pods.FirstOrDefault(IsRunning) ?? pods[0];
  }

  public static ResourceRow? PickRunning(
    IReadOnlyList<ResourceRow> pods,
    string? preferredName,
    IReadOnlyDictionary<string, string>? labels = null) {
    var pool = labels is { Count: > 0 }
      ? pods.Where(p => HasLabels(p.Document, labels)).ToList()
      : pods.ToList();
    if (pool.Count == 0)
      return null;

    if (!string.IsNullOrWhiteSpace(preferredName)) {
      var preferred = pool.FirstOrDefault(p =>
        string.Equals(p.Name, preferredName, StringComparison.Ordinal) && IsRunning(p));
      if (preferred is not null)
        return preferred;
    }

    return pool.FirstOrDefault(IsRunning);
  }

  public static Dictionary<string, string>? StableLabels(JsonObject? document) {
    var selector = ResourceOwnership.SelectorLabels(document);
    if (selector is not null && selector.Count > 0)
      return ToLabels(selector, stripVolatile: false);

    var labels = document?["metadata"]?["labels"] as JsonObject;
    return ToLabels(labels, stripVolatile: true);
  }

  public static bool HasLabels(JsonObject? document, IReadOnlyDictionary<string, string> required) {
    if (required.Count == 0)
      return true;

    var labels = document?["metadata"]?["labels"] as JsonObject;
    if (labels is null)
      return false;

    foreach (var pair in required) {
      if (!string.Equals(JsonPath.Text(labels[pair.Key]), pair.Value, StringComparison.Ordinal))
        return false;
    }

    return true;
  }

  public static Result<int> MapPort(JsonObject service, JsonObject? pod, int remotePort) {
    var ports = service["spec"]?["ports"] as JsonArray;
    var match = ports?.OfType<JsonObject>().FirstOrDefault(p =>
      TryNumber(p["port"], out var port) && port == remotePort);
    if (match is null)
      return Result<int>.Ok(remotePort);

    var target = match["targetPort"];
    if (target is null)
      return Result<int>.Ok(remotePort);

    if (TryNumber(target, out var number))
      return Result<int>.Ok(number);

    var name = JsonPath.Text(target);
    if (string.IsNullOrWhiteSpace(name))
      return Result<int>.Ok(remotePort);

    var containerPort = FindNamedContainerPort(pod, name);
    if (containerPort is null)
      return Result<int>.BadRequest(0, $"service targetPort '{name}' was not found on the selected pod");

    return Result<int>.Ok(containerPort.Value);
  }

  public static Result<PortForwardTarget> Resolve(
    JsonObject service,
    IReadOnlyList<ResourceRow> pods,
    ResourceRow? preferred,
    int remotePort) {
    var selector = ResourceOwnership.SelectorLabels(service);
    if (selector is null || selector.Count == 0)
      return Result<PortForwardTarget>.BadRequest(null, "Cannot port-forward a Service without a selector.");

    var pod = PickForwardPod(pods, preferred, service, remotePort);
    if (pod is null)
      return Result<PortForwardTarget>.NotFound(null, "No pods match this Service selector.");

    var mapped = MapPort(service, pod.Document, remotePort);
    if (!mapped.IsSuccess)
      return new Result<PortForwardTarget>(null, false, mapped.Messages, mapped.StatusCode);

    var ns = pod.Namespace ?? JsonPath.Namespace(service) ?? "default";
    return Result<PortForwardTarget>.Ok(new PortForwardTarget(pod.Name, ns, mapped.Value, remotePort));
  }

  private static ResourceRow? PickForwardPod(
    IReadOnlyList<ResourceRow> pods,
    ResourceRow? preferred,
    JsonObject service,
    int remotePort) {
    ResourceRow? mappedFallback = null;
    foreach (var candidate in OrderedPods(pods, preferred)) {
      if (!MapPort(service, candidate.Document, remotePort).IsSuccess)
        continue;

      if (IsSamePod(candidate, preferred) && IsRunning(candidate))
        return candidate;

      if (IsRunning(candidate))
        return candidate;

      mappedFallback ??= candidate;
    }

    return mappedFallback ?? PickPod(pods, preferred);
  }

  private static IEnumerable<ResourceRow> OrderedPods(IReadOnlyList<ResourceRow> pods, ResourceRow? preferred) {
    if (preferred is not null && pods.Any(p => IsSamePod(p, preferred)))
      yield return preferred;

    foreach (var pod in pods) {
      if (IsSamePod(pod, preferred))
        continue;

      yield return pod;
    }
  }

  private static bool IsRunning(ResourceRow pod) =>
    PodStatus.Of(pod.Document).Equals("Running", StringComparison.OrdinalIgnoreCase);

  private static bool IsSamePod(ResourceRow pod, ResourceRow? other) =>
    other is not null
    && (string.Equals(pod.Uid, other.Uid, StringComparison.Ordinal)
      || (pod.Name == other.Name && pod.Namespace == other.Namespace));

  private static Dictionary<string, string>? ToLabels(JsonObject? labels, bool stripVolatile) {
    if (labels is null || labels.Count == 0)
      return null;

    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in labels) {
      if (stripVolatile && VolatileLabelKeys.Contains(pair.Key))
        continue;
      var value = JsonPath.Text(pair.Value);
      if (!string.IsNullOrEmpty(value))
        result[pair.Key] = value;
    }

    return result.Count == 0 ? null : result;
  }

  private static int? FindNamedContainerPort(JsonObject? pod, string name) {
    if (pod?["spec"] is not JsonObject spec)
      return null;

    foreach (var field in new[] { "containers", "initContainers", "ephemeralContainers" }) {
      if (spec[field] is not JsonArray containers)
        continue;

      foreach (var container in containers.OfType<JsonObject>()) {
        if (container["ports"] is not JsonArray ports)
          continue;

        foreach (var port in ports.OfType<JsonObject>()) {
          if (!string.Equals(JsonPath.Text(port["name"]), name, StringComparison.Ordinal))
            continue;
          if (TryNumber(port["containerPort"], out var number))
            return number;
        }
      }
    }

    return null;
  }

  private static bool TryNumber(JsonNode? node, out int value) {
    value = 0;
    if (node is not JsonValue json)
      return false;
    if (json.TryGetValue<int>(out value) && value > 0)
      return true;
    if (json.TryGetValue<long>(out var longer) && longer > 0 && longer <= 65535) {
      value = (int)longer;
      return true;
    }

    return json.TryGetValue<string>(out var text)
      && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
      && value > 0;
  }
}
