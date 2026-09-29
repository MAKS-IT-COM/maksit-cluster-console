using System.Net;
using System.Text.Json.Nodes;
using k8s;
using k8s.Models;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

public sealed partial class ClusterSession {
  private static readonly ResourceRef StorageClassRef = new("storage.k8s.io", "v1", "storageclasses", "StorageClass", false);

  private static readonly ResourceRef PersistentVolumeRef = new("", "v1", "persistentvolumes", "PersistentVolume", false);

  public async Task<Result<StorageReclaimPreview>> PreviewStorageClassReclaimAsync(
    string name,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(name))
      return Result<StorageReclaimPreview>.BadRequest(null, "Storage class name is required.");

    var got = await GetAsync(StorageClassRef, name.Trim(), null, cancellationToken).ConfigureAwait(false);
    if (!got.IsSuccess)
      return CopyFailure<StorageReclaimPreview, JsonObject>(got);
    if (got.Value is null)
      return Result<StorageReclaimPreview>.NotFound(null, "Storage class not found.");

    var volumes = await ListPersistentVolumesAsync(cancellationToken).ConfigureAwait(false);
    if (!volumes.IsSuccess)
      return CopyFailure<StorageReclaimPreview, IReadOnlyList<JsonObject>>(volumes);
    if (volumes.Value is null)
      return Result<StorageReclaimPreview>.NotFound(null, "Persistent volume list was missing.");

    return Result<StorageReclaimPreview>.Ok(ReclaimPolicy.PreviewClass(got.Value, volumes.Value));
  }

  public async Task<Result<StorageReclaimPreview>> PreviewPersistentVolumeReclaimAsync(
    IReadOnlyList<string> names,
    CancellationToken cancellationToken = default) {
    if (names.Count == 0 || names.All(string.IsNullOrWhiteSpace))
      return Result<StorageReclaimPreview>.BadRequest(null, "Select a persistent volume.");

    var volumes = await ListPersistentVolumesAsync(cancellationToken).ConfigureAwait(false);
    if (!volumes.IsSuccess)
      return CopyFailure<StorageReclaimPreview, IReadOnlyList<JsonObject>>(volumes);
    if (volumes.Value is null)
      return Result<StorageReclaimPreview>.NotFound(null, "Persistent volume list was missing.");

    return Result<StorageReclaimPreview>.Ok(ReclaimPolicy.PreviewVolumes(volumes.Value, names));
  }

  public async Task<Result<StorageReclaimOutcome>> ApplyStorageClassReclaimAsync(
    string name,
    string policy,
    bool updateVolumes,
    bool updateClass,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(name))
      return Result<StorageReclaimOutcome>.BadRequest(null, "Storage class name is required.");
    if (ReclaimPolicy.Normalize(policy) is not { } canonical)
      return Result<StorageReclaimOutcome>.BadRequest(null, "Reclaim policy must be Delete or Retain.");
    if (!updateVolumes && !updateClass)
      return Result<StorageReclaimOutcome>.BadRequest(null, "Choose volumes, the storage class, or both.");

    var className = name.Trim();
    var errors = new List<string>();
    var patched = 0;
    var recreated = false;
    var unchanged = false;
    JsonObject? recovery = null;

    if (updateVolumes) {
      var listed = await ListPersistentVolumesAsync(cancellationToken).ConfigureAwait(false);
      if (!listed.IsSuccess || listed.Value is null) {
        errors.Add("Could not list volumes, so the storage class was left unchanged. " + Describe(listed));
        updateClass = false;
      }
      else {
        patched = await PatchVolumesAsync(
          ReclaimPolicy.VolumesForClass(listed.Value, className, canonical),
          canonical,
          errors,
          cancellationToken).ConfigureAwait(false);
      }
    }

    if (updateClass) {
      var got = await GetAsync(StorageClassRef, className, null, cancellationToken).ConfigureAwait(false);
      if (!got.IsSuccess || got.Value is null) {
        errors.Add(Describe(got));
      }
      else if (ReclaimPolicy.Same(ReclaimPolicy.ClassPolicy(got.Value), canonical)) {
        unchanged = true;
      }
      else {
        var replacement = ReclaimPolicy.Replacement(got.Value, canonical);
        var deleted = await DeleteAsync(StorageClassRef, className, null, cancellationToken: cancellationToken)
          .ConfigureAwait(false);
        if (!deleted.IsSuccess) {
          errors.Add(Describe(deleted));
        }
        else {
          var gone = await WaitUntilDeletedAsync(StorageClassRef, className, cancellationToken).ConfigureAwait(false);
          if (!gone.IsSuccess) {
            recovery = replacement;
            errors.Add(gone.StatusCode == HttpStatusCode.Conflict
              ? $"Storage class {className} is still deleting. When the name is free, create it again with the YAML below."
              : $"Storage class {className} was deleted. It was not created again because the cluster did not confirm the name was free. {Describe(gone)}");
          }
          else {
            var created = await CreateClusterAsync(replacement, StorageClassRef, cancellationToken).ConfigureAwait(false);
            if (created.IsSuccess)
              recreated = true;
            else {
              recovery = replacement;
              errors.Add($"Storage class {className} was deleted and could not be recreated. {Describe(created)}");
            }
          }
        }
      }
    }

    var outcome = new StorageReclaimOutcome(patched, canonical, recreated, unchanged, recovery, errors);
    return errors.Count == 0
      ? Result<StorageReclaimOutcome>.Ok(outcome)
      : new Result<StorageReclaimOutcome>(outcome, false, errors, HttpStatusCode.Conflict);
  }

  public async Task<Result<StorageReclaimOutcome>> ApplyPersistentVolumeReclaimAsync(
    IReadOnlyList<string> names,
    string policy,
    CancellationToken cancellationToken = default) {
    if (names.Count == 0 || names.All(string.IsNullOrWhiteSpace))
      return Result<StorageReclaimOutcome>.BadRequest(null, "Select a persistent volume.");
    if (ReclaimPolicy.Normalize(policy) is not { } canonical)
      return Result<StorageReclaimOutcome>.BadRequest(null, "Reclaim policy must be Delete or Retain.");

    var listed = await ListPersistentVolumesAsync(cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return CopyFailure<StorageReclaimOutcome, IReadOnlyList<JsonObject>>(listed);
    if (listed.Value is null)
      return Result<StorageReclaimOutcome>.NotFound(null, "Persistent volume list was missing.");

    var errors = new List<string>();
    var patched = await PatchVolumesAsync(ReclaimPolicy.VolumesToUpdate(listed.Value, names, canonical), canonical, errors, cancellationToken)
      .ConfigureAwait(false);
    var outcome = new StorageReclaimOutcome(patched, canonical, false, false, null, errors);
    return errors.Count == 0
      ? Result<StorageReclaimOutcome>.Ok(outcome)
      : new Result<StorageReclaimOutcome>(outcome, false, errors, HttpStatusCode.Conflict);
  }

  private async Task<Result<IReadOnlyList<JsonObject>>> ListPersistentVolumesAsync(CancellationToken cancellationToken) =>
    await ListAsync(PersistentVolumeRef, null, cancellationToken).ConfigureAwait(false);

  private async Task<int> PatchVolumesAsync(
    IReadOnlyList<JsonObject> volumes,
    string policy,
    List<string> errors,
    CancellationToken cancellationToken) {
    var patched = 0;
    foreach (var volume in volumes) {
      var name = ReclaimPolicy.Name(volume);
      var result = await PatchVolumeReclaimAsync(name, policy, cancellationToken).ConfigureAwait(false);
      if (result.IsSuccess)
        patched++;
      else
        errors.Add($"{name}: {Describe(result)}");
    }

    return patched;
  }

  private async Task<Result> PatchVolumeReclaimAsync(string name, string policy, CancellationToken cancellationToken) {
    try {
      var patch = new V1Patch(
        "{\"spec\":{\"persistentVolumeReclaimPolicy\":\"" + policy + "\"}}",
        V1Patch.PatchType.MergePatch);
      await _client.CustomObjects.PatchClusterCustomObjectAsync(
        patch,
        "",
        "v1",
        "persistentvolumes",
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  private async Task<Result> CreateClusterAsync(JsonObject body, ResourceRef resource, CancellationToken cancellationToken) {
    try {
      await _client.CustomObjects.CreateClusterCustomObjectAsync(
        body,
        resource.Group,
        resource.Version,
        resource.Plural,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  private async Task<Result> WaitUntilDeletedAsync(ResourceRef resource, string name, CancellationToken cancellationToken) {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
    while (true) {
      cancellationToken.ThrowIfCancellationRequested();
      var got = await GetAsync(resource, name, null, cancellationToken).ConfigureAwait(false);
      if (!got.IsSuccess && got.StatusCode == HttpStatusCode.NotFound)
        return Result.Ok();
      if (!got.IsSuccess)
        return Fail(got);
      if (DateTime.UtcNow >= deadline)
        return Result.Conflict($"{resource.Kind} {name} is still deleting.");
      await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
    }
  }

  private static Result<TOut> CopyFailure<TOut, TIn>(Result<TIn> source) =>
    new(default, false, source.Messages, source.StatusCode);

  private static string Describe(Result result) =>
    result.Messages is { Count: > 0 } ? string.Join(" ", result.Messages) : "Request failed.";

  private static string Describe<T>(Result<T> result) =>
    result.Messages is { Count: > 0 } ? string.Join(" ", result.Messages) : "Request failed.";
}
