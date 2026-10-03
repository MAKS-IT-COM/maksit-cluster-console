using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


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

  public string? LastResourceVersion { get; private set; }

  public IReadOnlyList<NavigatorItem> Navigator { get; private set; } = BuildNavigator(ResourceCatalog.BuiltIns);

  public async Task<Result> ConnectAsync(IClusterSession session, CancellationToken cancellationToken = default) {
    _session?.Dispose();
    ResetMetricsCache();
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
    ResetMetricsCache();
    Navigator = BuildNavigator(ResourceCatalog.BuiltIns);
  }

  public async Task<Result<IReadOnlyList<ResourceRow>>> ListAsync(
    string itemId,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken = default,
    string? labelSelector = null) {
    if (_session is null)
      return Result<IReadOnlyList<ResourceRow>>.ServiceUnavailable(null, "not connected");

    if (itemId == ResourceCatalog.ApplicationsId)
      return await ApplicationCatalog.ListAsync(_session!, GetClusterCpuAllocatableCachedAsync, @namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.HelmReleasesId)
      return await HelmCatalog.ListReleasesAsync(_session!, @namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.HelmChartsId)
      return await HelmCatalog.ListChartsAsync(_session!, @namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.DaprSidecarsId)
      return await DaprCatalog.ListSidecarsAsync(_session!, @namespace, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == ResourceCatalog.DaprControlPlaneId)
      return await DaprCatalog.ListControlPlaneAsync(_session!, filter, cancellationToken).ConfigureAwait(false);

    if (itemId == "customresourcedefinitions")
      return await ListDefinitionsAsync(filter, cancellationToken).ConfigureAwait(false);

    var descriptor = FindDescriptor(itemId);

    if (descriptor is null)
      return Result<IReadOnlyList<ResourceRow>>.NotFound(null, $"unknown resource {itemId}");

    var options = string.IsNullOrWhiteSpace(labelSelector)
      ? new ResourceListOptions()
      : new ResourceListOptions { LabelSelector = labelSelector.Trim() };
    var listed = await _session.ListAsync(descriptor.ToRef(), @namespace, cancellationToken, options).ConfigureAwait(false);
    LastResourceVersion = options.ResourceVersion;

    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    if (PodMetricsAggregate.WorkloadResourceIds.Contains(descriptor.Id))
      return await ListWorkloadsWithPodMetricsAsync(descriptor, listed.Value ?? [], @namespace, filter, cancellationToken)
        .ConfigureAwait(false);

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
      .Where(p => ResourceOwnership.Owns(p, owner.Document))
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

  public Task<Result<ClusterIssueSet>> GetClusterIssuesAsync(
    CancellationToken cancellationToken = default) =>
    ClusterIssueReader.LoadAsync(_session, cancellationToken);

  public Task<Result<DrainPreview>> PreviewDrainAsync(
    IReadOnlyList<string> nodeNames,
    CancellationToken cancellationToken = default) =>
    DrainPreviewReader.LoadAsync(_session, nodeNames, cancellationToken);

  public Task<Result<IReadOnlyList<HelmRevision>>> HelmHistoryAsync(
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default) =>
    HelmCatalog.HistoryAsync(_session, name, @namespace, cancellationToken);

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

  private static bool EventMatches(JsonObject ev, ResourceRow row) {
    var name = ev["involvedObject"]?["name"]?.GetValue<string>();

    if (string.IsNullOrEmpty(name))
      return false;

    if (name == row.Name)
      return true;

    return ApplicationManifest.WorkloadNames(row.Document).Contains(name, StringComparer.Ordinal);
  }

  public static bool Matches(ResourceRow row, string? filter) {
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
      Special(
        ResourceCatalog.PortForwardingId,
        "Port Forwarding",
        ResourceCatalog.Network,
        ResourceCatalog.PortForwardingDescriptor),
      Special(
        ResourceCatalog.HelmChartsId,
        "Charts",
        ResourceCatalog.Helm,
        ResourceCatalog.HelmChartsDescriptor),
      Special(
        ResourceCatalog.HelmReleasesId,
        "Releases",
        ResourceCatalog.Helm,
        ResourceCatalog.HelmReleasesDescriptor),
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
