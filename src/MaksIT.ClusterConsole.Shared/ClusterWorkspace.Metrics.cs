using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public sealed partial class ClusterWorkspace {
  private static readonly TimeSpan ClusterCpuAllocatableCacheLifetime = TimeSpan.FromSeconds(30);

  private double? _cachedClusterCpuAllocatable;
  private DateTimeOffset _clusterCpuAllocatableCachedAt;

  private void ResetMetricsCache() {
    _cachedClusterCpuAllocatable = null;
    _clusterCpuAllocatableCachedAt = default;
  }

  private async Task<double> GetClusterCpuAllocatableCachedAsync(CancellationToken cancellationToken) {
    if (_session is null)
      return 0;

    if (_cachedClusterCpuAllocatable is not null
        && DateTimeOffset.UtcNow - _clusterCpuAllocatableCachedAt < ClusterCpuAllocatableCacheLifetime)
      return _cachedClusterCpuAllocatable.Value;

    var result = await _session.GetClusterCpuAllocatableAsync(cancellationToken).ConfigureAwait(false);
    if (!result.IsSuccess)
      return _cachedClusterCpuAllocatable ?? 0;

    _cachedClusterCpuAllocatable = result.Value;
    _clusterCpuAllocatableCachedAt = DateTimeOffset.UtcNow;
    return result.Value;
  }

  private async Task<Result<IReadOnlyList<ResourceRow>>> ListWorkloadsWithPodMetricsAsync(
    ResourceDescriptor descriptor,
    IReadOnlyList<JsonObject> items,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var podsDescriptor = ResourceCatalog.Find("pods")!;
    var podsTask = _session!.ListAsync(podsDescriptor.ToRef(), @namespace, cancellationToken);
    var metricsTask = _session.GetPodMetricsAsync(@namespace, cancellationToken);
    await Task.WhenAll(podsTask, metricsTask).ConfigureAwait(false);

    var podsResult = await podsTask.ConfigureAwait(false);
    var metricsResult = await metricsTask.ConfigureAwait(false);
    var podMetrics = metricsResult.IsSuccess && metricsResult.Value is not null
      ? metricsResult.Value
      : (IReadOnlyDictionary<string, ResourceMetrics>)new Dictionary<string, ResourceMetrics>();
    var metricsAvailable = podMetrics.Count > 0;
    var allPods = podsResult.IsSuccess ? podsResult.Value ?? [] : [];

    var rows = items
      .Select(item => {
        var usage = PodMetricsAggregate.SumForOwner(item, allPods, podMetrics);
        return ResourceRow.From(item, descriptor, PodMetricsAggregate.ToDisplayMetrics(usage, metricsAvailable));
      })
      .Where(row => Matches(row, filter))
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }
}
