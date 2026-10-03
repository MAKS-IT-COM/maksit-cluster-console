using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public sealed partial class ClusterWorkspace {
  public async Task<Result<NodeImageReport>> NodeImagesAsync(
    JsonObject node,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<NodeImageReport>.ServiceUnavailable(null, "not connected");

    var name = (node["metadata"] as JsonObject)?["name"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(name))
      return Result<NodeImageReport>.BadRequest(null, "node name is missing");

    var pods = ResourceCatalog.Find("pods")!;
    var listed = await _session.ListAsync(
      pods.ToRef(),
      Configuration.AllNamespaces,
      cancellationToken,
      new ResourceListOptions { FieldSelector = NodeImages.PodsOnNodeSelector(name) }).ConfigureAwait(false);

    if (!listed.IsSuccess) {
      var error = string.Join("; ", listed.Messages);

      return Result<NodeImageReport>.Ok(NodeImages.Classify(node, [], false, error));
    }

    return Result<NodeImageReport>.Ok(NodeImages.Classify(node, listed.Value ?? [], true));
  }
}
