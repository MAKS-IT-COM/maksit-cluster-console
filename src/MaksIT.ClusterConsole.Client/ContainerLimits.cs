using k8s.Models;


namespace MaksIT.ClusterConsole.Client;

public sealed record WorkloadContainerLimit(
  string Namespace,
  string WorkloadKind,
  string WorkloadName,
  string Container,
  bool Init,
  int Pods,
  string CpuRequest,
  string CpuLimit,
  string MemoryRequest,
  string MemoryLimit,
  double CpuLimitCores,
  double MemoryLimitBytes) {
  public double CpuContribution => CpuLimitCores * Pods;

  public double MemoryContribution => MemoryLimitBytes * Pods;

  public string Workload => $"{WorkloadKind}/{WorkloadName}";

  public string ContainerLabel => Init ? $"{Container} (init)" : Container;
}

public static class ContainerLimits {
  public static IReadOnlyList<WorkloadContainerLimit> From(
    IEnumerable<V1Pod> pods,
    IEnumerable<V1ReplicaSet> replicaSets) {
    var deployByReplicaSet = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var rs in replicaSets) {
      var ns = rs.Metadata?.NamespaceProperty ?? "";
      var name = rs.Metadata?.Name ?? "";
      if (string.IsNullOrEmpty(name))
        continue;
      var deploy = rs.Metadata?.OwnerReferences?
        .FirstOrDefault(o => o.Kind == "Deployment" && o.Controller == true)
        ?.Name;
      if (!string.IsNullOrEmpty(deploy))
        deployByReplicaSet[$"{ns}/{name}"] = deploy;
    }

    var grouped = new Dictionary<string, Acc>(StringComparer.Ordinal);
    foreach (var pod in pods) {
      if (pod.Status?.Phase is "Succeeded" or "Failed")
        continue;
      var ns = pod.Metadata?.NamespaceProperty ?? "default";
      var (kind, owner) = ResolveOwner(pod, deployByReplicaSet);
      AddContainers(grouped, ns, kind, owner, pod.Spec?.Containers, false);
      AddContainers(grouped, ns, kind, owner, pod.Spec?.InitContainers, true);
    }

    return grouped.Values
      .Select(a => a.ToRow())
      .OrderByDescending(r => r.CpuContribution)
      .ThenByDescending(r => r.MemoryContribution)
      .ThenBy(r => r.Namespace, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.WorkloadName, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static (string Kind, string Name) ResolveOwner(
    V1Pod pod,
    IReadOnlyDictionary<string, string> deployByReplicaSet) {
    var ns = pod.Metadata?.NamespaceProperty ?? "";
    var controller = pod.Metadata?.OwnerReferences?.FirstOrDefault(o => o.Controller == true);
    if (controller is null)
      return ("Pod", pod.Metadata?.Name ?? "");

    if (controller.Kind == "ReplicaSet") {
      var key = $"{ns}/{controller.Name}";
      if (deployByReplicaSet.TryGetValue(key, out var deploy))
        return ("Deployment", deploy);
    }

    return (controller.Kind, controller.Name);
  }

  private static void AddContainers(
    Dictionary<string, Acc> grouped,
    string ns,
    string kind,
    string owner,
    IList<V1Container>? containers,
    bool init) {
    if (containers is null)
      return;

    foreach (var container in containers) {
      var name = container.Name ?? "";
      if (string.IsNullOrEmpty(name))
        continue;
      var cpuLimit = Qty(container.Resources?.Limits, "cpu");
      var memLimit = Qty(container.Resources?.Limits, "memory");
      if (string.IsNullOrEmpty(cpuLimit) && string.IsNullOrEmpty(memLimit))
        continue;

      var key = $"{ns}/{kind}/{owner}/{name}/{(init ? "i" : "c")}";
      if (!grouped.TryGetValue(key, out var acc)) {
        acc = new Acc(ns, kind, owner, name, init, cpuLimit, memLimit,
          Qty(container.Resources?.Requests, "cpu"),
          Qty(container.Resources?.Requests, "memory"));
        grouped[key] = acc;
      }

      acc.Pods++;
    }
  }

  private static string Qty(IDictionary<string, ResourceQuantity>? quantities, string name) {
    if (quantities is null || !quantities.TryGetValue(name, out var qty))
      return "";
    return qty.ToString() ?? "";
  }

  private sealed class Acc(
    string ns,
    string kind,
    string owner,
    string container,
    bool init,
    string cpuLimit,
    string memLimit,
    string cpuRequest,
    string memRequest) {
    public int Pods { get; set; }

    public WorkloadContainerLimit ToRow() =>
      new(
        ns,
        kind,
        owner,
        container,
        init,
        Pods,
        cpuRequest,
        cpuLimit,
        memRequest,
        memLimit,
        KubeQuantity.ToCores(cpuLimit),
        KubeQuantity.ToBytes(memLimit));
  }
}
