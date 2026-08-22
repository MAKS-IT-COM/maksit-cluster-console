using System.Text.Json.Nodes;
using k8s;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

public interface IClusterSession : IDisposable {
  string ContextName { get; }

  IKubernetes Kubernetes { get; }

  Task<Result<IReadOnlyList<JsonObject>>> ListAsync(
    ResourceRef resource,
    string? @namespace,
    CancellationToken cancellationToken = default);

  Task<Result<JsonObject>> GetAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default);

  Task<Result<JsonObject>> ApplyAsync(
    JsonObject document,
    ResourceRef? resource = null,
    CancellationToken cancellationToken = default);

  Task<Result> DeleteAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    bool force = false,
    CancellationToken cancellationToken = default);

  Task<Result> ForceDeleteNamespaceAsync(string name, CancellationToken cancellationToken = default);

  Task<Result> ScaleAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    int replicas,
    CancellationToken cancellationToken = default);

  Task<Result> RestartAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default);

  Task<Result<string>> GetLogsAsync(
    string podName,
    string @namespace,
    string? container,
    bool previous,
    int tailLines,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<string> FollowLogsAsync(
    string podName,
    string @namespace,
    string? container,
    CancellationToken cancellationToken = default);

  Task<Result<PortForwardHandle>> PortForwardAsync(
    string podName,
    string @namespace,
    int containerPort,
    int localPort,
    int requestedPort = 0,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget = null,
    CancellationToken cancellationToken = default);

  Task<Result<IReadOnlyList<JsonObject>>> ListCustomResourceDefinitionsAsync(
    CancellationToken cancellationToken = default);

  Task<Result<bool>> HasApiGroupAsync(string group, CancellationToken cancellationToken = default);

  Task<Result<ClusterSummary>> GetSummaryAsync(CancellationToken cancellationToken = default);

  Task<Result<ClusterUsage>> GetClusterUsageAsync(CancellationToken cancellationToken = default);

  Task<Result<double>> GetClusterCpuAllocatableAsync(CancellationToken cancellationToken = default);

  Task<Result> PatchContainerResourcesAsync(
    WorkloadContainerLimit row,
    string cpuLimit,
    string memoryLimit,
    CancellationToken cancellationToken = default);

  Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetPodMetricsAsync(
    string? @namespace,
    CancellationToken cancellationToken = default);

  Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetNodeMetricsAsync(
    CancellationToken cancellationToken = default);

  Task<Result<IReadOnlyList<HelmReleaseInfo>>> ListHelmReleasesAsync(
    string? @namespace,
    CancellationToken cancellationToken = default);

  Task<Result<string>> ExecAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    CancellationToken cancellationToken = default);

  Task<Result<ExecBytesResult>> ExecBytesAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    byte[]? stdin = null,
    CancellationToken cancellationToken = default);

  Task<Result> CordonAsync(string nodeName, bool unschedulable, CancellationToken cancellationToken = default);

  Task<Result> DrainAsync(string nodeName, CancellationToken cancellationToken = default);

  Task<Result> TriggerCronJobAsync(string name, string @namespace, CancellationToken cancellationToken = default);
}
