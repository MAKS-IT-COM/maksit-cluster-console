using System.Text.Json.Nodes;
using k8s;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.Client.Cluster;

public interface IClusterSession : IDisposable {
  string ContextName { get; }

  IKubernetes Kubernetes { get; }

  Task<Result<IReadOnlyList<JsonObject>>> ListAsync(
    ResourceRef resource,
    string? @namespace,
    CancellationToken cancellationToken = default,
    ResourceListOptions? options = null);

  IAsyncEnumerable<ClusterWatchEvent> WatchAsync(
    ResourceRef resource,
    string? @namespace,
    string? resourceVersion,
    string? labelSelector,
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

  Task<Result<IReadOnlyList<JsonObject>>> ListHelmReleaseDocumentsAsync(
    string? @namespace,
    string? releaseName,
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

  Task<Result<PodTerminalConnection>> OpenTerminalAsync(
    string podName,
    string @namespace,
    string? container,
    string? shell,
    CancellationToken cancellationToken = default);

  Task<Result> CordonAsync(string nodeName, bool unschedulable, CancellationToken cancellationToken = default);

  Task<Result> DrainAsync(string nodeName, CancellationToken cancellationToken = default);

  Task<Result> TriggerCronJobAsync(string name, string @namespace, CancellationToken cancellationToken = default);

  Task<Result> ResizePersistentVolumeClaimAsync(
    string name,
    string @namespace,
    string storage,
    CancellationToken cancellationToken = default);

  Task<Result> PauseRolloutAsync(string name, string @namespace, bool paused, CancellationToken cancellationToken = default);

  Task<Result<IReadOnlyList<string>>> RolloutHistoryAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken = default);

  Task<Result> UndoRolloutAsync(string name, string @namespace, CancellationToken cancellationToken = default);

  Task<Result> SetCertificateApprovalAsync(string name, bool approved, CancellationToken cancellationToken = default);

  Task<Result<string>> CreateServiceAccountTokenAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken = default);

  Task<Result<string>> AttachAsync(
    string podName,
    string @namespace,
    string? container,
    CancellationToken cancellationToken = default);

  Task<Result> AddEphemeralContainerAsync(
    string podName,
    string @namespace,
    string image,
    string? targetContainer,
    CancellationToken cancellationToken = default);

  Task<Result<StorageReclaimPreview>> PreviewStorageClassReclaimAsync(
    string name,
    CancellationToken cancellationToken = default);

  Task<Result<StorageReclaimPreview>> PreviewPersistentVolumeReclaimAsync(
    IReadOnlyList<string> names,
    CancellationToken cancellationToken = default);

  Task<Result<StorageReclaimOutcome>> ApplyStorageClassReclaimAsync(
    string name,
    string policy,
    bool updateVolumes,
    bool updateClass,
    CancellationToken cancellationToken = default);

  Task<Result<StorageReclaimOutcome>> ApplyPersistentVolumeReclaimAsync(
    IReadOnlyList<string> names,
    string policy,
    CancellationToken cancellationToken = default);
}
