using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using k8s;
using k8s.Models;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Internal;


namespace MaksIT.ClusterConsole.Client.Cluster;

public sealed partial class ClusterSession {
  public const string FieldManager = "maksit-cluster-console";

  public async IAsyncEnumerable<ClusterWatchEvent> WatchAsync(
    ResourceRef resource,
    string? @namespace,
    string? resourceVersion,
    string? labelSelector,
    [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    var selector = string.IsNullOrWhiteSpace(labelSelector) ? null : labelSelector.Trim();
    var namespaced = resource.Namespaced && !string.IsNullOrWhiteSpace(@namespace) && @namespace != "all";
    var stream = namespaced
      ? _client.CustomObjects.WatchListNamespacedCustomObjectAsync(
        resource.Group,
        resource.Version,
        @namespace!,
        resource.Plural,
        allowWatchBookmarks: true,
        labelSelector: selector,
        resourceVersion: resourceVersion,
        cancellationToken: cancellationToken)
      : resource.Namespaced
        ? _client.CustomObjects.WatchListCustomObjectForAllNamespacesAsync(
          resource.Group,
          resource.Version,
          resource.Plural,
          allowWatchBookmarks: true,
          labelSelector: selector,
          resourceVersion: resourceVersion,
          cancellationToken: cancellationToken)
        : _client.CustomObjects.WatchListClusterCustomObjectAsync(
          resource.Group,
          resource.Version,
          resource.Plural,
          allowWatchBookmarks: true,
          labelSelector: selector,
          resourceVersion: resourceVersion,
          cancellationToken: cancellationToken);

    await foreach (var (type, item) in stream.ConfigureAwait(false)) {
      cancellationToken.ThrowIfCancellationRequested();
      yield return new ClusterWatchEvent(type.ToString(), KubernetesResult.ToObject(item));
    }
  }

  public async Task<Result> ResizePersistentVolumeClaimAsync(
    string name,
    string @namespace,
    string storage,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(storage) || storage.Any(c => char.IsWhiteSpace(c) || c is '"' or '\\' or '{' or '}'))
      return Result.BadRequest("Storage quantity is invalid.");

    try {
      var patch = new V1Patch(
        "{\"spec\":{\"resources\":{\"requests\":{\"storage\":\"" + storage.Trim() + "\"}}}}",
        V1Patch.PatchType.MergePatch);
      await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
        patch,
        "",
        "v1",
        @namespace,
        "persistentvolumeclaims",
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public async Task<Result> PauseRolloutAsync(
    string name,
    string @namespace,
    bool paused,
    CancellationToken cancellationToken = default) {
    try {
      var patch = new V1Patch(
        "{\"spec\":{\"paused\":" + (paused ? "true" : "false") + "}}",
        V1Patch.PatchType.MergePatch);
      await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
        patch,
        "apps",
        "v1",
        @namespace,
        "deployments",
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public async Task<Result<IReadOnlyList<string>>> RolloutHistoryAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken = default) {
    var sets = await ListOwnedReplicaSetsAsync(name, @namespace, cancellationToken).ConfigureAwait(false);
    if (!sets.IsSuccess)
      return new Result<IReadOnlyList<string>>(null, false, sets.Messages, sets.StatusCode);

    return Result<IReadOnlyList<string>>.Ok(RolloutHistory.Lines(sets.Value ?? []));
  }

  public async Task<Result> UndoRolloutAsync(string name, string @namespace, CancellationToken cancellationToken = default) {
    try {
      var deployment = await GetAsync(
        new ResourceRef("apps", "v1", "deployments", "Deployment", true),
        name,
        @namespace,
        cancellationToken).ConfigureAwait(false);
      if (!deployment.IsSuccess || deployment.Value is null)
        return Fail(deployment);

      var sets = await ListOwnedReplicaSetsAsync(name, @namespace, cancellationToken).ConfigureAwait(false);
      if (!sets.IsSuccess)
        return Fail(sets);

      var template = RolloutHistory.PreviousTemplate(deployment.Value, sets.Value ?? []);
      if (template is null)
        return Result.BadRequest("No previous rollout revision.");

      var patch = new V1Patch(
        new JsonObject { ["spec"] = new JsonObject { ["template"] = template } }.ToJsonString(),
        V1Patch.PatchType.StrategicMergePatch);
      await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
        patch,
        "apps",
        "v1",
        @namespace,
        "deployments",
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public async Task<Result> SetCertificateApprovalAsync(
    string name,
    bool approved,
    CancellationToken cancellationToken = default) {
    try {
      var type = approved ? "Approved" : "Denied";
      var patch = new V1Patch(
        "{\"status\":{\"conditions\":[{\"type\":\"" + type + "\",\"status\":\"True\",\"reason\":\"ClusterConsole\",\"message\":\"" + type + "\"}]}}",
        V1Patch.PatchType.MergePatch);
      await _client.CertificatesV1.PatchCertificateSigningRequestApprovalAsync(
        patch,
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public async Task<Result<string>> CreateServiceAccountTokenAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken = default) {
    try {
      var body = new Authenticationv1TokenRequest {
        Spec = new V1TokenRequestSpec {
          Audiences = ["https://kubernetes.default.svc.default"],
          ExpirationSeconds = 3600
        }
      };
      var created = await _client.CoreV1.CreateNamespacedServiceAccountTokenAsync(
        body,
        name,
        @namespace,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      var token = created.Status?.Token;
      return string.IsNullOrEmpty(token)
        ? Result<string>.InternalServerError(null, "Token request returned an empty token.")
        : Result<string>.Ok(token);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<string>(ex);
    }
  }

  public async Task<Result<string>> AttachAsync(
    string podName,
    string @namespace,
    string? container,
    CancellationToken cancellationToken = default) {
    try {
      var webSocket = await _client.WebSocketNamespacedPodAttachAsync(
        podName,
        @namespace,
        container,
        stderr: true,
        stdin: false,
        stdout: true,
        tty: false,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      using var demux = new StreamDemuxer(webSocket);
      demux.Start();
      using var stdout = demux.GetStream(ChannelIndex.StdOut, null);
      using var stderr = demux.GetStream(ChannelIndex.StdErr, null);
      using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      limit.CancelAfter(TimeSpan.FromSeconds(4));
      var stdoutTask = ReadUntilAsync(stdout, limit.Token);
      var stderrTask = ReadUntilAsync(stderr, limit.Token);
      await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
      var text = Encoding.UTF8.GetString(stdoutTask.Result);
      var err = Encoding.UTF8.GetString(stderrTask.Result).TrimEnd();
      if (!string.IsNullOrEmpty(err))
        text = string.IsNullOrEmpty(text) ? err : text + "\n" + err;

      return Result<string>.Ok(text.TrimEnd());
    }
    catch (Exception ex) {
      return KubernetesResult.Map<string>(ex);
    }
  }

  public async Task<Result> AddEphemeralContainerAsync(
    string podName,
    string @namespace,
    string image,
    string? targetContainer,
    CancellationToken cancellationToken = default) {
    if (string.IsNullOrWhiteSpace(image))
      return Result.BadRequest("Image is required.");

    try {
      var pod = await GetAsync(
        new ResourceRef("", "v1", "pods", "Pod", true),
        podName,
        @namespace,
        cancellationToken).ConfigureAwait(false);
      if (!pod.IsSuccess || pod.Value is null)
        return Fail(pod);

      var existing = pod.Value["spec"]?["ephemeralContainers"] as JsonArray;
      var containers = existing is null
        ? new JsonArray()
        : JsonNode.Parse(existing.ToJsonString()) as JsonArray ?? new JsonArray();
      var debug = new JsonObject {
        ["name"] = "debug-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["image"] = image.Trim(),
        ["stdin"] = true,
        ["tty"] = true
      };
      if (!string.IsNullOrWhiteSpace(targetContainer))
        debug["targetContainerName"] = targetContainer;

      containers.Add(debug);
      var patch = new V1Patch(
        new JsonObject { ["spec"] = new JsonObject { ["ephemeralContainers"] = containers } }.ToJsonString(),
        V1Patch.PatchType.StrategicMergePatch);
      await _client.CoreV1.PatchNamespacedPodEphemeralcontainersAsync(
        patch,
        podName,
        @namespace,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  private async Task<Result<IReadOnlyList<JsonObject>>> ListOwnedReplicaSetsAsync(
    string name,
    string @namespace,
    CancellationToken cancellationToken) {
    var deployment = await GetAsync(
      new ResourceRef("apps", "v1", "deployments", "Deployment", true),
      name,
      @namespace,
      cancellationToken).ConfigureAwait(false);
    if (!deployment.IsSuccess || deployment.Value is null)
      return new Result<IReadOnlyList<JsonObject>>(null, false, deployment.Messages, deployment.StatusCode);

    var selector = RolloutHistory.LabelSelector(deployment.Value);
    var listed = await ListAsync(
      new ResourceRef("apps", "v1", "replicasets", "ReplicaSet", true),
      @namespace,
      cancellationToken,
      selector is null ? null : new ResourceListOptions { LabelSelector = selector }).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return listed;

    var owned = (listed.Value ?? [])
      .Where(item => RolloutHistory.Revision(item) > 0)
      .ToList();
    return Result<IReadOnlyList<JsonObject>>.Ok(owned);
  }

  private async Task<object> CreateOrReplaceAsync(
    JsonObject body,
    string group,
    string version,
    string plural,
    string name,
    bool namespaced,
    string? ns,
    CancellationToken cancellationToken) {
    var existing = namespaced
      ? await TryGet(() => _client.CustomObjects.GetNamespacedCustomObjectAsync(group, version, ns!, plural, name, cancellationToken: cancellationToken))
      : await TryGet(() => _client.CustomObjects.GetClusterCustomObjectAsync(group, version, plural, name, cancellationToken: cancellationToken));

    if (existing is null) {
      if (body["metadata"] is JsonObject createMeta)
        createMeta.Remove("resourceVersion");

      return namespaced
        ? await _client.CustomObjects.CreateNamespacedCustomObjectAsync(body, group, version, ns!, plural, cancellationToken: cancellationToken).ConfigureAwait(false)
        : await _client.CustomObjects.CreateClusterCustomObjectAsync(body, group, version, plural, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    return namespaced
      ? await _client.CustomObjects.ReplaceNamespacedCustomObjectAsync(body, group, version, ns!, plural, name, cancellationToken: cancellationToken).ConfigureAwait(false)
      : await _client.CustomObjects.ReplaceClusterCustomObjectAsync(body, group, version, plural, name, cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  private static Result Fail<T>(Result<T> source) {
    var message = source.Messages is { Count: > 0 }
      ? string.Join("; ", source.Messages)
      : "request failed";
    return source.StatusCode switch {
      HttpStatusCode.BadRequest => Result.BadRequest(message),
      HttpStatusCode.Unauthorized => Result.Unauthorized(message),
      HttpStatusCode.Forbidden => Result.Forbidden(message),
      HttpStatusCode.NotFound => Result.NotFound(message),
      HttpStatusCode.Conflict => Result.Conflict(message),
      HttpStatusCode.UnprocessableEntity => Result.UnprocessableEntity(message),
      _ => Result.InternalServerError(message)
    };
  }

  private static bool IsApplyUnsupported(Exception ex) {
    if (ex is HttpRequestException { StatusCode: HttpStatusCode.UnsupportedMediaType })
      return true;

    var text = ex.ToString();
    return text.Contains("415", StringComparison.Ordinal)
      || text.Contains("UnsupportedMediaType", StringComparison.OrdinalIgnoreCase);
  }

  private static async Task<byte[]> ReadUntilAsync(Stream stream, CancellationToken cancellationToken) {
    using var buffer = new MemoryStream();
    var chunk = new byte[4096];
    try {
      while (!cancellationToken.IsCancellationRequested) {
        var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
        if (read == 0)
          break;
        buffer.Write(chunk, 0, read);
        if (buffer.Length >= 64 * 1024)
          break;
      }
    }
    catch (OperationCanceledException) {
    }

    return buffer.ToArray();
  }
}
