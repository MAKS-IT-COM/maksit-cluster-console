using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

internal static class ClusterObjectList {
  public static async Task<Result<IReadOnlyList<JsonObject>>> LoadAsync(
    IClusterSession session,
    string id,
    CancellationToken cancellationToken) {
    var descriptor = ResourceCatalog.Find(id);

    if (descriptor is null)
      return Result<IReadOnlyList<JsonObject>>.NotFound(null, $"unknown resource {id}");

    return await session.ListAsync(descriptor.ToRef(), Configuration.AllNamespaces, cancellationToken)
      .ConfigureAwait(false);
  }
}
