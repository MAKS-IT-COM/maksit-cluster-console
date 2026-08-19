using k8s.Models;


namespace MaksIT.ClusterConsole.Client;

public sealed record NodeUsage(
  string Name,
  ResourceSlice Cpu,
  ResourceSlice Memory,
  ResourceSlice Pods);

public static class ClusterMetrics {
  public static (ResourceSlice Cpu, ResourceSlice Memory, ResourceSlice Pods, IReadOnlyList<NodeUsage> Nodes) From(
    IEnumerable<V1Node> nodes,
    IEnumerable<V1Pod> pods,
    IReadOnlyDictionary<string, ResourceMetrics>? nodeMetrics) {
    var podList = pods.ToList();
    var byNode = podList
      .GroupBy(p => p.Spec?.NodeName ?? "", StringComparer.Ordinal)
      .ToDictionary(g => g.Key, g => (IReadOnlyList<V1Pod>)g.ToList(), StringComparer.Ordinal);

    var nodeUsages = new List<NodeUsage>();
    foreach (var node in nodes) {
      var name = node.Metadata?.Name ?? "";
      byNode.TryGetValue(name, out var scheduled);
      ResourceMetrics? metrics = null;
      nodeMetrics?.TryGetValue(name, out metrics);
      nodeUsages.Add(ForNode(name, node, scheduled ?? [], metrics));
    }

    double cpuUsed = 0, cpuRequests = 0, cpuLimits = 0, cpuAllocatable = 0, cpuCapacity = 0;
    double memoryUsed = 0, memoryRequests = 0, memoryLimits = 0, memoryAllocatable = 0, memoryCapacity = 0;
    double podUsed = 0, podAllocatable = 0, podCapacity = 0;
    foreach (var usage in nodeUsages) {
      cpuUsed += usage.Cpu.Used;
      cpuRequests += usage.Cpu.Requests;
      cpuLimits += usage.Cpu.Limits;
      cpuAllocatable += usage.Cpu.Allocatable;
      cpuCapacity += usage.Cpu.Capacity;
      memoryUsed += usage.Memory.Used;
      memoryRequests += usage.Memory.Requests;
      memoryLimits += usage.Memory.Limits;
      memoryAllocatable += usage.Memory.Allocatable;
      memoryCapacity += usage.Memory.Capacity;
      podUsed += usage.Pods.Used;
      podAllocatable += usage.Pods.Allocatable;
      podCapacity += usage.Pods.Capacity;
    }

    if (byNode.TryGetValue("", out var unscheduled)) {
      foreach (var pod in unscheduled) {
        podUsed++;
        if (pod.Status?.Phase is "Succeeded" or "Failed")
          continue;
        AddPod(pod, ref cpuRequests, ref cpuLimits, ref memoryRequests, ref memoryLimits);
      }
    }

    return (
      new ResourceSlice(cpuUsed, cpuRequests, cpuLimits, cpuAllocatable, cpuCapacity, "cpu"),
      new ResourceSlice(memoryUsed, memoryRequests, memoryLimits, memoryAllocatable, memoryCapacity, "memory"),
      new ResourceSlice(podUsed, 0, 0, podAllocatable, podCapacity, "pods"),
      nodeUsages.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList());
  }

  private static NodeUsage ForNode(
    string name,
    V1Node node,
    IReadOnlyList<V1Pod> pods,
    ResourceMetrics? metrics) {
    double cpuAllocatable = 0, cpuCapacity = 0, cpuRequests = 0, cpuLimits = 0;
    double memoryAllocatable = 0, memoryCapacity = 0, memoryRequests = 0, memoryLimits = 0;
    double podAllocatable = 0, podCapacity = 0;
    AddNode(node.Status?.Capacity, ref cpuCapacity, ref memoryCapacity, ref podCapacity);
    AddNode(node.Status?.Allocatable, ref cpuAllocatable, ref memoryAllocatable, ref podAllocatable);

    var cpuUsed = metrics is null ? 0 : KubeQuantity.ToCores(metrics.Cpu);
    var memoryUsed = metrics is null ? 0 : KubeQuantity.ToBytes(metrics.Memory);
    foreach (var pod in pods) {
      if (pod.Status?.Phase is "Succeeded" or "Failed")
        continue;
      AddPod(pod, ref cpuRequests, ref cpuLimits, ref memoryRequests, ref memoryLimits);
    }

    return new NodeUsage(
      name,
      new ResourceSlice(cpuUsed, cpuRequests, cpuLimits, cpuAllocatable, cpuCapacity, "cpu"),
      new ResourceSlice(memoryUsed, memoryRequests, memoryLimits, memoryAllocatable, memoryCapacity, "memory"),
      new ResourceSlice(pods.Count, 0, 0, podAllocatable, podCapacity, "pods"));
  }

  private static void AddPod(V1Pod pod, ref double cpuRequests, ref double cpuLimits, ref double memoryRequests, ref double memoryLimits) {
    AddContainers(pod.Spec?.Containers, ref cpuRequests, ref cpuLimits, ref memoryRequests, ref memoryLimits);
    AddContainers(pod.Spec?.InitContainers, ref cpuRequests, ref cpuLimits, ref memoryRequests, ref memoryLimits);
    AddCpuMemory(pod.Spec?.Overhead, ref cpuRequests, ref memoryRequests);
  }

  private static void AddContainers(
    IList<V1Container>? containers,
    ref double cpuRequests,
    ref double cpuLimits,
    ref double memoryRequests,
    ref double memoryLimits) {
    if (containers is null)
      return;

    foreach (var container in containers) {
      AddCpuMemory(container.Resources?.Requests, ref cpuRequests, ref memoryRequests);
      AddCpuMemory(container.Resources?.Limits, ref cpuLimits, ref memoryLimits);
    }
  }

  private static void AddNode(
    IDictionary<string, ResourceQuantity>? quantities,
    ref double cpu,
    ref double memory,
    ref double pods) {
    AddCpuMemory(quantities, ref cpu, ref memory);
    if (quantities is not null && quantities.TryGetValue("pods", out var podQty))
      pods += KubeQuantity.ToCores(podQty.ToString());
  }

  private static void AddCpuMemory(
    IDictionary<string, ResourceQuantity>? quantities,
    ref double cpu,
    ref double memory) {
    if (quantities is null)
      return;

    if (quantities.TryGetValue("cpu", out var cpuQty))
      cpu += KubeQuantity.ToCores(cpuQty.ToString());
    if (quantities.TryGetValue("memory", out var memQty))
      memory += KubeQuantity.ToBytes(memQty.ToString());
  }
}
