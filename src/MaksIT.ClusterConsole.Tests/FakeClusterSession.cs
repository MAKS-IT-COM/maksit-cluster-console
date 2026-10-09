using System.Text.Json.Nodes;
using k8s;
using MaksIT.Results;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.Tests;

internal sealed class FakeClusterSession : IClusterSession {
  private readonly Dictionary<string, IReadOnlyList<JsonObject>> _lists = new(StringComparer.Ordinal);
  private readonly Dictionary<string, Result<IReadOnlyList<JsonObject>>> _listFailures = new(StringComparer.Ordinal);

  public string ContextName => "test";

  public IKubernetes Kubernetes =>
    throw new NotSupportedException();

  public bool Disposed { get; private set; }

  public IReadOnlyList<JsonObject> Crds { get; set; } = [];

  public string? CrdError { get; set; }

  public IReadOnlyList<JsonObject> HelmDocuments { get; set; } = [];

  public string? HelmError { get; set; }

  public Dictionary<string, ResourceMetrics> PodMetrics { get; } = new(StringComparer.Ordinal);

  public Dictionary<string, ResourceMetrics> NodeMetrics { get; } = new(StringComparer.Ordinal);

  public bool PodMetricsFail { get; set; }

  public bool NodeMetricsFail { get; set; }

  public double CpuAllocatable { get; set; } = 8;

  public bool CpuFails { get; set; }

  public int CpuCalls { get; private set; }

  public JsonObject? Gotten { get; set; }

  public string? GetError { get; set; }

  public (string Kind, string Name, string? Namespace)? LastGet { get; private set; }

  public string LogsText { get; set; } = "";

  public string? LogsError { get; set; }

  public int LastTail { get; private set; }

  public ExecBytesResult ExecBytes { get; set; } = new([], "");

  public Result<ExecBytesResult>? ExecBytesResult { get; set; }

  public string ExecText { get; set; } = "";

  public string? ExecError { get; set; }

  public List<IReadOnlyList<string>> Commands { get; } = [];

  public byte[]? LastStdin { get; private set; }

  public ResourceListOptions? LastOptions { get; private set; }

  public string? LastPlural { get; private set; }

  public string? LastListNamespace { get; private set; }

  public int Deletes { get; private set; }

  public int Scales { get; private set; }

  public int Restarts { get; private set; }

  public int Applies { get; private set; }

  public int LastReplicas { get; private set; }

  public JsonObject? Applied { get; private set; }

  public ResourceRef? AppliedRef { get; private set; }

  public bool ApplyFails { get; set; }

  public bool DeleteFails { get; set; }

  public void Set(string id, params JsonObject[] items) {
    var descriptor = ResourceCatalog.Find(id) ?? throw new InvalidOperationException(id);
    _lists[Key(descriptor.ToRef())] = items;
  }

  public void FailList(string id, string message) {
    var descriptor = ResourceCatalog.Find(id) ?? throw new InvalidOperationException(id);
    _listFailures[Key(descriptor.ToRef())] = Result<IReadOnlyList<JsonObject>>.NotFound(null, message);
  }

  public void Dispose() =>
    Disposed = true;

  public Task<Result<IReadOnlyList<JsonObject>>> ListAsync(
    ResourceRef resource,
    string? @namespace,
    CancellationToken cancellationToken = default,
    ResourceListOptions? options = null) {
    LastPlural = resource.Plural;
    LastListNamespace = @namespace;
    LastOptions = options;

    if (options is not null && string.IsNullOrEmpty(options.ResourceVersion))
      options.ResourceVersion = "rv-1";

    var key = Key(resource);

    if (_listFailures.TryGetValue(key, out var failed))
      return Task.FromResult(failed);

    _lists.TryGetValue(key, out var items);
    items ??= [];

    if (!string.IsNullOrWhiteSpace(@namespace) && @namespace != Configuration.AllNamespaces)
      items = items.Where(item => JsonPath.Namespace(item) == @namespace).ToList();

    return Task.FromResult(Result<IReadOnlyList<JsonObject>>.Ok(items));
  }

  public IAsyncEnumerable<ClusterWatchEvent> WatchAsync(
    ResourceRef resource,
    string? @namespace,
    string? resourceVersion,
    string? labelSelector,
    CancellationToken cancellationToken = default) =>
    throw new NotSupportedException();

  public Task<Result<JsonObject>> GetAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default) {
    LastGet = (resource.Kind, name, @namespace);

    if (GetError is not null)
      return Task.FromResult(Result<JsonObject>.NotFound(null, GetError));

    return Task.FromResult(Result<JsonObject>.Ok(Gotten ?? new JsonObject {
      ["kind"] = resource.Kind,
      ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = @namespace }
    }));
  }

  public Task<Result<JsonObject>> ApplyAsync(
    JsonObject document,
    ResourceRef? resource = null,
    CancellationToken cancellationToken = default) {
    Applies++;
    Applied = document;
    AppliedRef = resource;

    if (ApplyFails)
      return Task.FromResult(Result<JsonObject>.Conflict(null, "conflict"));

    return Task.FromResult(Result<JsonObject>.Ok(document));
  }

  public Task<Result> DeleteAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    bool force = false,
    CancellationToken cancellationToken = default) {
    Deletes++;

    return Task.FromResult(DeleteFails ? Result.NotFound("missing") : Result.Ok());
  }

  public Task<Result> ForceDeleteNamespaceAsync(string name, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result> ScaleAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    int replicas,
    CancellationToken cancellationToken = default) {
    Scales++;
    LastReplicas = replicas;

    return Task.FromResult(Result.Ok());
  }

  public Task<Result> RestartAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default) {
    Restarts++;

    return Task.FromResult(Result.Ok());
  }

  public Task<Result<string>> GetLogsAsync(
    string podName,
    string @namespace,
    string? container,
    bool previous,
    int tailLines,
    CancellationToken cancellationToken = default) {
    LastTail = tailLines;

    if (LogsError is not null)
      return Task.FromResult(Result<string>.NotFound(null, LogsError));

    return Task.FromResult(Result<string>.Ok(LogsText));
  }

  public IAsyncEnumerable<string> FollowLogsAsync(
    string podName,
    string @namespace,
    string? container,
    CancellationToken cancellationToken = default) =>
    throw new NotSupportedException();

  public Task<Result<PortForwardHandle>> PortForwardAsync(
    string podName,
    string @namespace,
    int containerPort,
    int localPort,
    int requestedPort = 0,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget = null,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<PortForwardHandle>.NotFound(null, "unused"));

  public Task<Result<IReadOnlyList<JsonObject>>> ListCustomResourceDefinitionsAsync(
    CancellationToken cancellationToken = default) {
    if (CrdError is not null)
      return Task.FromResult(Result<IReadOnlyList<JsonObject>>.NotFound(null, CrdError));

    return Task.FromResult(Result<IReadOnlyList<JsonObject>>.Ok(Crds));
  }

  public Task<Result<bool>> HasApiGroupAsync(string group, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<bool>.Ok(false));

  public Task<Result<ClusterSummary>> GetSummaryAsync(CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<ClusterSummary>.NotFound(null, "unused"));

  public Task<Result<ClusterUsage>> GetClusterUsageAsync(CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<ClusterUsage>.NotFound(null, "unused"));

  public Task<Result<double>> GetClusterCpuAllocatableAsync(CancellationToken cancellationToken = default) {
    CpuCalls++;

    if (CpuFails)
      return Task.FromResult(Result<double>.ServiceUnavailable(0, "no metrics"));

    return Task.FromResult(Result<double>.Ok(CpuAllocatable));
  }

  public Task<Result> PatchContainerResourcesAsync(
    WorkloadContainerLimit row,
    string cpuLimit,
    string memoryLimit,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetPodMetricsAsync(
    string? @namespace,
    CancellationToken cancellationToken = default) {
    if (PodMetricsFail)
      return Task.FromResult(Result<IReadOnlyDictionary<string, ResourceMetrics>>.NotFound(null, "metrics unavailable"));

    return Task.FromResult(Result<IReadOnlyDictionary<string, ResourceMetrics>>.Ok(PodMetrics));
  }

  public Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetNodeMetricsAsync(
    CancellationToken cancellationToken = default) {
    if (NodeMetricsFail)
      return Task.FromResult(Result<IReadOnlyDictionary<string, ResourceMetrics>>.NotFound(null, "metrics unavailable"));

    return Task.FromResult(Result<IReadOnlyDictionary<string, ResourceMetrics>>.Ok(NodeMetrics));
  }

  public Task<Result<IReadOnlyList<JsonObject>>> ListHelmReleaseDocumentsAsync(
    string? @namespace,
    string? releaseName,
    CancellationToken cancellationToken = default) {
    if (HelmError is not null)
      return Task.FromResult(Result<IReadOnlyList<JsonObject>>.NotFound(null, HelmError));

    IReadOnlyList<JsonObject> documents = HelmDocuments;

    if (!string.IsNullOrWhiteSpace(@namespace) && @namespace != Configuration.AllNamespaces)
      documents = documents.Where(doc => doc["namespace"]?.GetValue<string>() == @namespace).ToList();

    if (!string.IsNullOrWhiteSpace(releaseName))
      documents = documents.Where(doc => doc["name"]?.GetValue<string>() == releaseName).ToList();

    return Task.FromResult(Result<IReadOnlyList<JsonObject>>.Ok(documents));
  }

  public Task<Result<string>> ExecAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    CancellationToken cancellationToken = default) {
    Commands.Add(command);

    if (ExecError is not null)
      return Task.FromResult(Result<string>.NotFound(null, ExecError));

    return Task.FromResult(Result<string>.Ok(ExecText));
  }

  public Task<Result<ExecBytesResult>> ExecBytesAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    byte[]? stdin = null,
    CancellationToken cancellationToken = default) {
    Commands.Add(command);
    LastStdin = stdin;

    return Task.FromResult(ExecBytesResult ?? Result<ExecBytesResult>.Ok(ExecBytes));
  }

  public Task<Result<PodTerminalConnection>> OpenTerminalAsync(
    string podName,
    string @namespace,
    string? container,
    string? shell,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<PodTerminalConnection>.NotFound(null, "unused"));

  public Task<Result> CordonAsync(string nodeName, bool unschedulable, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result> DrainAsync(string nodeName, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result> TriggerCronJobAsync(string name, string @namespace, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result> ResizePersistentVolumeClaimAsync(
    string name,
    string @namespace,
    string storage,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result> PauseRolloutAsync(
    string name,
    string @namespace,
    bool paused,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result<IReadOnlyList<string>>> RolloutHistoryAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<IReadOnlyList<string>>.NotFound(null, "unused"));

  public Task<Result> UndoRolloutAsync(string name, string @namespace, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result> SetCertificateApprovalAsync(string name, bool approved, CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result<string>> CreateServiceAccountTokenAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<string>.NotFound(null, "unused"));

  public Task<Result<string>> AttachAsync(
    string podName,
    string @namespace,
    string? container,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<string>.NotFound(null, "unused"));

  public Task<Result> AddEphemeralContainerAsync(
    string podName,
    string @namespace,
    string image,
    string? targetContainer,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result.NotFound("unused"));

  public Task<Result<StorageReclaimPreview>> PreviewStorageClassReclaimAsync(
    string name,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<StorageReclaimPreview>.NotFound(null, "unused"));

  public Task<Result<StorageReclaimPreview>> PreviewPersistentVolumeReclaimAsync(
    IReadOnlyList<string> names,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<StorageReclaimPreview>.NotFound(null, "unused"));

  public Task<Result<StorageReclaimOutcome>> ApplyStorageClassReclaimAsync(
    string name,
    string policy,
    bool updateVolumes,
    bool updateClass,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<StorageReclaimOutcome>.NotFound(null, "unused"));

  public Task<Result<StorageReclaimOutcome>> ApplyPersistentVolumeReclaimAsync(
    IReadOnlyList<string> names,
    string policy,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(Result<StorageReclaimOutcome>.NotFound(null, "unused"));

  private static string Key(ResourceRef resource) =>
    resource.Group + "/" + resource.Plural;
}
