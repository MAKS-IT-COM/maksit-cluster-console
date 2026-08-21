using System.Net;
using System.Text;
using System.Net.Sockets;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using k8s;
using k8s.Models;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

public sealed class ClusterSession : IClusterSession {
  private readonly Kubernetes _client;

  public ClusterSession(string contextName, KubernetesClientConfiguration configuration) {
    ContextName = contextName;
    _client = new Kubernetes(configuration);
  }

  public string ContextName { get; }

  public IKubernetes Kubernetes => _client;

  public async Task<Result<IReadOnlyList<JsonObject>>> ListAsync(
    ResourceRef resource,
    string? @namespace,
    CancellationToken cancellationToken = default) {
    try {
      object raw;
      if (IsCoreNamespaces(resource))
        raw = await ListNamespacesPagedAsync(cancellationToken).ConfigureAwait(false);
      else if (!resource.Namespaced || string.IsNullOrWhiteSpace(@namespace) || @namespace == "all")
        raw = await ListPagedAsync(
          cont => _client.CustomObjects.ListClusterCustomObjectAsync(
            resource.Group,
            resource.Version,
            resource.Plural,
            continueParameter: cont,
            cancellationToken: cancellationToken),
          cancellationToken).ConfigureAwait(false);
      else
        raw = await ListPagedAsync(
          cont => _client.CustomObjects.ListNamespacedCustomObjectAsync(
            resource.Group,
            resource.Version,
            @namespace,
            resource.Plural,
            continueParameter: cont,
            cancellationToken: cancellationToken),
          cancellationToken).ConfigureAwait(false);

      return Result<IReadOnlyList<JsonObject>>.Ok(KubernetesResult.Items(raw));
    }
    catch (Exception ex) {
      return KubernetesResult.Map<IReadOnlyList<JsonObject>>(ex);
    }
  }

  public async Task<Result<JsonObject>> GetAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default) {
    try {
      object raw;
      if (resource.Namespaced)
        raw = await _client.CustomObjects.GetNamespacedCustomObjectAsync(
          resource.Group,
          resource.Version,
          @namespace ?? "default",
          resource.Plural,
          name,
          cancellationToken: cancellationToken).ConfigureAwait(false);
      else
        raw = await _client.CustomObjects.GetClusterCustomObjectAsync(
          resource.Group,
          resource.Version,
          resource.Plural,
          name,
          cancellationToken: cancellationToken).ConfigureAwait(false);

      var obj = KubernetesResult.ToObject(raw);
      return obj is null
        ? Result<JsonObject>.NotFound(null, "resource not found")
        : Result<JsonObject>.Ok(obj);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<JsonObject>(ex);
    }
  }

  public async Task<Result<JsonObject>> ApplyAsync(
    JsonObject document,
    ResourceRef? resource = null,
    CancellationToken cancellationToken = default) {
    try {
      var meta = document["metadata"] as JsonObject;
      var name = meta?["name"]?.GetValue<string>();
      var ns = meta?["namespace"]?.GetValue<string>();
      var apiVersion = document["apiVersion"]?.GetValue<string>() ?? resource?.Version ?? "v1";
      var kind = document["kind"]?.GetValue<string>() ?? resource?.Kind;
      if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind))
        return Result<JsonObject>.BadRequest(null, "document requires metadata.name and kind");

      var (group, version) = resource is null
        ? SplitApiVersion(apiVersion)
        : (resource.Group, resource.Version);
      var plural = resource?.Plural ?? GuessPlural(kind);
      var namespaced = resource?.Namespaced ?? !string.IsNullOrWhiteSpace(ns);
      if (namespaced && string.IsNullOrWhiteSpace(ns))
        ns = "default";

      var body = ResourceDocumentPrepare(document);

      object raw;
      var existing = namespaced
        ? await TryGet(() => _client.CustomObjects.GetNamespacedCustomObjectAsync(group, version, ns!, plural, name, cancellationToken: cancellationToken))
        : await TryGet(() => _client.CustomObjects.GetClusterCustomObjectAsync(group, version, plural, name, cancellationToken: cancellationToken));

      if (existing is null) {
        if (body["metadata"] is JsonObject createMeta)
          createMeta.Remove("resourceVersion");

        raw = namespaced
          ? await _client.CustomObjects.CreateNamespacedCustomObjectAsync(body, group, version, ns!, plural, cancellationToken: cancellationToken).ConfigureAwait(false)
          : await _client.CustomObjects.CreateClusterCustomObjectAsync(body, group, version, plural, cancellationToken: cancellationToken).ConfigureAwait(false);
      }
      else {
        raw = namespaced
          ? await _client.CustomObjects.ReplaceNamespacedCustomObjectAsync(body, group, version, ns!, plural, name, cancellationToken: cancellationToken).ConfigureAwait(false)
          : await _client.CustomObjects.ReplaceClusterCustomObjectAsync(body, group, version, plural, name, cancellationToken: cancellationToken).ConfigureAwait(false);
      }

      var obj = KubernetesResult.ToObject(raw);
      return obj is null
        ? Result<JsonObject>.InternalServerError(null, "apply returned empty body")
        : Result<JsonObject>.Ok(obj);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<JsonObject>(ex);
    }
  }

  private static JsonObject ResourceDocumentPrepare(JsonObject document) {
    var clone = JsonNode.Parse(document.ToJsonString()) as JsonObject ?? document;
    clone.Remove("status");
    if (clone["metadata"] is JsonObject meta) {
      meta.Remove("managedFields");
      meta.Remove("generation");
      meta.Remove("creationTimestamp");
      meta.Remove("deletionTimestamp");
      meta.Remove("selfLink");
    }

    return clone;
  }

  public async Task<Result> DeleteAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    bool force = false,
    CancellationToken cancellationToken = default) {
    try {
      await DeleteOnceAsync(resource, name, @namespace, force, cancellationToken).ConfigureAwait(false);
      if (!force)
        return Result.Ok();

      if (await ExistsAsync(resource, name, @namespace, cancellationToken).ConfigureAwait(false))
        await ClearFinalizersAsync(resource, name, @namespace, cancellationToken).ConfigureAwait(false);

      return Result.Ok();
    }
    catch (Exception ex) {
      var mapped = KubernetesResult.Map(ex);
      return mapped.StatusCode == HttpStatusCode.NotFound ? Result.Ok() : mapped;
    }
  }

  public async Task<Result> ForceDeleteNamespaceAsync(string name, CancellationToken cancellationToken = default) {
    var swept = await SweepNamespaceAsync(name, cancellationToken).ConfigureAwait(false);
    if (!swept.IsSuccess)
      return swept;

    try {
      await _client.CoreV1.DeleteNamespaceAsync(
        name,
        new V1DeleteOptions {
          GracePeriodSeconds = 0,
          PropagationPolicy = "Background"
        },
        gracePeriodSeconds: 0,
        propagationPolicy: "Background",
        cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) {
      var mapped = KubernetesResult.Map(ex);
      if (mapped.StatusCode != HttpStatusCode.NotFound
          && mapped.StatusCode != HttpStatusCode.Conflict)
        return mapped;
    }

    try {
      var ns = await _client.CoreV1.ReadNamespaceAsync(name, cancellationToken: cancellationToken)
        .ConfigureAwait(false);
      if (ns.Metadata.Finalizers is { Count: > 0 }) {
        ns.Metadata.Finalizers.Clear();
        await _client.CoreV1.ReplaceNamespaceFinalizeAsync(ns, name, cancellationToken: cancellationToken)
          .ConfigureAwait(false);
      }
    }
    catch (Exception ex) {
      var mapped = KubernetesResult.Map(ex);
      if (mapped.StatusCode != HttpStatusCode.NotFound)
        return mapped;
    }

    return Result.Ok();
  }

  public async Task<Result> ScaleAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    int replicas,
    CancellationToken cancellationToken = default) {
    try {
      var ns = @namespace ?? "default";
      var patch = new V1Patch("{\"spec\":{\"replicas\":" + replicas + "}}", V1Patch.PatchType.MergePatch);
      await _client.CustomObjects.PatchNamespacedCustomObjectScaleAsync(
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

  public async Task<Result> PatchContainerResourcesAsync(
    WorkloadContainerLimit row,
    string cpuLimit,
    string memoryLimit,
    CancellationToken cancellationToken = default) {
    try {
      var (group, version, plural, namespacedTemplate) = WorkloadGvr(row.WorkloadKind);
      var limits = new JsonObject();
      if (!string.IsNullOrWhiteSpace(cpuLimit))
        limits["cpu"] = cpuLimit.Trim();
      if (!string.IsNullOrWhiteSpace(memoryLimit))
        limits["memory"] = memoryLimit.Trim();

      var container = new JsonObject {
        ["name"] = row.Container,
        ["resources"] = new JsonObject { ["limits"] = limits }
      };
      var spec = new JsonObject {
        [row.Init ? "initContainers" : "containers"] = new JsonArray(container)
      };
      var body = namespacedTemplate
        ? new JsonObject { ["spec"] = new JsonObject { ["template"] = new JsonObject { ["spec"] = spec } } }
        : new JsonObject { ["spec"] = spec };

      var patch = new V1Patch(body.ToJsonString(), V1Patch.PatchType.StrategicMergePatch);
      await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
        patch,
        group,
        version,
        row.Namespace,
        plural,
        row.WorkloadName,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  private static (string Group, string Version, string Plural, bool Template) WorkloadGvr(string kind) =>
    kind switch {
      "Deployment" => ("apps", "v1", "deployments", true),
      "ReplicaSet" => ("apps", "v1", "replicasets", true),
      "StatefulSet" => ("apps", "v1", "statefulsets", true),
      "DaemonSet" => ("apps", "v1", "daemonsets", true),
      "Job" => ("batch", "v1", "jobs", true),
      _ => ("", "v1", "pods", false)
    };

  public async Task<Result> RestartAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default) {
    try {
      var ns = @namespace ?? "default";
      var now = DateTime.UtcNow.ToString("o");
      var patchJson = "{\"spec\":{\"template\":{\"metadata\":{\"annotations\":{\"kubectl.kubernetes.io/restartedAt\":\"" + now + "\"}}}}}";
      var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);
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

  public async Task<Result<string>> GetLogsAsync(
    string podName,
    string @namespace,
    string? container,
    bool previous,
    int tailLines,
    CancellationToken cancellationToken = default) {
    try {
      var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(
        podName,
        @namespace,
        container: container,
        previous: previous,
        tailLines: tailLines,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      using var reader = new StreamReader(stream);
      var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
      return Result<string>.Ok(text);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<string>(ex);
    }
  }

  public async IAsyncEnumerable<string> FollowLogsAsync(
    string podName,
    string @namespace,
    string? container,
    [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    var response = await _client.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
      podName,
      @namespace,
      container: container,
      follow: true,
      tailLines: 200,
      cancellationToken: cancellationToken).ConfigureAwait(false);

    try {
      var stream = response.Body;
      if (stream is null)
        yield break;

      await foreach (var line in ReadLogLinesAsync(stream, cancellationToken).ConfigureAwait(false))
        yield return line;
    }
    finally {
      response.Dispose();
    }
  }

  internal static async IAsyncEnumerable<string> ReadLogLinesAsync(
    Stream stream,
    [EnumeratorCancellation] CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(stream);
    using var reader = new StreamReader(stream);
    using var registration = cancellationToken.Register(stream.Dispose);
    while (!cancellationToken.IsCancellationRequested) {
      string? line;
      try {
        line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is ObjectDisposedException or IOException or OperationCanceledException) {
        yield break;
      }

      if (line is null)
        yield break;

      yield return line;
    }
  }

  public async Task<Result<PortForwardHandle>> PortForwardAsync(
    string podName,
    string @namespace,
    int containerPort,
    int localPort,
    int requestedPort = 0,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget = null,
    CancellationToken cancellationToken = default) {
    try {
      var listeners = BindLoopback(localPort);
      var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      var handle = new PortForwardHandle(
        podName,
        @namespace,
        containerPort,
        localPort,
        cts,
        () => {
          cts.Cancel();
          foreach (var listener in listeners)
            listener.Stop();
        },
        requestedPort);
      foreach (var listener in listeners)
        _ = AcceptAsync(listener, handle, resolveTarget, cts.Token);

      return Result<PortForwardHandle>.Ok(handle);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<PortForwardHandle>(ex);
    }
  }

  public async Task<Result<IReadOnlyList<JsonObject>>> ListCustomResourceDefinitionsAsync(
    CancellationToken cancellationToken = default) {
    try {
      var list = await _client.ApiextensionsV1.ListCustomResourceDefinitionAsync(cancellationToken: cancellationToken)
        .ConfigureAwait(false);
      var items = list.Items.Select(crd => KubernetesResult.ToObject(crd)!).Where(o => o is not null).Cast<JsonObject>().ToList();
      return Result<IReadOnlyList<JsonObject>>.Ok(items);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<IReadOnlyList<JsonObject>>(ex);
    }
  }

  public async Task<Result<bool>> HasApiGroupAsync(string group, CancellationToken cancellationToken = default) {
    try {
      var list = await _client.ApiextensionsV1.ListCustomResourceDefinitionAsync(cancellationToken: cancellationToken)
        .ConfigureAwait(false);
      var found = list.Items.Any(c => c.Spec.Group == group);
      return Result<bool>.Ok(found);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<bool>(ex);
    }
  }

  public async Task<Result<ClusterSummary>> GetSummaryAsync(CancellationToken cancellationToken = default) {
    try {
      var version = await _client.Version.GetCodeAsync(cancellationToken).ConfigureAwait(false);
      var nodes = await _client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
      var pods = await _client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result<ClusterSummary>.Ok(new ClusterSummary(
        version.GitVersion,
        version.Platform,
        nodes.Items.Count,
        pods.Items.Count));
    }
    catch (Exception ex) {
      return KubernetesResult.Map<ClusterSummary>(ex);
    }
  }

  public async Task<Result<ClusterUsage>> GetClusterUsageAsync(CancellationToken cancellationToken = default) {
    try {
      var version = await _client.Version.GetCodeAsync(cancellationToken).ConfigureAwait(false);
      var nodes = await _client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
      var pods = await _client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
      var replicaSets = await _client.AppsV1.ListReplicaSetForAllNamespacesAsync(cancellationToken: cancellationToken)
        .ConfigureAwait(false);
      var metrics = await GetNodeMetricsAsync(cancellationToken).ConfigureAwait(false);
      var metricsAvailable = metrics.IsSuccess && metrics.Value is { Count: > 0 };
      var (cpu, memory, podSlice, nodeUsages) = ClusterMetrics.From(
        nodes.Items,
        pods.Items,
        metricsAvailable ? metrics.Value : null);
      var containerLimits = ContainerLimits.From(pods.Items, replicaSets.Items);

      return Result<ClusterUsage>.Ok(new ClusterUsage(
        version.GitVersion,
        version.Platform,
        nodes.Items.Count,
        cpu,
        memory,
        podSlice,
        nodeUsages,
        containerLimits,
        metricsAvailable,
        metricsAvailable
          ? null
          : "No live usage from metrics-server (metrics.k8s.io). Usage bars stay empty until it is installed. Requests/limits come from pod specs."));
    }
    catch (Exception ex) {
      return KubernetesResult.Map<ClusterUsage>(ex);
    }
  }

  public async Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetPodMetricsAsync(
    string? @namespace,
    CancellationToken cancellationToken = default) {
    try {
      var raw = string.IsNullOrWhiteSpace(@namespace) || @namespace == "all"
        ? await _client.CustomObjects.ListClusterCustomObjectAsync("metrics.k8s.io", "v1beta1", "pods", cancellationToken: cancellationToken)
        : await _client.CustomObjects.ListNamespacedCustomObjectAsync("metrics.k8s.io", "v1beta1", @namespace, "pods", cancellationToken: cancellationToken);

      var map = new Dictionary<string, ResourceMetrics>(StringComparer.Ordinal);
      foreach (var item in KubernetesResult.Items(raw)) {
        var name = item["metadata"]?["name"]?.GetValue<string>() ?? string.Empty;
        var ns = item["metadata"]?["namespace"]?.GetValue<string>();
        var (cpu, mem) = SumPodMetrics(item);
        map[$"{ns}/{name}"] = new ResourceMetrics(name, ns, cpu, mem);
      }

      return Result<IReadOnlyDictionary<string, ResourceMetrics>>.Ok(map);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<IReadOnlyDictionary<string, ResourceMetrics>>(ex);
    }
  }

  public async Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetNodeMetricsAsync(
    CancellationToken cancellationToken = default) {
    try {
      var raw = await _client.CustomObjects.ListClusterCustomObjectAsync(
        "metrics.k8s.io",
        "v1beta1",
        "nodes",
        cancellationToken: cancellationToken).ConfigureAwait(false);

      var map = new Dictionary<string, ResourceMetrics>(StringComparer.Ordinal);
      foreach (var item in KubernetesResult.Items(raw)) {
        var name = item["metadata"]?["name"]?.GetValue<string>() ?? string.Empty;
        var usage = item["usage"] as JsonObject;
        var cpu = usage?["cpu"]?.ToString() ?? "-";
        var mem = usage?["memory"]?.ToString() ?? "-";
        map[name] = new ResourceMetrics(name, null, cpu, mem);
      }

      return Result<IReadOnlyDictionary<string, ResourceMetrics>>.Ok(map);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<IReadOnlyDictionary<string, ResourceMetrics>>(ex);
    }
  }

  public async Task<Result<IReadOnlyList<HelmReleaseInfo>>> ListHelmReleasesAsync(
    string? @namespace,
    CancellationToken cancellationToken = default) {
    try {
      V1SecretList secrets;
      if (string.IsNullOrWhiteSpace(@namespace) || @namespace == "all")
        secrets = await _client.CoreV1.ListSecretForAllNamespacesAsync(
          labelSelector: "owner=helm",
          cancellationToken: cancellationToken).ConfigureAwait(false);
      else
        secrets = await _client.CoreV1.ListNamespacedSecretAsync(
          @namespace,
          labelSelector: "owner=helm",
          cancellationToken: cancellationToken).ConfigureAwait(false);

      var releases = secrets.Items
        .Select(TryDecodeHelm)
        .Where(r => r is not null)
        .Cast<HelmReleaseInfo>()
        .GroupBy(r => (r.Name, r.Namespace))
        .Select(g => g.MaxBy(r => r.Updated)!)
        .OrderBy(r => r.Namespace)
        .ThenBy(r => r.Name)
        .ToList();

      return Result<IReadOnlyList<HelmReleaseInfo>>.Ok(releases);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<IReadOnlyList<HelmReleaseInfo>>(ex);
    }
  }

  public async Task<Result<string>> ExecAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    CancellationToken cancellationToken = default) {
    var result = await ExecBytesAsync(podName, @namespace, container, command, null, cancellationToken)
      .ConfigureAwait(false);
    if (!result.IsSuccess || result.Value is null)
      return new Result<string>(null, false, result.Messages, result.StatusCode);

    var text = Encoding.UTF8.GetString(result.Value.Stdout);
    if (!string.IsNullOrEmpty(result.Value.Stderr))
      text = string.IsNullOrEmpty(text) ? result.Value.Stderr : text + "\n" + result.Value.Stderr;

    return Result<string>.Ok(text.TrimEnd());
  }

  public async Task<Result<ExecBytesResult>> ExecBytesAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    byte[]? stdin = null,
    CancellationToken cancellationToken = default) {
    try {
      var cmd = command.Count == 0 ? new[] { "sh", "-c", "echo ok" } : command.ToArray();
      var webSocket = await _client.WebSocketNamespacedPodExecAsync(
        podName,
        @namespace,
        command: cmd,
        container: container,
        stderr: true,
        stdin: stdin is not null,
        stdout: true,
        tty: false,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      using var demux = new StreamDemuxer(webSocket);
      demux.Start();
      using var stdout = demux.GetStream(ChannelIndex.StdOut, null);
      using var stderr = demux.GetStream(ChannelIndex.StdErr, null);
      using var error = demux.GetStream(ChannelIndex.Error, null);
      var stdoutTask = ReadAllAsync(stdout, cancellationToken);
      var stderrTask = ReadAllAsync(stderr, cancellationToken);
      var errorTask = ReadAllAsync(error, cancellationToken);

      if (stdin is not null) {
        using (var stdinStream = demux.GetStream(null, ChannelIndex.StdIn)) {
          if (stdin.Length > 0)
            await stdinStream.WriteAsync(stdin, cancellationToken).ConfigureAwait(false);

          await stdinStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
      }

      await Task.WhenAll(stdoutTask, stderrTask, errorTask).ConfigureAwait(false);
      var err = Encoding.UTF8.GetString(stderrTask.Result).TrimEnd();
      var status = Encoding.UTF8.GetString(errorTask.Result).TrimEnd();
      if (string.IsNullOrEmpty(err))
        err = status;

      return Result<ExecBytesResult>.Ok(new ExecBytesResult(stdoutTask.Result, err));
    }
    catch (Exception ex) {
      return KubernetesResult.Map<ExecBytesResult>(ex);
    }
  }

  private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken) {
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
    return buffer.ToArray();
  }

  public async Task<Result> CordonAsync(string nodeName, bool unschedulable, CancellationToken cancellationToken = default) {
    try {
      var patch = new V1Patch(
        "{\"spec\":{\"unschedulable\":" + unschedulable.ToString().ToLowerInvariant() + "}}",
        V1Patch.PatchType.MergePatch);
      await _client.CoreV1.PatchNodeAsync(patch, nodeName, cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public async Task<Result> DrainAsync(string nodeName, CancellationToken cancellationToken = default) {
    var cordon = await CordonAsync(nodeName, true, cancellationToken).ConfigureAwait(false);
    if (!cordon.IsSuccess)
      return cordon;

    try {
      var pods = await _client.CoreV1.ListPodForAllNamespacesAsync(
        fieldSelector: $"spec.nodeName={nodeName}",
        cancellationToken: cancellationToken).ConfigureAwait(false);

      foreach (var pod in pods.Items.Where(p => p.Metadata.OwnerReferences?.Any(o => o.Kind == "DaemonSet") != true)) {
        var eviction = new V1Eviction {
          Metadata = new V1ObjectMeta {
            Name = pod.Metadata.Name,
            NamespaceProperty = pod.Metadata.NamespaceProperty
          }
        };
        try {
          await _client.CoreV1.CreateNamespacedPodEvictionAsync(eviction, pod.Metadata.Name, pod.Metadata.NamespaceProperty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        }
        catch {
          await _client.CoreV1.DeleteNamespacedPodAsync(pod.Metadata.Name, pod.Metadata.NamespaceProperty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        }
      }

      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public async Task<Result> TriggerCronJobAsync(string name, string @namespace, CancellationToken cancellationToken = default) {
    try {
      var cron = await _client.BatchV1.ReadNamespacedCronJobAsync(name, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
      var job = new V1Job {
        Metadata = new V1ObjectMeta {
          Name = $"{name}-manual-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
          NamespaceProperty = @namespace,
          OwnerReferences = [
            new V1OwnerReference {
              ApiVersion = "batch/v1",
              Kind = "CronJob",
              Name = name,
              Uid = cron.Metadata.Uid
            }
          ]
        },
        Spec = cron.Spec.JobTemplate.Spec
      };
      await _client.BatchV1.CreateNamespacedJobAsync(job, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  public void Dispose() => _client.Dispose();

  private async Task DeleteOnceAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    bool force,
    CancellationToken cancellationToken) {
    int? grace = force ? 0 : null;
    var policy = force ? "Background" : null;
    var body = force
      ? new V1DeleteOptions { GracePeriodSeconds = 0, PropagationPolicy = "Background" }
      : null;

    if (resource.Namespaced)
      await _client.CustomObjects.DeleteNamespacedCustomObjectAsync(
        resource.Group,
        resource.Version,
        @namespace ?? "default",
        resource.Plural,
        name,
        body,
        gracePeriodSeconds: grace,
        propagationPolicy: policy,
        cancellationToken: cancellationToken).ConfigureAwait(false);
    else
      await _client.CustomObjects.DeleteClusterCustomObjectAsync(
        resource.Group,
        resource.Version,
        resource.Plural,
        name,
        body,
        gracePeriodSeconds: grace,
        propagationPolicy: policy,
        cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  private async Task<bool> ExistsAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken) {
    var got = await GetAsync(resource, name, @namespace, cancellationToken).ConfigureAwait(false);
    return got.IsSuccess && got.Value is not null;
  }

  private async Task ClearFinalizersAsync(
    ResourceRef resource,
    string name,
    string? @namespace,
    CancellationToken cancellationToken) {
    var patch = new V1Patch("""{"metadata":{"finalizers":[]}}""", V1Patch.PatchType.MergePatch);
    if (resource.Namespaced)
      await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
        patch,
        resource.Group,
        resource.Version,
        @namespace ?? "default",
        resource.Plural,
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
    else
      await _client.CustomObjects.PatchClusterCustomObjectAsync(
        patch,
        resource.Group,
        resource.Version,
        resource.Plural,
        name,
        cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  private async Task<object> ListNamespacesPagedAsync(CancellationToken cancellationToken) {
    var listed = new List<JsonObject>();
    string? continueToken = null;
    do {
      var list = await _client.CoreV1.ListNamespaceAsync(
        continueParameter: continueToken,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      foreach (var ns in list.Items ?? []) {
        var name = ns.Metadata?.Name;
        if (string.IsNullOrEmpty(name))
          continue;
        listed.Add(NamespaceListMerge.Document(
          name,
          ns.Status?.Phase ?? "Active",
          ToOffset(ns.Metadata?.CreationTimestamp)));
      }

      continueToken = list.Metadata?.ContinueProperty;
    } while (!string.IsNullOrEmpty(continueToken) && !cancellationToken.IsCancellationRequested);

    var pods = await ListPodNamespacesAsync(cancellationToken).ConfigureAwait(false);
    var merged = NamespaceListMerge.WithOrphansFromPods(listed, pods);
    var items = new JsonArray();
    foreach (var item in merged)
      items.Add(item);
    return new JsonObject { ["items"] = items };
  }

  private async Task<List<(string Namespace, DateTimeOffset? Created)>> ListPodNamespacesAsync(
    CancellationToken cancellationToken) {
    var pods = new List<(string Namespace, DateTimeOffset? Created)>();
    string? continueToken = null;
    do {
      var list = await _client.CoreV1.ListPodForAllNamespacesAsync(
        continueParameter: continueToken,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      foreach (var pod in list.Items ?? []) {
        var ns = pod.Metadata?.NamespaceProperty;
        if (string.IsNullOrEmpty(ns))
          continue;
        pods.Add((ns, ToOffset(pod.Metadata?.CreationTimestamp)));
      }

      continueToken = list.Metadata?.ContinueProperty;
    } while (!string.IsNullOrEmpty(continueToken) && !cancellationToken.IsCancellationRequested);

    return pods;
  }

  private async Task<Result> SweepNamespaceAsync(string name, CancellationToken cancellationToken) {
    try {
      await DeleteAllAsync(
        async () => (await _client.AppsV1.ListNamespacedDeploymentAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false)).Items,
        item => _client.AppsV1.DeleteNamespacedDeploymentAsync(
          item.Metadata.Name, name, gracePeriodSeconds: 0, propagationPolicy: "Background",
          cancellationToken: cancellationToken)).ConfigureAwait(false);
      await DeleteAllAsync(
        async () => (await _client.AppsV1.ListNamespacedStatefulSetAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false)).Items,
        item => _client.AppsV1.DeleteNamespacedStatefulSetAsync(
          item.Metadata.Name, name, gracePeriodSeconds: 0, propagationPolicy: "Background",
          cancellationToken: cancellationToken)).ConfigureAwait(false);
      await DeleteAllAsync(
        async () => (await _client.AppsV1.ListNamespacedDaemonSetAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false)).Items,
        item => _client.AppsV1.DeleteNamespacedDaemonSetAsync(
          item.Metadata.Name, name, gracePeriodSeconds: 0, propagationPolicy: "Background",
          cancellationToken: cancellationToken)).ConfigureAwait(false);
      await DeleteAllAsync(
        async () => (await _client.BatchV1.ListNamespacedJobAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false)).Items,
        item => _client.BatchV1.DeleteNamespacedJobAsync(
          item.Metadata.Name, name, gracePeriodSeconds: 0, propagationPolicy: "Background",
          cancellationToken: cancellationToken)).ConfigureAwait(false);
      await DeleteAllAsync(
        async () => (await _client.AppsV1.ListNamespacedReplicaSetAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false)).Items,
        item => _client.AppsV1.DeleteNamespacedReplicaSetAsync(
          item.Metadata.Name, name, gracePeriodSeconds: 0, propagationPolicy: "Background",
          cancellationToken: cancellationToken)).ConfigureAwait(false);
      await DeletePodsInNamespaceAsync(name, cancellationToken).ConfigureAwait(false);
      return Result.Ok();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }
  }

  private async Task DeletePodsInNamespaceAsync(string name, CancellationToken cancellationToken) {
    V1PodList list;
    try {
      list = await _client.CoreV1.ListNamespacedPodAsync(name, cancellationToken: cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) {
      var mapped = KubernetesResult.Map(ex);
      if (mapped.StatusCode == HttpStatusCode.NotFound)
        return;
      throw;
    }

    foreach (var pod in list.Items ?? []) {
      if (string.IsNullOrEmpty(pod.Metadata?.Name))
        continue;
      await IgnoreMissing(() => _client.CoreV1.DeleteNamespacedPodAsync(
        pod.Metadata.Name,
        name,
        gracePeriodSeconds: 0,
        propagationPolicy: "Background",
        cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
  }

  private async Task DeleteAllAsync<TItem>(
    Func<Task<IList<TItem>>> list,
    Func<TItem, Task> delete)
    where TItem : IKubernetesObject<V1ObjectMeta> {
    IList<TItem> items;
    try {
      items = await list().ConfigureAwait(false);
    }
    catch (Exception ex) {
      var mapped = KubernetesResult.Map(ex);
      if (mapped.StatusCode == HttpStatusCode.NotFound)
        return;
      throw;
    }

    foreach (var item in items ?? []) {
      if (string.IsNullOrEmpty(item.Metadata?.Name))
        continue;
      await IgnoreMissing(() => delete(item)).ConfigureAwait(false);
    }
  }

  private static async Task IgnoreMissing(Func<Task> action) {
    try {
      await action().ConfigureAwait(false);
    }
    catch (Exception ex) {
      var mapped = KubernetesResult.Map(ex);
      if (mapped.StatusCode != HttpStatusCode.NotFound
          && mapped.StatusCode != HttpStatusCode.Conflict)
        throw;
    }
  }

  private static DateTimeOffset? ToOffset(DateTime? value) =>
    value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

  private static bool IsCoreNamespaces(ResourceRef resource) =>
    string.IsNullOrEmpty(resource.Group)
    && string.Equals(resource.Plural, "namespaces", StringComparison.OrdinalIgnoreCase);

  private static async Task<object> ListPagedAsync(
    Func<string?, Task<object>> page,
    CancellationToken cancellationToken) {
    var items = new JsonArray();
    string? continueToken = null;
    do {
      var raw = await page(continueToken).ConfigureAwait(false);
      var root = KubernetesResult.ToObject(raw);
      foreach (var item in KubernetesResult.Items(raw))
        items.Add(item.DeepClone());
      continueToken = KubernetesResult.ContinueToken(root);
    } while (!string.IsNullOrEmpty(continueToken) && !cancellationToken.IsCancellationRequested);

    return new JsonObject { ["items"] = items };
  }

  private static async Task<object?> TryGet(Func<Task<object>> get) {
    try {
      return await get().ConfigureAwait(false);
    }
    catch {
      return null;
    }
  }

  private static (string Group, string Version) SplitApiVersion(string apiVersion) {
    var parts = apiVersion.Split('/', 2);
    return parts.Length == 1 ? ("", parts[0]) : (parts[0], parts[1]);
  }

  private static string GuessPlural(string kind) {
    if (kind.EndsWith("s", StringComparison.OrdinalIgnoreCase))
      return kind.ToLowerInvariant();
    if (kind.EndsWith("y", StringComparison.OrdinalIgnoreCase) && kind.Length > 1)
      return kind[..^1].ToLowerInvariant() + "ies";
    return kind.ToLowerInvariant() + "s";
  }

  private static (string Cpu, string Memory) SumPodMetrics(JsonObject item) {
    var containers = item["containers"] as JsonArray;
    if (containers is null)
      return ("-", "-");

    var cpu = "-";
    var mem = "-";
    foreach (var c in containers.OfType<JsonObject>()) {
      var usage = c["usage"] as JsonObject;
      cpu = usage?["cpu"]?.ToString() ?? cpu;
      mem = usage?["memory"]?.ToString() ?? mem;
    }

    return (cpu, mem);
  }

  private static HelmReleaseInfo? TryDecodeHelm(V1Secret secret) {
    try {
      if (secret.Data is null || !secret.Data.TryGetValue("release", out var bytes))
        return null;

      var decoded = Convert.FromBase64String(Encoding.UTF8.GetString(bytes));
      using var gzip = new GZipStream(new MemoryStream(decoded), CompressionMode.Decompress);
      using var reader = new StreamReader(gzip);
      var json = reader.ReadToEnd();
      var node = JsonNode.Parse(json) as JsonObject;
      if (node is null)
        return null;

      var info = node["info"] as JsonObject;
      var chart = node["chart"] as JsonObject;
      var metadata = chart?["metadata"] as JsonObject;
      DateTimeOffset? updated = null;
      if (DateTimeOffset.TryParse(info?["last_deployed"]?.ToString(), out var parsed))
        updated = parsed;

      return new HelmReleaseInfo(
        node["name"]?.GetValue<string>() ?? secret.Metadata.Name,
        node["namespace"]?.GetValue<string>() ?? secret.Metadata.NamespaceProperty,
        info?["status"]?.ToString() ?? "unknown",
        metadata?["name"]?.ToString() ?? "",
        metadata?["appVersion"]?.ToString() ?? "",
        updated);
    }
    catch {
      var labels = secret.Metadata.Labels;
      return new HelmReleaseInfo(
        Label(labels, "name") ?? secret.Metadata.Name,
        secret.Metadata.NamespaceProperty,
        Label(labels, "status") ?? "unknown",
        Label(labels, "chart") ?? "",
        "",
        secret.Metadata.CreationTimestamp is DateTime ts ? new DateTimeOffset(ts) : null);
    }
  }

  private static string? Label(IDictionary<string, string>? labels, string key) =>
    labels is not null && labels.TryGetValue(key, out var value) ? value : null;

  private static List<TcpListener> BindLoopback(int port) {
    SocketException? last = null;
    var listeners = new List<TcpListener>(2);
    foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }) {
      try {
        var listener = new TcpListener(address, port);
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
          listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);

        listener.Start();
        listeners.Add(listener);
      }
      catch (SocketException ex) {
        last = ex;
      }
    }

    if (listeners.Count == 0)
      throw last ?? new SocketException((int)SocketError.AddressNotAvailable);

    return listeners;
  }

  private async Task AcceptAsync(
    TcpListener listener,
    PortForwardHandle handle,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget,
    CancellationToken cancellationToken) {
    try {
      while (!cancellationToken.IsCancellationRequested) {
        var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        client.NoDelay = true;
        _ = PumpConnectionAsync(client, handle, resolveTarget, cancellationToken);
      }
    }
    catch (OperationCanceledException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (SocketException) {
    }
  }

  private async Task PumpConnectionAsync(
    TcpClient tcp,
    PortForwardHandle handle,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget,
    CancellationToken cancellationToken) {
    StreamDemuxer? demux = null;
    try {
      var podName = handle.PodName;
      var @namespace = handle.Namespace;
      var containerPort = handle.ContainerPort;
      if (resolveTarget is not null) {
        var resolved = await resolveTarget(cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null) {
          tcp.Dispose();
          return;
        }

        podName = resolved.Value.PodName;
        @namespace = resolved.Value.Namespace;
        containerPort = resolved.Value.ContainerPort;
        handle.Retarget(podName, @namespace, containerPort);
      }

      var webSocket = await _client.WebSocketNamespacedPodPortForwardAsync(
        podName,
        @namespace,
        [containerPort],
        WebSocketProtocol.V4BinaryWebsocketProtocol,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      demux = new StreamDemuxer(webSocket, StreamType.PortForward, ownsSocket: true);
      var stream = demux.GetStream((byte?)0, (byte?)0);
      var errors = demux.GetStream((byte?)1, null);
      demux.Start();
      _ = Task.Run(() => Drain(errors), cancellationToken);
      var socket = tcp.Client;
      using var copyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      await Task.WhenAny(
        Task.Run(() => CopySocketToStream(socket, stream, copyCts.Token), copyCts.Token),
        Task.Run(() => CopyStreamToSocket(stream, socket, copyCts.Token), copyCts.Token)).ConfigureAwait(false);
      copyCts.Cancel();
    }
    catch (OperationCanceledException) {
    }
    catch {
    }
    finally {
      tcp.Dispose();
      demux?.Dispose();
    }
  }

  private static void CopySocketToStream(Socket socket, Stream stream, CancellationToken cancellationToken) {
    var buffer = new byte[16 * 1024];
    try {
      while (!cancellationToken.IsCancellationRequested && socket.Connected) {
        var read = socket.Receive(buffer);
        if (read == 0)
          break;

        stream.Write(buffer, 0, read);
      }
    }
    catch (SocketException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
  }

  private static void CopyStreamToSocket(Stream stream, Socket socket, CancellationToken cancellationToken) {
    var buffer = new byte[16 * 1024];
    try {
      while (!cancellationToken.IsCancellationRequested && socket.Connected) {
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read == 0)
          break;

        var sent = 0;
        while (sent < read)
          sent += socket.Send(buffer, sent, read - sent, SocketFlags.None);
      }
    }
    catch (SocketException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
  }

  private static void Drain(Stream stream) {
    var buffer = new byte[256];
    try {
      while (stream.Read(buffer, 0, buffer.Length) > 0) {
      }
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
  }
}

public interface IClusterSessionFactory {
  Result<IClusterSession> Create(string contextName, string? kubeConfigPath = null);
}

public sealed class ClusterSessionFactory(IKubeConfigService kubeConfig) : IClusterSessionFactory {
  public Result<IClusterSession> Create(string contextName, string? kubeConfigPath = null) {
    var cfg = kubeConfig.Build(contextName, kubeConfigPath);
    if (!cfg.IsSuccess || cfg.Value is null)
      return new Result<IClusterSession>(null, false, cfg.Messages, cfg.StatusCode);

    return Result<IClusterSession>.Ok(new ClusterSession(contextName, cfg.Value));
  }
}
