namespace MaksIT.ClusterConsole.Client;

public sealed record ClusterSummary(
  string GitVersion,
  string Platform,
  int NodeCount,
  int PodCount);

public sealed record ResourceSlice(
  double Used,
  double Requests,
  double Limits,
  double Allocatable,
  double Capacity,
  string Kind) {
  public static ResourceSlice Empty(string kind) => new(0, 0, 0, 0, 0, kind);

  public double Scale =>
    Math.Max(1, new[] { Used, Requests, Limits, Allocatable, Capacity }.Max());

  public double UsedPercent => Percent(Used);

  public double RequestsPercent => Percent(Requests);

  public double LimitsPercent => Percent(Limits);

  public double AllocatablePercent => Percent(Allocatable);

  public double CapacityPercent => Percent(Capacity);

  public bool ShowRequestsAndLimits => Kind is "cpu" or "memory";

  public bool LimitsExceedCapacity => ShowRequestsAndLimits && Limits > Capacity && Capacity > 0;

  public string UsageLine => $"Usage: {Format(Used)}";

  public string RequestsLine => $"Requests: {Format(Requests)}";

  public string LimitsLine => $"Limits: {Format(Limits)}";

  public string AllocatableLine => $"Allocatable Capacity: {Format(Allocatable)}";

  public string CapacityLine => $"Capacity: {Format(Capacity)}";

  public string LimitsWarning =>
    LimitsExceedCapacity ? "Specified limits are higher than node capacity!" : "";

  public string Caption => $"{Format(Used)} / {Format(Allocatable)}";

  private double Percent(double value) =>
    Math.Clamp(value / Scale * 100, 0, 100);

  private string Format(double value) => Kind switch {
    "cpu" => KubeQuantity.FormatCoresFixed(value),
    "memory" => KubeQuantity.FormatBytesCompact((long)Math.Round(value)),
    _ => value.ToString("0")
  };
}

public sealed record ClusterUsage(
  string GitVersion,
  string Platform,
  int NodeCount,
  ResourceSlice Cpu,
  ResourceSlice Memory,
  ResourceSlice Pods,
  IReadOnlyList<NodeUsage> Nodes,
  IReadOnlyList<WorkloadContainerLimit> ContainerLimits,
  bool MetricsAvailable,
  string? MetricsMessage) {
  public double CpuPercent => Percent(Cpu.Used, Cpu.Allocatable);

  public double MemoryPercent => Percent(Memory.Used, Memory.Allocatable);

  public double PodPercent => Percent(Pods.Used, Pods.Allocatable);

  public string CpuCaption => Cpu.Caption;

  public string MemoryCaption => Memory.Caption;

  public string PodCaption => Pods.Caption;

  private static double Percent(double used, double capacity) =>
    capacity <= 0 ? 0 : Math.Clamp(used / capacity * 100, 0, 100);
}

public sealed record ResourceMetrics(
  string Name,
  string? Namespace,
  string Cpu,
  string Memory);

public sealed record ExecBytesResult(byte[] Stdout, string Stderr);

public sealed record HelmReleaseInfo(
  string Name,
  string Namespace,
  string Status,
  string Chart,
  string AppVersion,
  DateTimeOffset? Updated);

public sealed class PortForwardHandle : IDisposable {
  private readonly IDisposable _inner;
  private readonly Action? _onDispose;

  public PortForwardHandle(string podName, string @namespace, int containerPort, int localPort, IDisposable inner, Action? onDispose = null) {
    PodName = podName;
    Namespace = @namespace;
    ContainerPort = containerPort;
    LocalPort = localPort;
    _inner = inner;
    _onDispose = onDispose;
  }

  public string PodName { get; }

  public string Namespace { get; }

  public int ContainerPort { get; }

  public int LocalPort { get; }

  public void Dispose() {
    _onDispose?.Invoke();
    _inner.Dispose();
  }
}
