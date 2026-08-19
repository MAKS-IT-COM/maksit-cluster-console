using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class ClusterPageViewModel : ObservableObject, IDisposable {
  private readonly ClusterWorkspace _workspace;
  private readonly ConfigurationFileService _configuration;
  private readonly ClusterChatService _chat;
  private readonly Action<string> _setStatus;
  private CancellationTokenSource? _logsCts;
  private CancellationTokenSource? _refreshCts;
  private bool _hasDaprCrd;
  private bool _syncingNamespace;
  private bool _syncingLayout;
  private readonly List<ResourceRow> _listedRows = [];
  private readonly Dictionary<string, ColumnFilterViewModel> _columnFilters = new(StringComparer.Ordinal);
  private JsonObject? _editDocument;
  private string? _detailsUid;
  private bool _loadingDetails;
  private bool _updatingPodContext;
  private readonly List<double> _cpuHistory = [];
  private readonly List<double> _memoryHistory = [];
  private const int HistoryPoints = 60;

  public ClusterPageViewModel(
    KubeContextInfo context,
    ClusterWorkspace workspace,
    ConfigurationFileService configuration,
    IOllamaChatClient ollama,
    Action<string> setStatus) {
    Context = context;
    _workspace = workspace;
    _configuration = configuration;
    _chat = new ClusterChatService(ollama, workspace);
    _setStatus = setStatus;
    selectedNamespace = NormalizeNamespace(_configuration.Current.NamespaceFor(context.Name));
    overviewPerNode = _configuration.Current.OverviewPerNode;
    chatStatus = $"Local Ollama · {_configuration.Current.OllamaModel}. Pull: ollama pull {_configuration.Current.OllamaModel}";
  }

  public KubeContextInfo Context { get; }

  public string Name => Context.Name;

  public ClusterWorkspace Workspace => _workspace;

  public ObservableCollection<NavGroupViewModel> Navigator { get; } = [];

  public ObservableCollection<ResourceRow> Rows { get; } = [];

  public ObservableCollection<PortForwardItemViewModel> PortForwards { get; } = [];

  public ObservableCollection<ResourceRow> RelatedPods { get; } = [];

  public ObservableCollection<PodContainer> Containers { get; } = [];

  public ObservableCollection<DataEntryViewModel> DataEntries { get; } = [];

  public ObservableCollection<WorkloadKindCount> WorkloadCounts { get; } = [];

  public ObservableCollection<NodeUsageViewModel> NodeUsages { get; } = [];

  public ObservableCollection<ClusterIssue> OverviewWarnings { get; } = [];

  public ObservableCollection<ClusterIssue> OverviewErrors { get; } = [];

  public ObservableCollection<LimitRowViewModel> LimitRows { get; } = [];

  [ObservableProperty]
  private NavigatorItem? selectedNavItem;

  [ObservableProperty]
  private ResourceRow? selectedRow;

  [ObservableProperty]
  private LimitRowViewModel? selectedLimitRow;

  [ObservableProperty]
  private ResourceRow? selectedRelatedPod;

  [ObservableProperty]
  private PodContainer? selectedContainer;

  [ObservableProperty]
  private string selectedNamespace = Configuration.AllNamespaces;

  [ObservableProperty]
  private string filter = string.Empty;

  [ObservableProperty]
  private string overviewText = string.Empty;

  [ObservableProperty]
  private string yamlText = string.Empty;

  [ObservableProperty]
  private string eventsText = string.Empty;

  [ObservableProperty]
  private string logsText = string.Empty;

  [ObservableProperty]
  private string terminalText = string.Empty;

  [ObservableProperty]
  private string terminalCommand = "/bin/sh";

  [ObservableProperty]
  private string selectedTab = "Overview";

  [ObservableProperty]
  private int scaleReplicas = 1;

  [ObservableProperty]
  private int forwardLocalPort = 8080;

  [ObservableProperty]
  private int forwardContainerPort = 80;

  [ObservableProperty]
  private bool followLogs;

  [ObservableProperty]
  private bool isDirty;

  [ObservableProperty]
  private ResourceSlice cpuSlice = ResourceSlice.Empty("cpu");

  [ObservableProperty]
  private ResourceSlice memorySlice = ResourceSlice.Empty("memory");

  [ObservableProperty]
  private ResourceSlice podSlice = ResourceSlice.Empty("pods");

  [ObservableProperty]
  private string cpuCaption = "—";

  [ObservableProperty]
  private string memoryCaption = "—";

  [ObservableProperty]
  private string podCaption = "—";

  [ObservableProperty]
  private double cpuPercent;

  [ObservableProperty]
  private double memoryPercent;

  [ObservableProperty]
  private double podPercent;

  [ObservableProperty]
  private string metricsHint = "Usage charts use metrics-server (metrics.k8s.io), same as kubectl top.";

  [ObservableProperty]
  private IReadOnlyList<double> cpuHistory = [];

  [ObservableProperty]
  private IReadOnlyList<double> memoryHistory = [];

  [ObservableProperty]
  private bool overviewPerNode;

  partial void OnOverviewPerNodeChanged(bool value) {
    var cfg = _configuration.Current;
    if (cfg.OverviewPerNode == value)
      return;
    cfg.OverviewPerNode = value;
    _configuration.Save(cfg);
  }

  [RelayCommand]
  private void ShowClusterOverview() => OverviewPerNode = false;

  [RelayCommand]
  private void ShowNodesOverview() => OverviewPerNode = true;

  public bool CanScale =>
    SelectedResourceRef()?.Actions.CanScale == true;

  public bool CanRestart =>
    SelectedResourceRef()?.Actions.CanRestart == true;

  public bool CanDelete =>
    SelectedResourceRef()?.Actions.CanDelete == true && SelectedRow is not null;

  public bool CanForceDelete =>
    CanDelete && SelectedDescriptor?.Kind != "Namespace";

  public bool CanDeleteNamespace {
    get {
      var name = TargetNamespaceName;
      return SelectedDescriptor?.Kind == "Namespace"
        && !string.IsNullOrEmpty(name)
        && !IsProtectedNamespace(name);
    }
  }

  public bool CanBrowseFiles =>
    SelectedRow is not null
    && SelectedDescriptor?.Id is "persistentvolumes" or "persistentvolumeclaims";

  public event Action<VolumeFilesViewModel>? VolumeFilesRequested;

  public bool CanLogs => HasDetailTab("Logs");

  public bool CanExec => HasDetailTab("Terminal");

  public bool CanPortForward => SelectedDescriptor?.Actions.CanPortForward == true || TargetPodName is not null;

  public bool ShowEventsTab => HasDetailTab("Events");

  public bool ShowPodsTab => HasDetailTab("Pods");

  public bool ShowLogsTab => HasDetailTab("Logs");

  public bool ShowTerminalTab => HasDetailTab("Terminal");

  public bool ShowPodPicker => ShowPodsTab;

  public bool ShowContainerPicker => ShowLogsTab || ShowTerminalTab || IsPodSelection;

  public bool HasContainers => Containers.Count > 0;

  public bool HasSelectedRow => SelectedRow is not null;

  public bool CanCordon => SelectedDescriptor?.Actions.CanCordon == true;

  public bool CanDrain => SelectedDescriptor?.Actions.CanDrain == true;

  public bool CanTrigger => SelectedDescriptor?.Actions.CanTrigger == true;

  public bool CanApply =>
    (SelectedResourceRef() ?? SelectedDescriptor)?.Actions.CanApply != false;

  public string DetailsTitle {
    get {
      if (SelectedRow is null)
        return "Select a resource";

      var kind = SelectedDocumentKind
        ?? SelectedResourceRef()?.Kind
        ?? SelectedDescriptor?.Kind;
      return kind is { Length: > 0 }
        ? $"{kind}  ·  {SelectedRow.Name}"
        : SelectedRow.Name;
    }
  }

  public bool IsDataEditor =>
    SelectedDescriptor?.Id is "secrets" or "configmaps";

  public bool IsClusterDashboard =>
    SelectedNavItem?.Id == ResourceCatalog.OverviewId;

  public bool IsWorkloadsDashboard =>
    SelectedNavItem?.Id == ResourceCatalog.WorkloadsOverviewId;

  public bool IsResourceTable =>
    SelectedNavItem is not null && !IsClusterDashboard && !IsWorkloadsDashboard;

  public string OverviewWarningsCaption =>
    ClusterIssues.Caption("Warnings", OverviewWarnings);

  public string OverviewErrorsCaption =>
    ClusterIssues.Caption("Errors", OverviewErrors);

  public bool HasOverviewWarnings => OverviewWarnings.Count > 0;

  public bool HasOverviewErrors => OverviewErrors.Count > 0;

  public bool HasContainerLimits => LimitRows.Count > 0;

  public bool HasLimitOvercommit =>
    CpuSlice.LimitsExceedCapacity || MemorySlice.LimitsExceedCapacity;

  public string LimitsCaption =>
    HasLimitOvercommit
      ? $"Resource limits ({LimitRows.Count}) — specified limits are higher than node capacity"
      : $"Resource limits ({LimitRows.Count})";

  public ResourceDescriptor? SelectedDescriptor =>
    SelectedNavItem?.Descriptor ?? _workspace.FindDescriptor(SelectedNavItem?.Id ?? "");

  private string? SelectedDocumentKind =>
    SelectedRow?.Document["kind"]?.GetValue<string>();

  private string? SelectedDocumentApiVersion =>
    SelectedRow?.Document["apiVersion"]?.GetValue<string>();

  private ResourceDescriptor? SelectedResourceRef() {
    var match = _workspace.FindByGvk(SelectedDocumentApiVersion, SelectedDocumentKind);
    if (match is not null && match.Id != ResourceCatalog.ApplicationsId)
      return match;

    if (SelectedDescriptor is { } descriptor && descriptor.Id != ResourceCatalog.ApplicationsId)
      return descriptor;

    return null;
  }

  public async Task<MaksIT.Results.Result> StartAsync(IClusterSession session) {
    var connected = await _workspace.ConnectAsync(session);
    if (!connected.IsSuccess)
      return connected;

    var dapr = await session.HasApiGroupAsync("dapr.io");
    _hasDaprCrd = dapr.IsSuccess && dapr.Value;
    RebuildNavigator();
    _syncingLayout = true;
    try {
      var items = Navigator.SelectMany(g => g.AllItems()).Select(i => i.Item).ToList();
      var savedId = _configuration.Current.Layout.SelectedNavId;
      var nav = items.FirstOrDefault(i => i.Id == savedId)
        ?? items.FirstOrDefault(i => i.Id == "pods")
        ?? items.FirstOrDefault();
      Filter = _configuration.Current.Layout.SearchFor(nav?.Id);
      SelectedNavItem = nav;
    }
    finally {
      _syncingLayout = false;
    }
    _setStatus($"Connected to {Name}");
    ResumePolling();
    return MaksIT.Results.Result.Ok();
  }

  public void PausePolling() =>
    _refreshCts?.Cancel();

  public void ResumePolling() =>
    StartRefreshLoop();

  public void Dispose() {
    PausePolling();
    _logsCts?.Cancel();
    _chatCts?.Cancel();
    foreach (var pf in PortForwards.ToList())
      pf.Handle.Dispose();
    PortForwards.Clear();
    _workspace.Disconnect();
  }

  public ColumnFilterViewModel FilterFor(string header) {
    if (!_columnFilters.TryGetValue(header, out var filter)) {
      filter = new ColumnFilterViewModel(header, () => OnColumnFilterChanged(header));
      var saved = _configuration.Current.Layout.FilterFor(TableKey(), header);
      if (saved is not null)
        filter.Restore(saved);
      _columnFilters[header] = filter;
    }

    return filter;
  }

  private void OnColumnFilterChanged(string header) {
    ApplyColumnFilters();
    if (header == "Namespace")
      SyncNamespaceFromColumnFilter();
    PersistColumnFilters();
  }

  private void ApplyColumnFilters() =>
    ApplyColumnFilters(SelectedRow?.Uid);

  private void ApplyColumnFilters(string? keepUid) {
    var filters = _columnFilters.Values.Select(filter => filter.Model);
    var desired = new List<ResourceRow>();
    foreach (var row in _listedRows) {
      if (ResourceColumnFilter.MatchesAll(row, filters))
        desired.Add(row);
    }

    CollectionSync.MergeByKey(Rows, desired, row => row.Uid, static (current, incoming) => current.CopyFrom(incoming));

    if (SelectedRow is null || !Rows.Contains(SelectedRow))
      SelectedRow = keepUid is null ? null : Rows.FirstOrDefault(row => row.Uid == keepUid);

    var title = SelectedNavItem?.Title ?? "items";
    var filtered = _columnFilters.Values.Any(filter => filter.IsActive) && Rows.Count != _listedRows.Count;
    _setStatus(filtered
      ? $"{Rows.Count} of {_listedRows.Count} {title} · {Name}"
      : $"{Rows.Count} {title} · {Name}");
  }

  partial void OnSelectedNavItemChanged(NavigatorItem? value) {
    _columnFilters.Clear();
    foreach (var item in Navigator.SelectMany(g => g.AllItems()))
      item.IsSelected = item.Item.Id == value?.Id;

    OnPropertyChanged(nameof(IsDataEditor));
    NotifyActionFlags();
    OnPropertyChanged(nameof(IsClusterDashboard));
    OnPropertyChanged(nameof(IsWorkloadsDashboard));
    OnPropertyChanged(nameof(IsResourceTable));
    NotifyDetailsUi();
    _listedRows.Clear();
    Rows.Clear();
    SelectedRow = null;

    if (!_syncingLayout) {
      var cfg = _configuration.Current;
      cfg.Layout.SelectedNavId = value?.Id;
      _configuration.Save(cfg);
      var savedSearch = cfg.Layout.SearchFor(value?.Id);
      if (Filter != savedSearch) {
        _syncingLayout = true;
        Filter = savedSearch;
        _syncingLayout = false;
      }
    }

    _ = RefreshRowsAsync();
  }

  partial void OnSelectedNamespaceChanged(string value) {
    if (_syncingNamespace)
      return;
    PersistSelectedNamespace(NormalizeNamespace(value));
  }

  partial void OnFilterChanged(string value) {
    if (!_syncingLayout) {
      var cfg = _configuration.Current;
      cfg.Layout.SetSearch(SelectedNavItem?.Id, value);
      _configuration.Save(cfg);
    }

    _ = RefreshRowsAsync();
  }

  partial void OnSelectedRowChanged(ResourceRow? value) {
    NotifyActionFlags();
    NotifyDetailsUi();
    if (value?.Uid == _detailsUid)
      return;
    _ = LoadDetailsAsync();
  }

  partial void OnSelectedRelatedPodChanged(ResourceRow? value) {
    if (_updatingPodContext)
      return;

    ApplyContainers(value?.Document);
    if (SelectedRow is not null)
      OverviewText = SelectedRow.FormatOverview(Containers);
    NotifyDetailsUi();
    _ = LoadLogsAsync();
  }

  partial void OnSelectedContainerChanged(PodContainer? value) {
    if (_updatingPodContext)
      return;

    _ = LoadLogsAsync();
  }

  partial void OnYamlTextChanged(string value) {
    if (!_loadingDetails)
      IsDirty = true;
  }

  partial void OnFollowLogsChanged(bool value) =>
    _ = LoadLogsAsync();

  [RelayCommand]
  private async Task RefreshRowsAsync() {
    if (SelectedNavItem is null)
      return;

    await SampleClusterUsageAsync();

    if (SelectedNavItem.Id == ResourceCatalog.OverviewId) {
      _listedRows.Clear();
      Rows.Clear();
      await LoadOverviewIssuesAsync();
      _setStatus($"Overview · {Name}");
      return;
    }

    if (SelectedNavItem.Id == ResourceCatalog.WorkloadsOverviewId) {
      await LoadWorkloadsOverviewAsync();
      return;
    }

    if (SelectedNavItem.Id == ResourceCatalog.HelmChartsId) {
      _listedRows.Clear();
      Rows.Clear();
      OverviewText = "Add chart repositories with the helm CLI. Releases are listed under Helm → Releases.";
      _setStatus("Helm charts are managed via helm repos on this machine.");
      return;
    }

    if (SelectedNavItem.Id == ResourceCatalog.PortForwardingId) {
      _listedRows.Clear();
      Rows.Clear();
      _setStatus($"{PortForwards.Count} active port-forward(s).");
      return;
    }

    var listed = await _workspace.ListAsync(SelectedNavItem.Id, Configuration.AllNamespaces, Filter);
    var keepUid = SelectedRow?.Uid;
    _listedRows.Clear();
    if (!listed.IsSuccess) {
      Rows.Clear();
      _setStatus(string.Join("; ", listed.Messages));
      return;
    }

    _listedRows.AddRange(listed.Value ?? []);
    foreach (var column in SelectedDescriptor?.Columns ?? [])
      FilterFor(column.Header).LoadValues(_listedRows);
    SeedNamespaceColumnFromSelection();

    ApplyColumnFilters(keepUid);
    NotifyActionFlags();
    NotifyDetailsUi();
    OnPropertyChanged(nameof(IsDataEditor));
    OnPropertyChanged(nameof(IsClusterDashboard));
    OnPropertyChanged(nameof(IsWorkloadsDashboard));
    OnPropertyChanged(nameof(IsResourceTable));
  }

  [RelayCommand]
  private async Task ApplyYamlAsync() {
    if (_workspace.Session is null)
      return;

    var doc = YamlFormatter.ToJsonObject(YamlText);
    if (doc is null) {
      _setStatus("YAML is empty or invalid.");
      return;
    }

    var applied = await _workspace.ApplyDocumentAsync(doc);
    _setStatus(applied.IsSuccess ? "Applied YAML." : string.Join("; ", applied.Messages));
    if (!applied.IsSuccess)
      return;

    IsDirty = false;
    _detailsUid = null;
    await RefreshRowsAsync();
    await LoadDetailsAsync();
  }

  [RelayCommand]
  private async Task ApplyDataAsync() {
    if (_workspace.Session is null)
      return;

    var doc = _editDocument is not null
      ? ResourceDocument.Clone(_editDocument)
      : YamlFormatter.ToJsonObject(YamlText);
    if (doc is null) {
      _setStatus("Nothing to apply.");
      return;
    }

    ResourceDocument.WriteDataEntries(doc, DataEntries.Select(e => new ResourceDataEntry(e.Key, e.Value, e.IsBinary)));
    var applied = await _workspace.ApplyDocumentAsync(doc);
    _setStatus(applied.IsSuccess ? "Applied data." : string.Join("; ", applied.Messages));
    if (!applied.IsSuccess)
      return;

    IsDirty = false;
    _detailsUid = null;
    await RefreshRowsAsync();
    await LoadDetailsAsync();
  }

  [RelayCommand]
  private void AddDataEntry() {
    DataEntries.Add(new DataEntryViewModel { Key = "new-key", Value = "" });
    IsDirty = true;
  }

  [RelayCommand]
  private void NewResource() {
    if (SelectedDescriptor is null || SelectedDescriptor.Actions.CanApply == false)
      return;

    _detailsUid = null;
    SelectedRow = null;
    _editDocument = YamlFormatter.ToJsonObject(ResourceDocument.NewTemplate(SelectedDescriptor, SelectedNamespace));
    _loadingDetails = true;
    YamlText = _editDocument is null ? "" : YamlFormatter.FromJson(_editDocument);
    ReplaceDataEntries(_editDocument);
    _loadingDetails = false;
    IsDirty = true;
    _setStatus($"New {SelectedDescriptor.Kind} — edit YAML or Data, then Apply.");
  }

  [RelayCommand]
  private async Task ReloadYamlAsync() {
    _detailsUid = null;
    await LoadDetailsAsync();
  }

  [RelayCommand]
  private async Task DeleteAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var resource = SelectedResourceRef();
    if (resource is null || resource.Actions.CanDelete == false)
      return;

    var deleted = await _workspace.Session.DeleteAsync(resource.ToRef(), SelectedRow.Name, SelectedRow.Namespace);
    _setStatus(deleted.IsSuccess
      ? ReplicaSetWarning(SelectedRow)
      : string.Join("; ", deleted.Messages));
    await RefreshRowsAsync();
  }

  [RelayCommand]
  private async Task ForceDeleteAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var resource = SelectedResourceRef();
    if (resource is null || resource.Actions.CanDelete == false)
      return;

    var deleted = await _workspace.Session.DeleteAsync(
      resource.ToRef(),
      SelectedRow.Name,
      SelectedRow.Namespace,
      force: true);
    _setStatus(deleted.IsSuccess
      ? "Force-deleted. " + ReplicaSetWarning(SelectedRow)
      : string.Join("; ", deleted.Messages));
    await RefreshRowsAsync();
  }

  [RelayCommand]
  private async Task DeleteNamespaceAsync() {
    if (_workspace.Session is null)
      return;

    var name = TargetNamespaceName;
    if (string.IsNullOrEmpty(name) || IsProtectedNamespace(name))
      return;

    var deleted = await _workspace.Session.ForceDeleteNamespaceAsync(name);
    _setStatus(deleted.IsSuccess
      ? $"Force-deleted namespace {name}."
      : string.Join("; ", deleted.Messages));
    if (string.Equals(SelectedNamespace, name, StringComparison.Ordinal)) {
      SelectedNamespace = Configuration.AllNamespaces;
      _columnFilters.Remove("Namespace");
    }

    await RefreshRowsAsync();
  }

  private string? TargetNamespaceName =>
    SelectedDescriptor?.Kind == "Namespace" ? SelectedRow?.Name : null;

  private static bool IsProtectedNamespace(string name) =>
    name.Equals("default", StringComparison.OrdinalIgnoreCase)
    || name.Equals("kube-system", StringComparison.OrdinalIgnoreCase)
    || name.Equals("kube-public", StringComparison.OrdinalIgnoreCase)
    || name.Equals("kube-node-lease", StringComparison.OrdinalIgnoreCase);

  private static string ReplicaSetWarning(ResourceRow row) {
    var owners = row.Document["metadata"]?["ownerReferences"] as JsonArray;
    var owned = owners?.OfType<JsonObject>().Any(o => {
      var kind = o["kind"]?.GetValue<string>();
      return kind is "ReplicaSet" or "Deployment" or "StatefulSet" or "DaemonSet" or "Job";
    }) == true;
    return owned
      ? "Deleted. A controller may recreate this pod — open Namespaces and force-delete the sandbox namespace."
      : "Deleted.";
  }

  [RelayCommand]
  private async Task ScaleAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var resource = SelectedResourceRef();
    if (resource is null || resource.Actions.CanScale == false)
      return;

    var scaled = await _workspace.Session.ScaleAsync(resource.ToRef(), SelectedRow.Name, SelectedRow.Namespace, ScaleReplicas);
    _setStatus(scaled.IsSuccess ? $"Scaled to {ScaleReplicas}." : string.Join("; ", scaled.Messages));
    await RefreshRowsAsync();
  }

  [RelayCommand]
  private async Task RestartAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var resource = SelectedResourceRef();
    if (resource is null || resource.Actions.CanRestart == false)
      return;

    var restarted = await _workspace.Session.RestartAsync(resource.ToRef(), SelectedRow.Name, SelectedRow.Namespace);
    _setStatus(restarted.IsSuccess ? "Restarted." : string.Join("; ", restarted.Messages));
  }

  [RelayCommand]
  private async Task CordonAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var result = await _workspace.Session.CordonAsync(SelectedRow.Name, true);
    _setStatus(result.IsSuccess ? "Cordoned." : string.Join("; ", result.Messages));
  }

  [RelayCommand]
  private async Task UncordonAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var result = await _workspace.Session.CordonAsync(SelectedRow.Name, false);
    _setStatus(result.IsSuccess ? "Uncordoned." : string.Join("; ", result.Messages));
  }

  [RelayCommand]
  private async Task DrainAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var result = await _workspace.Session.DrainAsync(SelectedRow.Name);
    _setStatus(result.IsSuccess ? "Drain requested." : string.Join("; ", result.Messages));
  }

  [RelayCommand]
  private async Task TriggerCronAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var result = await _workspace.Session.TriggerCronJobAsync(SelectedRow.Name, SelectedRow.Namespace ?? "default");
    _setStatus(result.IsSuccess ? "Job created." : string.Join("; ", result.Messages));
  }

  [RelayCommand]
  private async Task StartPortForwardAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var pod = TargetPodName;
    var ns = TargetPodNamespace ?? "default";
    if (pod is null) {
      _setStatus(ShowPodsTab
        ? "Select a pod in the details pane, then port-forward."
        : "Port-forward requires a pod.");
      return;
    }

    var started = await _workspace.Session.PortForwardAsync(pod, ns, ForwardContainerPort, ForwardLocalPort);
    if (!started.IsSuccess || started.Value is null) {
      _setStatus(string.Join("; ", started.Messages));
      return;
    }

    PortForwards.Add(new PortForwardItemViewModel { Handle = started.Value, Cluster = Name });
    _setStatus($"Forwarding localhost:{ForwardLocalPort} ({Name})");
  }

  [RelayCommand]
  private void StopPortForward(PortForwardItemViewModel? item) {
    if (item is null)
      return;

    item.Handle.Dispose();
    PortForwards.Remove(item);
  }

  [RelayCommand]
  private async Task ExecAsync() {
    if (_workspace.Session is null || SelectedRow is null)
      return;

    var pod = TargetPodName;
    var ns = TargetPodNamespace ?? "default";
    if (pod is null) {
      TerminalText = ShowPodsTab
        ? "Select a pod in the details pane, then exec."
        : "Exec requires a pod.";
      SelectedTab = "Terminal";
      return;
    }

    if (Containers.Count > 1 && SelectedContainer is null) {
      TerminalText = "Select a container in the details pane, then exec.";
      SelectedTab = "Terminal";
      return;
    }

    var parts = TerminalCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var result = await _workspace.Session.ExecAsync(pod, ns, SelectedContainer?.Name, parts);
    TerminalText = result.IsSuccess ? result.Value ?? "" : string.Join("; ", result.Messages);
    SelectedTab = "Terminal";
  }

  [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
  private void BrowseFiles() {
    if (SelectedRow is null)
      return;

    VolumeFilesRequested?.Invoke(new VolumeFilesViewModel(_workspace, SelectedRow, SelectedDescriptor?.Kind));
  }

  private void SeedNamespaceColumnFromSelection() {
    if (!_columnFilters.TryGetValue("Namespace", out var filter))
      return;
    var ns = NormalizeNamespace(SelectedNamespace);
    if (ns == Configuration.AllNamespaces)
      return;
    if (!filter.Values.Any(value => string.Equals(value.Value, ns, StringComparison.Ordinal)))
      return;
    filter.IncludeOnly(ns);
  }

  private void SyncNamespaceFromColumnFilter() {
    if (!_columnFilters.TryGetValue("Namespace", out var filter))
      return;
    var distinct = filter.Values.Select(value => value.Value).ToList();
    var scope = ResourceColumnFilter.Scope(filter.Model, distinct);
    if (string.Equals(SelectedNamespace, scope, StringComparison.Ordinal))
      return;
    _syncingNamespace = true;
    SelectedNamespace = scope;
    _syncingNamespace = false;
    PersistSelectedNamespace(scope);
  }

  private void PersistSelectedNamespace(string ns) {
    var cfg = _configuration.Current;
    if (string.Equals(cfg.NamespaceFor(Name), ns, StringComparison.Ordinal)
        && string.Equals(cfg.ActiveContext, Name, StringComparison.Ordinal))
      return;

    cfg.SetNamespace(Name, ns);
    _configuration.Save(cfg);
  }

  private string TableKey() =>
    LayoutSettings.ResourceTable(SelectedNavItem?.Id);

  private void PersistColumnFilters() {
    if (_syncingLayout)
      return;

    var map = new Dictionary<string, SavedColumnFilter>(StringComparer.Ordinal);
    foreach (var (header, filter) in _columnFilters)
      map[header] = filter.Snapshot();

    var cfg = _configuration.Current;
    cfg.Layout.SetFilters(TableKey(), map);
    _configuration.Save(cfg);
  }

  private static string NormalizeNamespace(string? value) =>
    string.IsNullOrWhiteSpace(value) ? Configuration.AllNamespaces : value;

  private void RebuildNavigator() {
    Navigator.Clear();
    foreach (var section in ResourceCatalog.Sections) {
      if (section == ResourceCatalog.Dapr && !_hasDaprCrd)
        continue;

      var sectionItems = _workspace.Navigator.Where(i => i.Section == section).ToList();
      if (section == ResourceCatalog.CustomResources) {
        var definitions = sectionItems
          .Where(i => i.Descriptor?.Kind == "CustomResourceDefinition")
          .Select(ToNavItem)
          .ToList();
        var groups = ResourceCatalog.GroupCustomResources(sectionItems.Select(i => i.Descriptor).OfType<ResourceDescriptor>())
          .Select(g => CreateNavGroup(
            g.Group,
            $"{section}/{g.Group}",
            g.Kinds.Select(d => sectionItems.First(i => i.Id == d.Id)).Select(ToNavItem).ToList(),
            [],
            topLevel: false))
          .ToList();
        if (definitions.Count == 0 && groups.Count == 0)
          continue;

        Navigator.Add(CreateNavGroup(section, section, definitions, groups));
        continue;
      }

      var items = sectionItems
        .OrderBy(i => i.IsSpecial ? 0 : 1)
        .ThenBy(i => i.Title)
        .Select(ToNavItem)
        .ToList();

      if (items.Count == 0)
        continue;

      Navigator.Add(CreateNavGroup(section, section, items, []));
    }
  }

  private NavGroupViewModel CreateNavGroup(
    string section,
    string path,
    IReadOnlyList<NavItemViewModel> items,
    IReadOnlyList<NavGroupViewModel> groups,
    bool topLevel = true) {
    var group = new NavGroupViewModel {
      Section = section,
      Path = path,
      IconData = topLevel ? NavigatorIcons.Path(section) : "",
      Items = items,
      Groups = groups,
      ExpandedChanged = PersistNavigatorExpanded
    };
    group.IsExpanded = _configuration.Current.IsNavigatorExpanded(path);
    return group;
  }

  private void PersistNavigatorExpanded(NavGroupViewModel _) {
    var cfg = _configuration.Current;
    cfg.SetNavigatorExpanded(SnapshotExpanded(Navigator));
    _configuration.Save(cfg);
  }

  private static Dictionary<string, bool> SnapshotExpanded(IEnumerable<NavGroupViewModel> groups) {
    var map = new Dictionary<string, bool>(StringComparer.Ordinal);
    void Walk(NavGroupViewModel group) {
      map[group.Path] = group.IsExpanded;
      foreach (var child in group.Groups)
        Walk(child);
    }

    foreach (var group in groups)
      Walk(group);
    return map;
  }

  private NavItemViewModel ToNavItem(NavigatorItem item) =>
    new() { Item = item, Select = selected => SelectedNavItem = selected };

  private async Task LoadWorkloadsOverviewAsync() {
    Rows.Clear();
    WorkloadCounts.Clear();
    if (_workspace.Session is null)
      return;

    var kinds = new[] {
      "pods", "deployments", "statefulsets", "daemonsets",
      "replicasets", "jobs", "cronjobs", "replicationcontrollers"
    };
    foreach (var id in kinds) {
      var listed = await _workspace.ListAsync(id, SelectedNamespace, null);
      var count = listed.IsSuccess ? listed.Value?.Count ?? 0 : 0;
      WorkloadCounts.Add(new WorkloadKindCount {
        Id = id,
        Title = ResourceCatalog.Find(id)?.Title ?? id,
        Count = count,
        Open = OpenWorkloadKind
      });
    }

    OverviewText = SelectedNamespace == Configuration.AllNamespaces
      ? "Counts for all namespaces."
      : $"Counts in namespace {SelectedNamespace}.";
    _setStatus($"Workloads · {Name}");
  }

  private void OpenWorkloadKind(string id) {
    var item = Navigator.SelectMany(g => g.AllItems()).Select(i => i.Item).FirstOrDefault(i => i.Id == id);
    if (item is not null)
      SelectedNavItem = item;
  }

  private async Task SampleClusterUsageAsync() {
    if (_workspace.Session is null)
      return;

    var usage = await _workspace.Session.GetClusterUsageAsync();
    if (!usage.IsSuccess || usage.Value is null) {
      MetricsHint = string.Join("; ", usage.Messages);
      return;
    }

    ApplyUsage(usage.Value);
  }

  private async Task LoadOverviewIssuesAsync() {
    var issues = await _workspace.GetClusterIssuesAsync();
    var warnings = issues.IsSuccess && issues.Value is not null
      ? issues.Value.Warnings
      : [];
    var errors = issues.IsSuccess && issues.Value is not null
      ? issues.Value.Errors
      : [];
    CollectionSync.MergeByKey(OverviewWarnings, warnings, issue => issue.Id);
    CollectionSync.MergeByKey(OverviewErrors, errors, issue => issue.Id);

    OnPropertyChanged(nameof(OverviewWarningsCaption));
    OnPropertyChanged(nameof(OverviewErrorsCaption));
    OnPropertyChanged(nameof(HasOverviewWarnings));
    OnPropertyChanged(nameof(HasOverviewErrors));
  }

  private void ApplyUsage(ClusterUsage usage) {
    if (IsClusterDashboard)
      OverviewText = $"Kubernetes {usage.GitVersion} ({usage.Platform})\nNodes: {usage.NodeCount}";
    CpuSlice = usage.Cpu;
    MemorySlice = usage.Memory;
    PodSlice = usage.Pods;
    CpuCaption = usage.CpuCaption;
    MemoryCaption = usage.MemoryCaption;
    PodCaption = usage.PodCaption;
    CpuPercent = usage.CpuPercent;
    MemoryPercent = usage.MemoryPercent;
    PodPercent = usage.PodPercent;
    ApplyNodeUsages(usage.Nodes, usage.MetricsAvailable);
    ApplyLimitRows(usage.ContainerLimits);
    MetricsHint = usage.MetricsAvailable
      ? "Live usage from metrics-server (metrics.k8s.io). Sparklines are sampled in this session."
      : usage.MetricsMessage ?? MetricsHint;

    if (!usage.MetricsAvailable)
      return;

    AppendHistory(_cpuHistory, usage.CpuPercent);
    AppendHistory(_memoryHistory, usage.MemoryPercent);
    CpuHistory = _cpuHistory.ToArray();
    MemoryHistory = _memoryHistory.ToArray();
  }

  private void ApplyNodeUsages(IReadOnlyList<NodeUsage> nodes, bool sampleHistory) {
    var existing = NodeUsages.ToDictionary(n => n.Name, StringComparer.Ordinal);
    var desired = new List<NodeUsageViewModel>(nodes.Count);
    foreach (var node in nodes) {
      if (!existing.TryGetValue(node.Name, out var vm))
        vm = new NodeUsageViewModel { Name = node.Name };
      vm.Update(node, HistoryPoints, sampleHistory);
      desired.Add(vm);
    }

    CollectionSync.MergeByKey(NodeUsages, desired, n => n.Name);
  }

  private void ApplyLimitRows(IReadOnlyList<WorkloadContainerLimit> limits) {
    var keep = SelectedLimitRow;
    var existing = LimitRows.ToDictionary(row => LimitKey(row.Source), StringComparer.Ordinal);
    var desired = new List<LimitRowViewModel>(limits.Count);
    foreach (var limit in limits) {
      if (existing.TryGetValue(LimitKey(limit), out var vm)) {
        vm.SyncFrom(limit);
        desired.Add(vm);
      }
      else
        desired.Add(new LimitRowViewModel(limit));
    }

    CollectionSync.MergeByKey(LimitRows, desired, row => LimitKey(row.Source));
    if (keep is not null && LimitRows.Contains(keep))
      SelectedLimitRow = keep;
    else if (SelectedLimitRow is null || !LimitRows.Contains(SelectedLimitRow))
      SelectedLimitRow = LimitRows.FirstOrDefault();
    OnPropertyChanged(nameof(HasContainerLimits));
    OnPropertyChanged(nameof(HasLimitOvercommit));
    OnPropertyChanged(nameof(LimitsCaption));
    OnPropertyChanged(nameof(CanApplyLimits));
  }

  private static string LimitKey(WorkloadContainerLimit limit) =>
    $"{limit.Namespace}\0{limit.WorkloadKind}\0{limit.WorkloadName}\0{limit.Container}\0{limit.Init}";

  public bool CanApplyLimits =>
    LimitRows.Any(row => row.IsDirty) || SelectedLimitRow is not null;

  [RelayCommand]
  private async Task ApplySelectedLimitAsync() {
    if (SelectedLimitRow is null)
      return;
    if (!SelectedLimitRow.IsDirty) {
      _setStatus("Edit CPU limit or MEM limit on the selected row, then apply.");
      return;
    }

    await ApplyLimitAsync(SelectedLimitRow);
  }

  [RelayCommand]
  private async Task ApplyChangedLimitsAsync() {
    var dirty = LimitRows.Where(row => row.IsDirty).ToList();
    if (dirty.Count == 0) {
      _setStatus("No limit edits to apply.");
      return;
    }

    foreach (var row in dirty)
      await ApplyLimitAsync(row);
  }

  private async Task ApplyLimitAsync(LimitRowViewModel row) {
    if (_workspace.Session is null)
      return;
    var patched = await _workspace.Session.PatchContainerResourcesAsync(
      row.Source,
      row.CpuLimit,
      row.MemoryLimit);
    _setStatus(patched.IsSuccess
      ? $"Patched {row.Workload} container {row.Source.Container}."
      : string.Join("; ", patched.Messages));
    if (patched.IsSuccess)
      await SampleClusterUsageAsync();
  }

  private static void AppendHistory(List<double> history, double value) {
    history.Add(value);
    if (history.Count > HistoryPoints)
      history.RemoveAt(0);
  }

  private async Task LoadDetailsAsync() {
    _logsCts?.Cancel();
    var row = SelectedRow;
    if (row is null) {
      if (IsDirty)
        return;

      _detailsUid = null;
      _editDocument = null;
      SetYaml("");
      OverviewText = "";
      EventsText = "";
      LogsText = "";
      TerminalText = "";
      DataEntries.Clear();
      ReplaceRelatedPods([]);
      ApplyContainers(null);
      NotifyDetailsUi();
      return;
    }

    _detailsUid = row.Uid;
    var document = row.Document;
    var resource = SelectedResourceRef();
    if (resource is not null && _workspace.Session is not null) {
      var got = await _workspace.Session.GetAsync(resource.ToRef(), row.Name, row.Namespace);
      if (!DetailsStillCurrent(row))
        return;
      if (got.IsSuccess && got.Value is not null)
        document = got.Value;
    }

    _editDocument = ResourceDocument.PrepareForEdit(document);
    SetYaml(YamlFormatter.FromJson(_editDocument));
    ReplaceDataEntries(_editDocument);

    IReadOnlyList<ResourceRow> related = [];
    if (ShowPodsTab) {
      var listed = await _workspace.RelatedPodsAsync(row);
      if (!DetailsStillCurrent(row))
        return;
      related = listed.IsSuccess ? listed.Value ?? [] : [];
    }

    ReplaceRelatedPods(related);
    var podDocument = IsPodSelection ? document : SelectedRelatedPod?.Document;
    ApplyContainers(podDocument);
    OverviewText = row.FormatOverview(Containers);
    var events = await _workspace.EventsForAsync(row);
    if (!DetailsStillCurrent(row))
      return;
    EventsText = events.IsSuccess
      ? string.Join('\n', (events.Value ?? []).Select(e => $"{e.Cells.GetValueOrDefault("Type")} {e.Cells.GetValueOrDefault("Reason")} {e.Cells.GetValueOrDefault("Message")}"))
      : string.Join("; ", events.Messages);
    NotifyDetailsUi();
    await LoadLogsAsync();
  }

  private bool DetailsStillCurrent(ResourceRow row) =>
    SelectedRow is not null && string.Equals(SelectedRow.Uid, row.Uid, StringComparison.Ordinal);

  private void SetYaml(string yaml) {
    _loadingDetails = true;
    YamlText = yaml;
    _loadingDetails = false;
    IsDirty = false;
  }

  private void ReplaceDataEntries(JsonObject? document) {
    DataEntries.Clear();
    if (document is null)
      return;

    foreach (var entry in ResourceDocument.ReadDataEntries(document)) {
      DataEntries.Add(new DataEntryViewModel {
        Key = entry.Key,
        Value = entry.Value,
        IsBinary = entry.IsBinary
      });
    }
  }

  private async Task LoadLogsAsync() {
    _logsCts?.Cancel();
    _logsCts = null;
    if (!ShowLogsTab) {
      LogsText = "";
      return;
    }

    var pod = TargetPodName;
    var ns = TargetPodNamespace;
    if (pod is null || ns is null || _workspace.Session is null) {
      LogsText = ShowPodsTab ? "Select a pod to read logs." : "";
      return;
    }

    if (Containers.Count > 1 && SelectedContainer is null) {
      LogsText = "Select a container to read logs.";
      return;
    }

    var container = SelectedContainer?.Name;
    if (FollowLogs) {
      _logsCts = new CancellationTokenSource();
      LogsText = "";
      _ = FollowLogsLoopAsync(pod, ns, container, _logsCts.Token);
      return;
    }

    var logs = await _workspace.Session.GetLogsAsync(pod, ns, container, false, 200);
    LogsText = logs.IsSuccess ? logs.Value ?? "" : string.Join("; ", logs.Messages);
  }

  private async Task FollowLogsLoopAsync(string pod, string ns, string? container, CancellationToken cancellationToken) {
    if (_workspace.Session is null)
      return;

    try {
      await foreach (var line in _workspace.Session.FollowLogsAsync(pod, ns, container, cancellationToken)) {
        LogsText += line + Environment.NewLine;
        if (LogsText.Length > 200_000)
          LogsText = LogsText[^100_000..];
      }
    }
    catch (OperationCanceledException) {
    }
    catch (Exception ex) {
      LogsText = string.IsNullOrEmpty(LogsText) ? ex.Message : LogsText + Environment.NewLine + ex.Message;
    }
  }

  private void ReplaceRelatedPods(IReadOnlyList<ResourceRow> pods) {
    _updatingPodContext = true;
    var keep = SelectedRelatedPod?.Name;
    RelatedPods.Clear();
    foreach (var pod in pods)
      RelatedPods.Add(pod);

    SelectedRelatedPod = RelatedPods.FirstOrDefault(p => p.Name == keep)
      ?? RelatedPods.FirstOrDefault();
    _updatingPodContext = false;
  }

  private void ApplyContainers(JsonObject? pod) {
    _updatingPodContext = true;
    var keep = SelectedContainer?.Name;
    Containers.Clear();
    foreach (var container in JsonPath.ListPodContainers(pod))
      Containers.Add(container);

    SelectedContainer = Containers.FirstOrDefault(c => c.Name == keep)
      ?? Containers.FirstOrDefault(c => c.Kind == "Container")
      ?? Containers.FirstOrDefault();
    _updatingPodContext = false;
    OnPropertyChanged(nameof(HasContainers));
  }

  private bool IsPodSelection =>
    SelectedDescriptor?.Kind == "Pod"
    || SelectedNavItem?.Id is ResourceCatalog.DaprSidecarsId or ResourceCatalog.DaprControlPlaneId
    || string.Equals(SelectedRow?.Document["kind"]?.GetValue<string>(), "Pod", StringComparison.OrdinalIgnoreCase);

  private string? TargetPodName =>
    IsPodSelection ? SelectedRow?.Name : SelectedRelatedPod?.Name;

  private string? TargetPodNamespace =>
    IsPodSelection ? SelectedRow?.Namespace : SelectedRelatedPod?.Namespace ?? SelectedRow?.Namespace;

  private bool HasDetailTab(string tab) {
    if (IsPodSelection)
      return tab is "Overview" or "YAML" or "Events" or "Logs" or "Terminal";

    return SelectedDescriptor?.DetailTabs.Contains(tab) == true;
  }

  private void NotifyActionFlags() {
    OnPropertyChanged(nameof(CanDelete));
    OnPropertyChanged(nameof(CanForceDelete));
    OnPropertyChanged(nameof(CanDeleteNamespace));
    OnPropertyChanged(nameof(CanScale));
    OnPropertyChanged(nameof(CanRestart));
    OnPropertyChanged(nameof(CanLogs));
    OnPropertyChanged(nameof(CanExec));
    OnPropertyChanged(nameof(CanPortForward));
    OnPropertyChanged(nameof(CanCordon));
    OnPropertyChanged(nameof(CanDrain));
    OnPropertyChanged(nameof(CanTrigger));
    OnPropertyChanged(nameof(CanApply));
    OnPropertyChanged(nameof(CanBrowseFiles));
    BrowseFilesCommand.NotifyCanExecuteChanged();
  }

  private void NotifyDetailsUi() {
    OnPropertyChanged(nameof(ShowEventsTab));
    OnPropertyChanged(nameof(ShowPodsTab));
    OnPropertyChanged(nameof(ShowLogsTab));
    OnPropertyChanged(nameof(ShowTerminalTab));
    OnPropertyChanged(nameof(ShowPodPicker));
    OnPropertyChanged(nameof(ShowContainerPicker));
    OnPropertyChanged(nameof(HasContainers));
    OnPropertyChanged(nameof(HasSelectedRow));
    OnPropertyChanged(nameof(DetailsTitle));
    OnPropertyChanged(nameof(CanLogs));
    OnPropertyChanged(nameof(CanExec));
    OnPropertyChanged(nameof(CanPortForward));
    OnPropertyChanged(nameof(CanBrowseFiles));
    BrowseFilesCommand.NotifyCanExecuteChanged();
  }

  private void StartRefreshLoop() {
    _refreshCts?.Cancel();
    _refreshCts = new CancellationTokenSource();
    var token = _refreshCts.Token;
    _ = Task.Run(async () => {
      while (!token.IsCancellationRequested) {
        try {
          await Task.Delay(TimeSpan.FromSeconds(5), token);
          await RefreshRowsAsync();
        }
        catch (OperationCanceledException) {
          break;
        }
      }
    }, token);
  }
}
