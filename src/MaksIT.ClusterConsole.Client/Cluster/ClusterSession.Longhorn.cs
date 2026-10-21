using System.Net;
using k8s;
using k8s.Models;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Internal;


namespace MaksIT.ClusterConsole.Client.Cluster;

public sealed partial class ClusterSession {
  public async Task<Result> AttachLonghornVolumeAsync(
    string name,
    string @namespace,
    string nodeId,
    bool disableFrontend,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(nodeId))
      return Result.BadRequest("Volume name and node are required.");

    var ns = string.IsNullOrWhiteSpace(@namespace) ? "longhorn-system" : @namespace;
    var ticket = await UpsertAttachmentTicketAsync(name, ns, nodeId, disableFrontend, cancellationToken)
      .ConfigureAwait(false);

    if (!ticket.IsSuccess)
      return ticket;

    return await MergePatchAsync(
      LonghornAttachment.Volume,
      name,
      ns,
      LonghornAttachment.NodePatch(nodeId),
      cancellationToken).ConfigureAwait(false);
  }

  public async Task<Result> DetachLonghornVolumeAsync(
    string name,
    string @namespace,
    bool force,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(name))
      return Result.BadRequest("Volume name is required.");

    var ns = string.IsNullOrWhiteSpace(@namespace) ? "longhorn-system" : @namespace;
    var existing = await GetAsync(LonghornAttachment.Attachment, name, ns, cancellationToken).ConfigureAwait(false);

    if (existing.IsSuccess) {
      var ids = force
        ? LonghornAttachment.TicketIds(existing.Value)
        : [LonghornAttachment.TicketId];
      var cleared = await MergePatchAsync(
        LonghornAttachment.Attachment,
        name,
        ns,
        LonghornAttachment.ClearTickets(ids),
        cancellationToken).ConfigureAwait(false);

      if (!cleared.IsSuccess)
        return cleared;
    }
    else if (existing.StatusCode != HttpStatusCode.NotFound) {
      return Fail(existing);
    }

    return await MergePatchAsync(
      LonghornAttachment.Volume,
      name,
      ns,
      LonghornAttachment.NodePatch(""),
      cancellationToken).ConfigureAwait(false);
  }

  public async Task<Result> PatchLonghornVolumeAsync(
    string name,
    string @namespace,
    string mergePatchJson,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mergePatchJson))
      return Result.BadRequest("Volume name and patch are required.");

    var ns = string.IsNullOrWhiteSpace(@namespace) ? "longhorn-system" : @namespace;

    return await MergePatchAsync(LonghornAttachment.Volume, name, ns, mergePatchJson, cancellationToken).ConfigureAwait(false);
  }

  private async Task<Result> UpsertAttachmentTicketAsync(
    string name,
    string ns,
    string nodeId,
    bool disableFrontend,
    CancellationToken cancellationToken) {
    var existing = await GetAsync(LonghornAttachment.Attachment, name, ns, cancellationToken).ConfigureAwait(false);

    if (existing.IsSuccess) {
      return await MergePatchAsync(
        LonghornAttachment.Attachment,
        name,
        ns,
        LonghornAttachment.TicketMerge(nodeId, disableFrontend),
        cancellationToken).ConfigureAwait(false);
    }

    if (existing.StatusCode != HttpStatusCode.NotFound)
      return Fail(existing);

    try {
      await _client.CustomObjects.CreateNamespacedCustomObjectAsync(
        LonghornAttachment.CreateBody(name, ns, nodeId, disableFrontend),
        LonghornAttachment.Group,
        LonghornAttachment.Version,
        ns,
        LonghornAttachment.Plural,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      return Result.Ok();
    }
    catch (Exception ex) {
      var created = KubernetesResult.Map(ex);

      if (created.StatusCode == HttpStatusCode.NotFound)
        return Result.Ok();

      if (created.StatusCode == HttpStatusCode.Conflict) {
        return await MergePatchAsync(
          LonghornAttachment.Attachment,
          name,
          ns,
          LonghornAttachment.TicketMerge(nodeId, disableFrontend),
          cancellationToken).ConfigureAwait(false);
      }

      return created;
    }
  }

  private async Task<Result> MergePatchAsync(
    ResourceRef resource,
    string name,
    string ns,
    string json,
    CancellationToken cancellationToken) {
    try {
      var patch = new V1Patch(json, V1Patch.PatchType.MergePatch);
      await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
        patch,
        resource.Group,
        resource.Version,
        ns,
        resource.Plural,
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }
}
