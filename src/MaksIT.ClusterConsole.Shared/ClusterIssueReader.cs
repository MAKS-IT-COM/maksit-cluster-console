using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public static class ClusterIssueReader {
  public static async Task<Result<ClusterIssueSet>> LoadAsync(
    IClusterSession? session,
    CancellationToken cancellationToken = default) {
    if (session is null)
      return Result<ClusterIssueSet>.ServiceUnavailable(null, "not connected");

    var nodesTask = ClusterObjectList.LoadAsync(session, "nodes", cancellationToken);
    var eventsTask = ClusterObjectList.LoadAsync(session, "events", cancellationToken);
    var podsTask = ClusterObjectList.LoadAsync(session, "pods", cancellationToken);
    var servicesTask = ClusterObjectList.LoadAsync(session, "services", cancellationToken);
    var claimsTask = ClusterObjectList.LoadAsync(session, "persistentvolumeclaims", cancellationToken);
    var longhornTask = session.HasApiGroupAsync("longhorn.io", cancellationToken);
    await Task.WhenAll(nodesTask, eventsTask, podsTask, servicesTask, claimsTask, longhornTask).ConfigureAwait(false);

    var nodes = nodesTask.Result;
    var events = eventsTask.Result;
    var pods = podsTask.Result;
    var services = servicesTask.Result;
    var claims = claimsTask.Result;

    if (!nodes.IsSuccess && !events.IsSuccess)
      return new Result<ClusterIssueSet>(
        null,
        false,
        nodes.Messages.Concat(events.Messages).ToList(),
        nodes.StatusCode);

    IReadOnlyList<JsonObject> volumes = [];
    IReadOnlyList<JsonObject> replicas = [];
    var longhorn = longhornTask.Result;

    if (longhorn.IsSuccess && longhorn.Value) {
      var volumeRef = ResourceCatalog.Find(ResourceCatalog.LonghornVolumesId)!.ToRef();
      var replicaRef = new ResourceRef("longhorn.io", "v1beta2", "replicas", "Replica", true);
      var volumeTask = session.ListAsync(volumeRef, Configuration.AllNamespaces, cancellationToken);
      var replicaTask = session.ListAsync(replicaRef, Configuration.AllNamespaces, cancellationToken);
      await Task.WhenAll(volumeTask, replicaTask).ConfigureAwait(false);

      if (volumeTask.Result.IsSuccess && volumeTask.Result.Value is not null)
        volumes = volumeTask.Result.Value;

      if (replicaTask.Result.IsSuccess && replicaTask.Result.Value is not null)
        replicas = replicaTask.Result.Value;
    }

    return Result<ClusterIssueSet>.Ok(ClusterIssues.Collect(
      nodes.Value ?? [],
      events.Value ?? [],
      pods.Value ?? [],
      services: services.IsSuccess ? services.Value : [],
      claims: claims.IsSuccess ? claims.Value : [],
      volumes: volumes,
      replicas: replicas));
  }
}
