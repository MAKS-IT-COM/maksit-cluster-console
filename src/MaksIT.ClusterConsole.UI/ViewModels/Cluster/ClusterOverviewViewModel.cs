using System.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class ClusterOverviewViewModel : ObservableObject {
  private const int HistoryPoints = 60;

  private readonly ClusterWorkspace _workspace;
  private readonly ConfigurationFileService _configuration;
  private readonly Action<string> _setStatus;
  private readonly Func<bool> _isDashboard;
  private readonly Action<string> _setOverview;
  private readonly List<double> _cpuHistory = [];
  private readonly List<double> _memoryHistory = [];

  public ClusterOverviewViewModel(
    ClusterWorkspace workspace,
    ConfigurationFileService configuration,
    Action<string> setStatus,
    Func<bool> isDashboard,
    Action<string> setOverview) {
    _workspace = workspace;
    _configuration = configuration;
    _setStatus = setStatus;
    _isDashboard = isDashboard;
    _setOverview = setOverview;
    overviewPerNode = configuration.Current.OverviewPerNode;
  }

  public ObservableCollection<NodeUsageViewModel> NodeUsages { get; } = [];

  public ObservableCollection<ClusterIssue> OverviewWarnings { get; } = [];

  public ObservableCollection<ClusterIssue> OverviewErrors { get; } = [];

  public ObservableCollection<LimitRowViewModel> LimitRows { get; } = [];

  [ObservableProperty]
  private LimitRowViewModel? selectedLimitRow;

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

  public string OverviewWarningsCaption =>
    ClusterIssues.Caption("Warnings", OverviewWarnings);

  public string OverviewErrorsCaption =>
    ClusterIssues.Caption("Errors", OverviewErrors);

  public bool HasOverviewWarnings => OverviewWarnings.Count > 0;

  public bool HasOverviewErrors => OverviewErrors.Count > 0;

  public bool HasContainerLimits => LimitRows.Count > 0;

  public bool HasSelectedLimit => SelectedLimitRow is not null;

  public bool HasDirtyLimits => LimitRows.Any(row => row.IsDirty);

  public bool HasLimitOvercommit =>
    CpuSlice.LimitsExceedCapacity || MemorySlice.LimitsExceedCapacity;

  public string LimitsCaption =>
    HasLimitOvercommit
      ? $"Resource limits ({LimitRows.Count}) — specified limits are higher than node capacity"
      : $"Resource limits ({LimitRows.Count})";

  public bool CanApplyLimits =>
    LimitRows.Any(row => row.IsDirty) || SelectedLimitRow is not null;

  partial void OnOverviewPerNodeChanged(bool value) {
    var cfg = _configuration.Current;

    if (cfg.OverviewPerNode == value)
      return;

    cfg.OverviewPerNode = value;
    _configuration.Save(cfg);
  }

  partial void OnSelectedLimitRowChanged(LimitRowViewModel? value) {
    OnPropertyChanged(nameof(HasSelectedLimit));
    OnPropertyChanged(nameof(CanApplyLimits));
  }

  [RelayCommand]
  private void ShowClusterOverview() => OverviewPerNode = false;

  [RelayCommand]
  private void ShowNodesOverview() => OverviewPerNode = true;

  public async Task SampleAsync() {
    if (_workspace.Session is null)
      return;

    var usage = await _workspace.Session.GetClusterUsageAsync();

    if (!usage.IsSuccess || usage.Value is null) {
      MetricsHint = string.Join("; ", usage.Messages);

      return;
    }

    ApplyUsage(usage.Value);
  }

  public async Task LoadIssuesAsync() {
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

  public void Detach() {
    foreach (var row in LimitRows)
      row.PropertyChanged -= OnLimitRowPropertyChanged;
  }

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

  private void ApplyUsage(ClusterUsage usage) {
    if (_isDashboard())
      _setOverview($"Kubernetes {usage.GitVersion} ({usage.Platform})\nNodes: {usage.NodeCount}");

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
    var existing = NodeUsages.ToDictionary(node => node.Name, StringComparer.Ordinal);
    var desired = new List<NodeUsageViewModel>(nodes.Count);

    foreach (var node in nodes) {
      if (!existing.TryGetValue(node.Name, out var vm))
        vm = new NodeUsageViewModel { Name = node.Name };

      vm.Update(node, HistoryPoints, sampleHistory);
      desired.Add(vm);
    }

    CollectionSync.MergeByKey(NodeUsages, desired, node => node.Name);
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

    foreach (var row in LimitRows)
      row.PropertyChanged -= OnLimitRowPropertyChanged;

    CollectionSync.MergeByKey(LimitRows, desired, row => LimitKey(row.Source));

    foreach (var row in LimitRows)
      row.PropertyChanged += OnLimitRowPropertyChanged;

    if (keep is not null && LimitRows.Contains(keep))
      SelectedLimitRow = keep;
    else if (SelectedLimitRow is null || !LimitRows.Contains(SelectedLimitRow))
      SelectedLimitRow = LimitRows.FirstOrDefault();

    OnPropertyChanged(nameof(HasContainerLimits));
    OnPropertyChanged(nameof(HasLimitOvercommit));
    OnPropertyChanged(nameof(LimitsCaption));
    OnPropertyChanged(nameof(HasSelectedLimit));
    OnPropertyChanged(nameof(HasDirtyLimits));
    OnPropertyChanged(nameof(CanApplyLimits));
  }

  private void OnLimitRowPropertyChanged(object? sender, PropertyChangedEventArgs e) {
    if (e.PropertyName is nameof(LimitRowViewModel.CpuLimit)
        or nameof(LimitRowViewModel.MemoryLimit)
        or nameof(LimitRowViewModel.IsDirty)) {
      OnPropertyChanged(nameof(HasDirtyLimits));
      OnPropertyChanged(nameof(CanApplyLimits));
    }
  }

  private static string LimitKey(WorkloadContainerLimit limit) =>
    $"{limit.Namespace}\0{limit.WorkloadKind}\0{limit.WorkloadName}\0{limit.Container}\0{limit.Init}";

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
      await SampleAsync();
  }

  private static void AppendHistory(List<double> history, double value) {
    history.Add(value);

    if (history.Count > HistoryPoints)
      history.RemoveAt(0);
  }
}
