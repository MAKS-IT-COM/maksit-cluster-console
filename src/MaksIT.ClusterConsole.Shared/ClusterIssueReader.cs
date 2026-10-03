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
    await Task.WhenAll(nodesTask, eventsTask, podsTask, servicesTask, claimsTask).ConfigureAwait(false);

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

    return Result<ClusterIssueSet>.Ok(ClusterIssues.Collect(
      nodes.Value ?? [],
      events.Value ?? [],
      pods.Value ?? [],
      services: services.IsSuccess ? services.Value : [],
      claims: claims.IsSuccess ? claims.Value : []));
  }
}
