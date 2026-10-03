using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public static class DrainPreviewReader {
  public static async Task<Result<DrainPreview>> LoadAsync(
    IClusterSession? session,
    IReadOnlyList<string> nodeNames,
    CancellationToken cancellationToken = default) {
    if (session is null)
      return Result<DrainPreview>.ServiceUnavailable(null, "not connected");

    var pods = await ClusterObjectList.LoadAsync(session, "pods", cancellationToken).ConfigureAwait(false);

    if (!pods.IsSuccess)
      return new Result<DrainPreview>(null, false, pods.Messages, pods.StatusCode);

    var budgets = await ClusterObjectList.LoadAsync(session, "poddisruptionbudgets", cancellationToken)
      .ConfigureAwait(false);

    if (!budgets.IsSuccess)
      return new Result<DrainPreview>(null, false, budgets.Messages, budgets.StatusCode);

    var plans = nodeNames
      .Distinct(StringComparer.Ordinal)
      .Select(name => DrainPlan.ForNode(name, pods.Value ?? [], budgets.Value ?? []))
      .ToList();

    return Result<DrainPreview>.Ok(new DrainPreview(plans));
  }
}
