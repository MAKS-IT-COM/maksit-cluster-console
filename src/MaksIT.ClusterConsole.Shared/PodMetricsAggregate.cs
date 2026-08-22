using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Shared;

public static class PodMetricsAggregate {
  public static readonly IReadOnlySet<string> WorkloadResourceIds = new HashSet<string>(StringComparer.Ordinal) {
    "deployments",
    "statefulsets",
    "daemonsets"
  };

  public static ApplicationUsage SumForOwner(
    JsonObject owner,
    IEnumerable<JsonObject> pods,
    IReadOnlyDictionary<string, ResourceMetrics> metrics) {
    var cpu = 0d;
    long memory = 0;
    foreach (var pod in pods) {
      if (!ResourceOwnership.Owns(pod, owner))
        continue;
      if (pod["status"]?["phase"]?.GetValue<string>() is "Succeeded" or "Failed")
        continue;

      var key = $"{JsonPath.Namespace(pod)}/{JsonPath.Name(pod)}";
      if (!metrics.TryGetValue(key, out var podMetrics))
        continue;

      cpu += KubeQuantity.ToCores(podMetrics.Cpu);
      memory += KubeQuantity.ToBytes(podMetrics.Memory);
    }

    return new ApplicationUsage(cpu, memory);
  }

  public static ResourceMetrics? ToDisplayMetrics(ApplicationUsage usage, bool metricsAvailable) {
    if (!metricsAvailable)
      return null;
    if (usage.CpuCores <= 0 && usage.MemoryBytes <= 0)
      return new ResourceMetrics("", null, "-", "-");

    return new ResourceMetrics(
      "",
      null,
      usage.CpuCores <= 0 ? "-" : KubeQuantity.FormatCores(usage.CpuCores),
      usage.MemoryBytes <= 0 ? "-" : KubeQuantity.FormatMemoryQuantity(usage.MemoryBytes));
  }

  public static IReadOnlyDictionary<string, string> DeploymentByReplicaSet(IEnumerable<JsonObject> replicaSets) {
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var replicaSet in replicaSets) {
      var ns = JsonPath.Namespace(replicaSet) ?? "";
      var name = JsonPath.Name(replicaSet);
      if (string.IsNullOrEmpty(name))
        continue;

      var deploy = (replicaSet["metadata"]?["ownerReferences"] as JsonArray)?
        .OfType<JsonObject>()
        .FirstOrDefault(o =>
          string.Equals(o["kind"]?.ToString(), "Deployment", StringComparison.Ordinal)
          && o["controller"]?.GetValue<bool?>() == true)
        ?["name"]?.ToString();
      if (!string.IsNullOrEmpty(deploy))
        map[$"{ns}/{name}"] = deploy;
    }

    return map;
  }
}
