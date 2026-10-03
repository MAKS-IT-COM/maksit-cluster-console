using System.Net;
using System.Text;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using k8s;
using k8s.Models;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Internal;
using MaksIT.ClusterConsole.Client.KubeConfig;


namespace MaksIT.ClusterConsole.Client.Cluster;

public sealed partial class ClusterSession : IClusterSession {
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
    CancellationToken cancellationToken = default,
    ResourceListOptions? options = null) {
    try {
      var selector = string.IsNullOrWhiteSpace(options?.LabelSelector) ? null : options.LabelSelector.Trim();
      var fieldSelector = string.IsNullOrWhiteSpace(options?.FieldSelector) ? null : options.FieldSelector.Trim();
      object raw;
      string? resourceVersion;

      if (IsCoreNamespaces(resource)) {
        raw = await ListNamespacesPagedAsync(cancellationToken).ConfigureAwait(false);
        resourceVersion = KubernetesResult.ResourceVersion(KubernetesResult.ToObject(raw));
      }
      else if (!resource.Namespaced || string.IsNullOrWhiteSpace(@namespace) || @namespace == "all") {
        (raw, resourceVersion) = await ListPagedAsync(
          cont => _client.CustomObjects.ListClusterCustomObjectAsync(
            resource.Group,
            resource.Version,
            resource.Plural,
            continueParameter: cont,
            fieldSelector: fieldSelector,
            labelSelector: selector,
            cancellationToken: cancellationToken),
          cancellationToken).ConfigureAwait(false);
      }
      else {
        (raw, resourceVersion) = await ListPagedAsync(
          cont => _client.CustomObjects.ListNamespacedCustomObjectAsync(
            resource.Group,
            resource.Version,
            @namespace,
            resource.Plural,
            continueParameter: cont,
            fieldSelector: fieldSelector,
            labelSelector: selector,
            cancellationToken: cancellationToken),
          cancellationToken).ConfigureAwait(false);
      }

      if (options is not null)
        options.ResourceVersion = resourceVersion;

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

      try {
        var patch = new V1Patch(body.ToJsonString(), V1Patch.PatchType.ApplyPatch);
        raw = namespaced
          ? await _client.CustomObjects.PatchNamespacedCustomObjectAsync(
            patch, group, version, ns!, plural, name,
            fieldManager: FieldManager,
            force: true,
            cancellationToken: cancellationToken).ConfigureAwait(false)
          : await _client.CustomObjects.PatchClusterCustomObjectAsync(
            patch, group, version, plural, name,
            fieldManager: FieldManager,
            force: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsApplyUnsupported(ex)) {
        raw = await CreateOrReplaceAsync(body, group, version, plural, name, namespaced, ns, cancellationToken)
          .ConfigureAwait(false);
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

  public Task<Result<PortForwardHandle>> PortForwardAsync(
    string podName,
    string @namespace,
    int containerPort,
    int localPort,
    int requestedPort = 0,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget = null,
    CancellationToken cancellationToken = default) =>
    PodPortForward.StartAsync(
      _client,
      podName,
      @namespace,
      containerPort,
      localPort,
      requestedPort,
      resolveTarget,
      cancellationToken);

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
    if (string.IsNullOrWhiteSpace(group))
      return Result<bool>.Ok(true);

    try {
      var versions = await _client.Apis.GetAPIVersionsAsync(cancellationToken).ConfigureAwait(false);
      var found = versions.Groups?.Any(item => string.Equals(item.Name, group, StringComparison.OrdinalIgnoreCase)) == true;

      return Result<bool>.Ok(found);
    }
    catch (Exception ex) {
      try {
        var list = await _client.ApiextensionsV1.ListCustomResourceDefinitionAsync(cancellationToken: cancellationToken)
          .ConfigureAwait(false);

        return Result<bool>.Ok(list.Items.Any(c => string.Equals(c.Spec.Group, group, StringComparison.OrdinalIgnoreCase)));
      }
      catch {
        return KubernetesResult.Map<bool>(ex);
      }
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

  public async Task<Result<double>> GetClusterCpuAllocatableAsync(CancellationToken cancellationToken = default) {
    try {
      var nodes = await _client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
      var cpu = 0d;

      foreach (var node in nodes.Items) {
        if (node.Status?.Allocatable?.TryGetValue("cpu", out var cpuQty) == true)
          cpu += KubeQuantity.ToCores(cpuQty.ToString());
      }

      return Result<double>.Ok(cpu);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<double>(ex);
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

  public async Task<Result<IReadOnlyList<JsonObject>>> ListHelmReleaseDocumentsAsync(
    string? @namespace,
    string? releaseName,
    CancellationToken cancellationToken = default) {
    try {
      var selector = string.IsNullOrWhiteSpace(releaseName)
        ? "owner=helm"
        : "owner=helm,name=" + releaseName.Trim();
      V1SecretList secrets;

      if (string.IsNullOrWhiteSpace(@namespace) || @namespace == "all")
        secrets = await _client.CoreV1.ListSecretForAllNamespacesAsync(
          labelSelector: selector,
          cancellationToken: cancellationToken).ConfigureAwait(false);
      else
        secrets = await _client.CoreV1.ListNamespacedSecretAsync(
          @namespace,
          labelSelector: selector,
          cancellationToken: cancellationToken).ConfigureAwait(false);

      var releases = secrets.Items
        .Select(DecodeHelmDocument)
        .OfType<JsonObject>()
        .ToList();

      return Result<IReadOnlyList<JsonObject>>.Ok(releases);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<IReadOnlyList<JsonObject>>(ex);
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

  public Task<Result<ExecBytesResult>> ExecBytesAsync(
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    byte[]? stdin = null,
    CancellationToken cancellationToken = default) =>
    PodExec.RunAsync(_client, podName, @namespace, container, command, stdin, cancellationToken);

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
    Result<IReadOnlyList<JsonObject>> pods;
    Result<IReadOnlyList<JsonObject>> budgets;

    try {
      pods = await ListAsync(new ResourceRef("", "v1", "pods", "Pod", true), "all", cancellationToken)
        .ConfigureAwait(false);

      if (!pods.IsSuccess)
        return pods.ToResult();

      budgets = await ListAsync(
        new ResourceRef("policy", "v1", "poddisruptionbudgets", "PodDisruptionBudget", true),
        "all",
        cancellationToken).ConfigureAwait(false);

      if (!budgets.IsSuccess)
        return budgets.ToResult();
    }
    catch (Exception ex) {
      return KubernetesResult.Map(ex);
    }

    var plan = DrainPlan.ForNode(nodeName, pods.Value ?? [], budgets.Value ?? []);
    var cordon = await CordonAsync(nodeName, true, cancellationToken).ConfigureAwait(false);

    if (!cordon.IsSuccess)
      return cordon;

    var failures = new List<string>();

    foreach (var pod in plan.Pods.Where(item => item.Action == DrainPlan.Evict)) {
      var eviction = new V1Eviction {
        Metadata = new V1ObjectMeta {
          Name = pod.Name,
          NamespaceProperty = pod.Namespace
        }
      };

      try {
        await _client.CoreV1.CreateNamespacedPodEvictionAsync(
          eviction,
          pod.Name,
          pod.Namespace,
          cancellationToken: cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) {
        failures.Add($"{pod.Namespace}/{pod.Name}: {ex.Message}");
      }
    }

    if (failures.Count > 0)
      return Result.Conflict("Cordoned " + nodeName + ". Not evicted: " + string.Join("; ", failures));

    return Result.Ok();
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
      var list = await KubernetesApiRetry.ExecuteAsync(
        ct => _client.CoreV1.ListNamespaceAsync(
          continueParameter: continueToken,
          cancellationToken: ct),
        cancellationToken).ConfigureAwait(false);

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
      var list = await KubernetesApiRetry.ExecuteAsync(
        ct => _client.CoreV1.ListPodForAllNamespacesAsync(
          continueParameter: continueToken,
          cancellationToken: ct),
        cancellationToken).ConfigureAwait(false);

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

  private static async Task<(object Body, string? ResourceVersion)> ListPagedAsync(
    Func<string?, Task<object>> page,
    CancellationToken cancellationToken) {
    var items = new JsonArray();
    string? continueToken = null;
    string? resourceVersion = null;

    do {
      var raw = await KubernetesApiRetry.ExecuteAsync(
        ct => page(continueToken),
        cancellationToken).ConfigureAwait(false);
      var root = KubernetesResult.ToObject(raw);
      resourceVersion ??= KubernetesResult.ResourceVersion(root);

      foreach (var item in KubernetesResult.Items(raw))
        items.Add(item.DeepClone());

      continueToken = KubernetesResult.ContinueToken(root);
    } while (!string.IsNullOrEmpty(continueToken) && !cancellationToken.IsCancellationRequested);

    return (new JsonObject { ["items"] = items }, resourceVersion);
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

    if (containers is null || containers.Count == 0)
      return ("-", "-");

    var cpu = 0d;
    long mem = 0;
    var hasCpu = false;
    var hasMem = false;

    foreach (var c in containers.OfType<JsonObject>()) {
      var usage = c["usage"] as JsonObject;

      if (usage?["cpu"] is not null) {
        cpu += KubeQuantity.ToCores(usage["cpu"]?.ToString());
        hasCpu = true;
      }

      if (usage?["memory"] is not null) {
        mem += KubeQuantity.ToBytes(usage["memory"]?.ToString());
        hasMem = true;
      }
    }

    return (
      hasCpu ? KubeQuantity.FormatCores(cpu) : "-",
      hasMem ? KubeQuantity.FormatMemoryQuantity(mem) : "-");
  }

  private static JsonObject? DecodeHelmDocument(V1Secret secret) {
    try {
      if (secret.Data is null || !secret.Data.TryGetValue("release", out var bytes))
        return HelmDocumentFromLabels(secret);

      var decoded = Convert.FromBase64String(Encoding.UTF8.GetString(bytes));
      using var gzip = new GZipStream(new MemoryStream(decoded), CompressionMode.Decompress);
      using var reader = new StreamReader(gzip);
      var json = reader.ReadToEnd();

      return JsonNode.Parse(json) as JsonObject ?? HelmDocumentFromLabels(secret);
    }
    catch {
      return HelmDocumentFromLabels(secret);
    }
  }

  private static JsonObject HelmDocumentFromLabels(V1Secret secret) {
    var labels = secret.Metadata.Labels;
    var info = new JsonObject {
      ["status"] = Label(labels, "status") ?? "unknown"
    };

    if (secret.Metadata.CreationTimestamp is DateTime created)
      info["last_deployed"] = new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)).ToString("o");

    var version = 0;
    _ = int.TryParse(Label(labels, "version"), out version);

    return new JsonObject {
      ["name"] = Label(labels, "name") ?? secret.Metadata.Name,
      ["namespace"] = secret.Metadata.NamespaceProperty ?? "",
      ["version"] = version,
      ["info"] = info,
      ["chart"] = new JsonObject {
        ["metadata"] = new JsonObject {
          ["name"] = Label(labels, "chart") ?? ""
        }
      }
    };
  }

  private static string? Label(IDictionary<string, string>? labels, string key) =>
    labels is not null && labels.TryGetValue(key, out var value) ? value : null;

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
