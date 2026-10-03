using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public static class DaprCatalog {
  public static async Task<Result<IReadOnlyList<ResourceRow>>> ListSidecarsAsync(
    IClusterSession session,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var pods = ResourceCatalog.Find("pods")!;
    var listed = await session.ListAsync(pods.ToRef(), @namespace, cancellationToken).ConfigureAwait(false);

    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = (listed.Value ?? [])
      .Where(IsSidecar)
      .Select(pod => ResourceRow.From(pod, pods))
      .Where(row => ClusterWorkspace.Matches(row, filter))
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  public static async Task<Result<IReadOnlyList<ResourceRow>>> ListControlPlaneAsync(
    IClusterSession session,
    string? filter,
    CancellationToken cancellationToken) {
    var pods = ResourceCatalog.Find("pods")!;
    var listed = await session.ListAsync(pods.ToRef(), "dapr", cancellationToken).ConfigureAwait(false);

    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = (listed.Value ?? [])
      .Select(pod => ResourceRow.From(pod, pods))
      .Where(row => ClusterWorkspace.Matches(row, filter))
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private static bool IsSidecar(JsonObject pod) {
    var annotations = pod["metadata"]?["annotations"] as JsonObject;
    var enabled = annotations?["dapr.io/enabled"]?.ToString();
    var appId = annotations?["dapr.io/app-id"]?.ToString();

    if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(appId))
      return true;

    var containers = pod["spec"]?["containers"] as JsonArray;

    return containers?.OfType<JsonObject>().Any(container => container["name"]?.ToString() == "daprd") == true;
  }
}
