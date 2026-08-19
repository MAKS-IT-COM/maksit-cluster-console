using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public sealed class NavigatorItem {
  public required string Id { get; init; }

  public required string Title { get; init; }

  public required string Section { get; init; }

  public ResourceDescriptor? Descriptor { get; init; }

  public bool IsSpecial { get; init; }
}

public sealed partial class ClusterWorkspace {
  private IClusterSession? _session;

  public IClusterSession? Session => _session;

  public IReadOnlyList<NavigatorItem> Navigator { get; private set; } = BuildNavigator(ResourceCatalog.BuiltIns);

  public async Task<Result> ConnectAsync(IClusterSession session, CancellationToken cancellationToken = default) {
    _session?.Dispose();
    _session = session;
    var builtins = ResourceCatalog.BuiltIns.ToList();
    var crds = await session.ListCustomResourceDefinitionsAsync(cancellationToken).ConfigureAwait(false);
    if (crds.IsSuccess && crds.Value is not null)
      builtins.AddRange(crds.Value.Select(ResourceCatalog.FromCustomResourceDefinition).OfType<ResourceDescriptor>());

    Navigator = BuildNavigator(builtins);
    return Result.Ok();
  }

  public void Disconnect() {
    _session?.Dispose();
    _session = null;
    Navigator = BuildNavigator(ResourceCatalog.BuiltIns);
  }

  public async Task<Result<IReadOnlyList<ResourceRow>>> ListAsync(
    string itemId,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<IReadOnlyList<ResourceRow>>.ServiceUnavailable(null, "not connected");

    if (itemId == ResourceCatalog.ApplicationsId)
      return await ListApplicationsAsync(@namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.HelmReleasesId)
      return await ListHelmAsync(@namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.DaprSidecarsId)
      return await ListDaprSidecarsAsync(@namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.DaprControlPlaneId)
      return await ListDaprControlPlaneAsync(filter, cancellationToken).ConfigureAwait(false);

    if (itemId == "customresourcedefinitions")
      return await ListDefinitionsAsync(filter, cancellationToken).ConfigureAwait(false);

    var descriptor = FindDescriptor(itemId);
    if (descriptor is null)
      return Result<IReadOnlyList<ResourceRow>>.NotFound(null, $"unknown resource {itemId}");

    var listed = await _session.ListAsync(descriptor.ToRef(), @namespace, cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    IReadOnlyDictionary<string, ResourceMetrics>? metrics = null;
    if (descriptor.Id is "pods" or "nodes") {
      var metricsResult = descriptor.Id == "pods"
        ? await _session.GetPodMetricsAsync(@namespace, cancellationToken).ConfigureAwait(false)
        : await _session.GetNodeMetricsAsync(cancellationToken).ConfigureAwait(false);
      if (metricsResult.IsSuccess)
        metrics = metricsResult.Value;
    }

    var rows = (listed.Value ?? [])
      .Select(item => {
        ResourceMetrics? m = null;
        if (metrics is not null) {
          var key = descriptor.Id == "nodes"
            ? JsonPath.Name(item)
            : $"{JsonPath.Namespace(item)}/{JsonPath.Name(item)}";
          metrics.TryGetValue(key, out m);
        }

        return ResourceRow.From(item, descriptor, m);
      })
      .Where(row => Matches(row, filter))
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  public ResourceDescriptor? FindDescriptor(string itemId) {
    var nav = Navigator.FirstOrDefault(n => n.Id == itemId);
    return nav?.Descriptor ?? ResourceCatalog.Find(itemId);
  }

  public ResourceDescriptor? FindByGvk(string? apiVersion, string? kind) {
    var match = ResourceCatalog.FindByGvk(apiVersion, kind);
    if (match is not null)
      return match;

    if (string.IsNullOrWhiteSpace(kind))
      return null;

    return Navigator
      .Select(n => n.Descriptor)
      .FirstOrDefault(d => d is not null && d.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
  }

  public async Task<Result<JsonObject>> ApplyDocumentAsync(
    JsonObject document,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<JsonObject>.ServiceUnavailable(null, "not connected");

    var prepared = ResourceDocument.PrepareForApply(document);
    var kind = prepared["kind"]?.GetValue<string>();
    var apiVersion = prepared["apiVersion"]?.GetValue<string>();
    var resource = FindByGvk(apiVersion, kind)?.ToRef();
    return await _session.ApplyAsync(prepared, resource, cancellationToken).ConfigureAwait(false);
  }

  public async Task<Result<IReadOnlyList<ResourceRow>>> RelatedPodsAsync(
    ResourceRow owner,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<IReadOnlyList<ResourceRow>>.ServiceUnavailable(null, "not connected");

    var pods = ResourceCatalog.Find("pods")!;
    var listed = await _session.ListAsync(pods.ToRef(), owner.Namespace, cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var related = (listed.Value ?? [])
      .Where(p => Owns(p, owner.Document))
      .Select(p => ResourceRow.From(p, pods))
      .ToList();
    return Result<IReadOnlyList<ResourceRow>>.Ok(related);
  }

  public async Task<Result<IReadOnlyList<ResourceRow>>> EventsForAsync(
    ResourceRow row,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<IReadOnlyList<ResourceRow>>.ServiceUnavailable(null, "not connected");

    var events = ResourceCatalog.Find("events")!;
    var listed = await _session.ListAsync(events.ToRef(), row.Namespace ?? "all", cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var filtered = (listed.Value ?? [])
      .Where(e => EventMatches(e, row))
      .Select(e => ResourceRow.From(e, events))
      .ToList();
    return Result<IReadOnlyList<ResourceRow>>.Ok(filtered);
  }

  public async Task<Result<ClusterIssueSet>> GetClusterIssuesAsync(
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<ClusterIssueSet>.ServiceUnavailable(null, "not connected");

    var nodesTask = ListObjectsAsync("nodes", cancellationToken);
    var eventsTask = ListObjectsAsync("events", cancellationToken);
    var podsTask = ListObjectsAsync("pods", cancellationToken);
    await Task.WhenAll(nodesTask, eventsTask, podsTask).ConfigureAwait(false);

    var nodes = nodesTask.Result;
    var events = eventsTask.Result;
    var pods = podsTask.Result;
    if (!nodes.IsSuccess && !events.IsSuccess)
      return new Result<ClusterIssueSet>(
        null,
        false,
        nodes.Messages.Concat(events.Messages).ToList(),
        nodes.StatusCode);

    return Result<ClusterIssueSet>.Ok(ClusterIssues.Collect(
      nodes.Value ?? [],
      events.Value ?? [],
      pods.Value ?? []));
  }

  private async Task<Result<IReadOnlyList<JsonObject>>> ListObjectsAsync(
    string id,
    CancellationToken cancellationToken) {
    var descriptor = ResourceCatalog.Find(id);
    if (descriptor is null)
      return Result<IReadOnlyList<JsonObject>>.NotFound(null, $"unknown resource {id}");
    return await _session!.ListAsync(descriptor.ToRef(), Configuration.AllNamespaces, cancellationToken)
      .ConfigureAwait(false);
  }

  private async Task<Result<IReadOnlyList<ResourceRow>>> ListDefinitionsAsync(
    string? filter,
    CancellationToken cancellationToken) {
    var descriptor = ResourceCatalog.Find("customresourcedefinitions")!;
    var listed = await _session!.ListCustomResourceDefinitionsAsync(cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = (listed.Value ?? [])
      .Select(item => ResourceRow.From(item, descriptor))
      .Where(row => Matches(row, filter))
      .ToList();
    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private async Task<Result<IReadOnlyList<ResourceRow>>> ListApplicationsAsync(
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var kinds = new[] {
      ResourceCatalog.Find("deployments")!,
      ResourceCatalog.Find("statefulsets")!,
      ResourceCatalog.Find("daemonsets")!
    };
    var listed = await Task.WhenAll(kinds.Select(kind =>
      _session!.ListAsync(kind.ToRef(), @namespace, cancellationToken))).ConfigureAwait(false);

    for (var i = 0; i < listed.Length; i++) {
      if (!listed[i].IsSuccess)
        return new Result<IReadOnlyList<ResourceRow>>(null, false, listed[i].Messages, listed[i].StatusCode);
    }

    var members = listed
      .SelectMany((result, i) => (result.Value ?? []).Select(item => {
        EnsureApiIdentity(item, kinds[i]);
        return item;
      }))
      .Where(ApplicationManifest.HasManifest)
      .ToList();

    var rows = ApplicationManifest.Collapse(members)
      .Select(doc => new ResourceRow {
        Uid = JsonPath.Uid(doc),
        Name = JsonPath.Name(doc),
        Namespace = JsonPath.Namespace(doc),
        Document = doc,
        Cells = ApplicationManifest.Cells(doc)
      })
      .Where(row => Matches(row, filter))
      .OrderBy(row => row.Namespace, StringComparer.OrdinalIgnoreCase)
      .ThenBy(row => row.Cell("Instance"), StringComparer.OrdinalIgnoreCase)
      .ToList();
    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private static void EnsureApiIdentity(JsonObject item, ResourceDescriptor kind) {
    item["kind"] ??= kind.Kind;
    if (item["apiVersion"] is not null)
      return;

    item["apiVersion"] = string.IsNullOrEmpty(kind.Group)
      ? kind.Version
      : $"{kind.Group}/{kind.Version}";
  }

  private async Task<Result<IReadOnlyList<ResourceRow>>> ListHelmAsync(
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var listed = await _session!.ListHelmReleasesAsync(@namespace, cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var fake = new ResourceDescriptor(
      ResourceCatalog.HelmReleasesId,
      "Releases",
      ResourceCatalog.Helm,
      "",
      "v1",
      "secrets",
      "Secret",
      true,
      [new("Name", "name"), new("Namespace", "namespace"), new("Status", "status"), new("Chart", "chart"), new("App", "app")],
      new ResourceActions(CanDelete: false, CanApply: false),
      ["Overview"]);

    var rows = (listed.Value ?? []).Select(r => {
      var doc = new JsonObject {
        ["metadata"] = new JsonObject { ["name"] = r.Name, ["namespace"] = r.Namespace },
        ["status"] = r.Status,
        ["chart"] = r.Chart,
        ["appVersion"] = r.AppVersion
      };
      return new ResourceRow {
        Uid = $"{r.Namespace}/{r.Name}",
        Name = r.Name,
        Namespace = r.Namespace,
        Document = doc,
        Cells = new Dictionary<string, string> {
          ["Name"] = r.Name,
          ["Namespace"] = r.Namespace,
          ["Status"] = r.Status,
          ["Chart"] = r.Chart,
          ["App"] = r.AppVersion
        }
      };
    }).Where(r => Matches(r, filter)).ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private async Task<Result<IReadOnlyList<ResourceRow>>> ListDaprSidecarsAsync(
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var pods = ResourceCatalog.Find("pods")!;
    var listed = await _session!.ListAsync(pods.ToRef(), @namespace, cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = (listed.Value ?? [])
      .Where(IsDaprSidecar)
      .Select(p => ResourceRow.From(p, pods))
      .Where(r => Matches(r, filter))
      .ToList();
    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private async Task<Result<IReadOnlyList<ResourceRow>>> ListDaprControlPlaneAsync(
    string? filter,
    CancellationToken cancellationToken) {
    var pods = ResourceCatalog.Find("pods")!;
    var listed = await _session!.ListAsync(pods.ToRef(), "dapr", cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = (listed.Value ?? [])
      .Select(p => ResourceRow.From(p, pods))
      .Where(r => Matches(r, filter))
      .ToList();
    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private static bool IsDaprSidecar(JsonObject pod) {
    var annotations = pod["metadata"]?["annotations"] as JsonObject;
    var enabled = annotations?["dapr.io/enabled"]?.ToString();
    var appId = annotations?["dapr.io/app-id"]?.ToString();
    if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(appId))
      return true;

    var containers = pod["spec"]?["containers"] as JsonArray;
    return containers?.OfType<JsonObject>().Any(c => c["name"]?.ToString() == "daprd") == true;
  }

  private static bool EventMatches(JsonObject ev, ResourceRow row) {
    var name = ev["involvedObject"]?["name"]?.GetValue<string>();
    if (string.IsNullOrEmpty(name))
      return false;
    if (name == row.Name)
      return true;

    return ApplicationManifest.WorkloadNames(row.Document).Contains(name, StringComparer.Ordinal);
  }

  private static bool Owns(JsonObject pod, JsonObject owner) {
    var ownerName = JsonPath.Name(owner);
    var refs = pod["metadata"]?["ownerReferences"] as JsonArray;
    if (refs?.OfType<JsonObject>().Any(r => r["name"]?.ToString() == ownerName) == true)
      return true;

    var matchLabels = owner["spec"]?["selector"]?["matchLabels"] as JsonObject;
    if (LabelsMatch(pod["metadata"]?["labels"] as JsonObject, matchLabels))
      return true;

    var labels = pod["metadata"]?["labels"] as JsonObject;
    return labels?["app"]?.ToString() == ownerName
      || labels?[ApplicationManifest.NameKey]?.ToString() == ownerName
      || ApplicationManifest.SameInstance(pod, owner);
  }

  private static bool LabelsMatch(JsonObject? podLabels, JsonObject? required) {
    if (podLabels is null || required is null || required.Count == 0)
      return false;

    foreach (var pair in required) {
      if (podLabels[pair.Key]?.ToString() != pair.Value?.ToString())
        return false;
    }

    return true;
  }

  private static bool Matches(ResourceRow row, string? filter) {
    if (string.IsNullOrWhiteSpace(filter))
      return true;

    var hay = string.Join(' ', row.Cells.Values) + " " + row.Name + " " + row.Namespace;
    return hay.Contains(filter, StringComparison.OrdinalIgnoreCase);
  }

  private static List<NavigatorItem> BuildNavigator(IEnumerable<ResourceDescriptor> descriptors) {
    var items = new List<NavigatorItem> {
      Special(ResourceCatalog.OverviewId, "Overview", ResourceCatalog.Cluster),
      Special(ResourceCatalog.WorkloadsOverviewId, "Overview", ResourceCatalog.Workloads),
      Special(
        ResourceCatalog.ApplicationsId,
        "Applications",
        ResourceCatalog.Applications,
        ResourceCatalog.ApplicationsDescriptor),
      Special(ResourceCatalog.PortForwardingId, "Port Forwarding", ResourceCatalog.Network),
      Special(ResourceCatalog.HelmChartsId, "Charts", ResourceCatalog.Helm),
      Special(ResourceCatalog.HelmReleasesId, "Releases", ResourceCatalog.Helm),
      Special(ResourceCatalog.DaprSidecarsId, "Sidecars", ResourceCatalog.Dapr),
      Special(ResourceCatalog.DaprControlPlaneId, "Control plane", ResourceCatalog.Dapr)
    };

    items.AddRange(descriptors.Select(d => new NavigatorItem {
      Id = d.Id,
      Title = d.Title,
      Section = d.Section,
      Descriptor = d
    }));

    return items;
  }

  private static NavigatorItem Special(
    string id,
    string title,
    string section,
    ResourceDescriptor? descriptor = null) =>
    new() {
      Id = id,
      Title = title,
      Section = section,
      IsSpecial = true,
      Descriptor = descriptor
    };
}
