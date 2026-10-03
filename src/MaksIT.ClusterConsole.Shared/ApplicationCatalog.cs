using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public static class ApplicationCatalog {
  public static async Task<Result<IReadOnlyList<ResourceRow>>> ListAsync(
    IClusterSession session,
    Func<CancellationToken, Task<double>> clusterCpuAllocatable,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var kinds = new[] {
      ResourceCatalog.Find("deployments")!,
      ResourceCatalog.Find("statefulsets")!,
      ResourceCatalog.Find("daemonsets")!
    };
    var podsDescriptor = ResourceCatalog.Find("pods")!;
    var workloadsTask = Task.WhenAll(kinds.Select(kind =>
      session.ListAsync(kind.ToRef(), @namespace, cancellationToken)));
    var podsTask = session.ListAsync(podsDescriptor.ToRef(), @namespace, cancellationToken);
    var metricsTask = session.GetPodMetricsAsync(@namespace, cancellationToken);
    var allocatableTask = clusterCpuAllocatable(cancellationToken);
    var replicaSetsTask = session.ListAsync(ResourceCatalog.Find("replicasets")!.ToRef(), @namespace, cancellationToken);
    await Task.WhenAll(workloadsTask, podsTask, metricsTask, allocatableTask, replicaSetsTask).ConfigureAwait(false);

    var listed = await workloadsTask.ConfigureAwait(false);

    for (var i = 0; i < listed.Length; i++) {
      if (!listed[i].IsSuccess)
        return new Result<IReadOnlyList<ResourceRow>>(null, false, listed[i].Messages, listed[i].StatusCode);
    }

    var podsResult = await podsTask.ConfigureAwait(false);
    var metricsResult = await metricsTask.ConfigureAwait(false);
    var allocatableResult = await allocatableTask.ConfigureAwait(false);
    var replicaSetsResult = await replicaSetsTask.ConfigureAwait(false);
    var podMetrics = metricsResult.IsSuccess && metricsResult.Value is not null
      ? metricsResult.Value
      : (IReadOnlyDictionary<string, ResourceMetrics>)new Dictionary<string, ResourceMetrics>();
    var metricsAvailable = podMetrics.Count > 0;
    var clusterCpu = allocatableResult;
    var deploymentByReplicaSet = replicaSetsResult.IsSuccess
      ? PodMetricsAggregate.DeploymentByReplicaSet(replicaSetsResult.Value ?? [])
      : (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
    var allPods = podsResult.IsSuccess ? podsResult.Value ?? [] : [];

    var members = listed
      .SelectMany((result, i) => (result.Value ?? []).Select(item => {
        EnsureApiIdentity(item, kinds[i]);

        return item;
      }))
      .Where(ApplicationManifest.HasManifest)
      .ToList();

    var rows = ApplicationManifest.Collapse(members)
      .Select(doc => {
        var usage = metricsAvailable
          ? ApplicationManifest.SumUsage(doc, allPods, podMetrics, deploymentByReplicaSet)
          : (ApplicationUsage?)null;

        return new ResourceRow {
          Uid = JsonPath.Uid(doc),
          Name = JsonPath.Name(doc),
          Namespace = JsonPath.Namespace(doc),
          Document = doc,
          Cells = ApplicationManifest.Cells(doc, usage, clusterCpu, metricsAvailable),
          CellTips = ApplicationManifest.MetricTips(usage, metricsAvailable)
        };
      })
      .Where(row => ClusterWorkspace.Matches(row, filter))
      .OrderBy(row => row.Namespace, StringComparer.OrdinalIgnoreCase)
      .ThenBy(row => row.Cell("Instance"), StringComparer.OrdinalIgnoreCase)
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private static void EnsureApiIdentity(JsonObject item, ResourceDescriptor kind) {
    item["kind"] ??= kind.Kind;

    if (item["apiVersion"] is not null)
      return;

    item["apiVersion"] = string.IsNullOrEmpty(kind.Group)
      ? kind.Version
      : $"{kind.Group}/{kind.Version}";
  }
}
