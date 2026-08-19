using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class NavItemViewModel : ObservableObject {
  public required NavigatorItem Item { get; init; }

  public required Action<NavigatorItem> Select { get; init; }

  public string Title => Item.Title;

  [ObservableProperty]
  private bool isSelected;

  [RelayCommand]
  private void Activate() => Select(Item);
}

public partial class NodeUsageViewModel : ObservableObject {
  public required string Name { get; init; }

  [ObservableProperty]
  private ResourceSlice cpu = ResourceSlice.Empty("cpu");

  [ObservableProperty]
  private ResourceSlice memory = ResourceSlice.Empty("memory");

  [ObservableProperty]
  private ResourceSlice pods = ResourceSlice.Empty("pods");

  [ObservableProperty]
  private double cpuPercent;

  [ObservableProperty]
  private double memoryPercent;

  [ObservableProperty]
  private double podPercent;

  [ObservableProperty]
  private IReadOnlyList<double> cpuHistory = [];

  [ObservableProperty]
  private IReadOnlyList<double> memoryHistory = [];

  private readonly List<double> _cpuHistory = [];
  private readonly List<double> _memoryHistory = [];

  public void Update(NodeUsage usage, int historyPoints, bool sampleHistory) {
    Cpu = usage.Cpu;
    Memory = usage.Memory;
    Pods = usage.Pods;
    CpuPercent = usage.Cpu.Allocatable <= 0 ? 0 : Math.Clamp(usage.Cpu.Used / usage.Cpu.Allocatable * 100, 0, 100);
    MemoryPercent = usage.Memory.Allocatable <= 0 ? 0 : Math.Clamp(usage.Memory.Used / usage.Memory.Allocatable * 100, 0, 100);
    PodPercent = usage.Pods.Allocatable <= 0 ? 0 : Math.Clamp(usage.Pods.Used / usage.Pods.Allocatable * 100, 0, 100);
    if (!sampleHistory)
      return;

    Append(_cpuHistory, CpuPercent, historyPoints);
    Append(_memoryHistory, MemoryPercent, historyPoints);
    CpuHistory = _cpuHistory.ToArray();
    MemoryHistory = _memoryHistory.ToArray();
  }

  private static void Append(List<double> history, double value, int historyPoints) {
    history.Add(value);
    if (history.Count > historyPoints)
      history.RemoveAt(0);
  }
}

public partial class WorkloadKindCount : ObservableObject {
  public required string Id { get; init; }

  public required string Title { get; init; }

  public required int Count { get; init; }

  public required Action<string> Open { get; init; }

  [RelayCommand]
  private void Activate() => Open(Id);
}

public partial class NavGroupViewModel : ObservableObject {
  public required string Section { get; init; }

  public required string Path { get; init; }

  public string IconData { get; init; } = "";

  public required IReadOnlyList<NavItemViewModel> Items { get; init; }

  public IReadOnlyList<NavGroupViewModel> Groups { get; init; } = [];

  public Action<NavGroupViewModel>? ExpandedChanged { get; init; }

  public bool HasIcon => !string.IsNullOrWhiteSpace(IconData);

  [ObservableProperty]
  private bool isExpanded;

  public string Chevron => IsExpanded ? "▾" : "▸";

  [RelayCommand]
  private void Toggle() {
    IsExpanded = !IsExpanded;
    ExpandedChanged?.Invoke(this);
  }

  partial void OnIsExpandedChanged(bool value) =>
    OnPropertyChanged(nameof(Chevron));

  public bool Contains(string? itemId) =>
    AllItems().Any(i => i.Item.Id == itemId);

  public IEnumerable<NavItemViewModel> AllItems() {
    foreach (var item in Items)
      yield return item;
    foreach (var group in Groups) {
      foreach (var item in group.AllItems())
        yield return item;
    }
  }
}

public sealed class PortForwardItemViewModel {
  public required PortForwardHandle Handle { get; init; }

  public string Cluster { get; init; } = "";

  public string Display =>
    string.IsNullOrEmpty(Cluster)
      ? $"{Handle.Namespace}/{Handle.PodName} {Handle.LocalPort}->{Handle.ContainerPort}"
      : $"{Cluster} {Handle.Namespace}/{Handle.PodName} {Handle.LocalPort}->{Handle.ContainerPort}";
}

public partial class DataEntryViewModel : ObservableObject {
  [ObservableProperty]
  private string key = string.Empty;

  [ObservableProperty]
  private string value = string.Empty;

  [ObservableProperty]
  private bool isBinary;
}

public partial class CatalogItemViewModel : ObservableObject {
  public required KubeContextInfo Context { get; init; }

  public required Action<CatalogItemViewModel> Select { get; init; }

  public required Action<CatalogItemViewModel> Close { get; init; }

  public string Name => Context.Name;

  public string Cluster => Context.Cluster;

  [ObservableProperty]
  private bool isConnected;

  [ObservableProperty]
  private bool isActive;

  [ObservableProperty]
  private bool isKubectlCurrent;

  [RelayCommand]
  private void Activate() => Select(this);

  [RelayCommand]
  private void Disconnect() => Close(this);
}

public partial class MainViewModel : ObservableObject, IDisposable {
  private readonly IKubeConfigService _kubeConfig;
  private readonly IClusterSessionFactory _sessions;
  private readonly ConfigurationFileService _configuration;
  private readonly IOllamaChatClient _ollama;
  private readonly Dictionary<string, ClusterPageViewModel> _pages = new(StringComparer.Ordinal);

  public MainViewModel(
    IKubeConfigService kubeConfig,
    IClusterSessionFactory sessions,
    ConfigurationFileService configuration,
    IOllamaChatClient ollama) {
    _kubeConfig = kubeConfig;
    _sessions = sessions;
    _configuration = configuration;
    _ollama = ollama;
    LoadCatalog();
    _ = RestoreSessionsCommand.ExecuteAsync(null);
  }

  public ObservableCollection<CatalogItemViewModel> Catalog { get; } = [];

  [ObservableProperty]
  private ClusterPageViewModel? activePage;

  [ObservableProperty]
  private string status = "Select a cluster from the catalog.";

  public bool IsClusterOpen => ActivePage is not null;

  public string ClusterTitle => ActivePage?.Name ?? "Catalog";

  public NavigatorItem? SelectedNavItem => ActivePage?.SelectedNavItem;

  public ResourceDescriptor? SelectedDescriptor => ActivePage?.SelectedDescriptor;

  partial void OnActivePageChanged(ClusterPageViewModel? oldValue, ClusterPageViewModel? newValue) {
    if (oldValue is not null)
      oldValue.PropertyChanged -= OnActivePagePropertyChanged;
    if (newValue is not null)
      newValue.PropertyChanged += OnActivePagePropertyChanged;

    OnPropertyChanged(nameof(IsClusterOpen));
    OnPropertyChanged(nameof(ClusterTitle));
    OnPropertyChanged(nameof(SelectedNavItem));
    OnPropertyChanged(nameof(SelectedDescriptor));
  }

  private void OnActivePagePropertyChanged(object? sender, PropertyChangedEventArgs e) {
    if (e.PropertyName is nameof(ClusterPageViewModel.SelectedNavItem)) {
      OnPropertyChanged(nameof(SelectedNavItem));
      OnPropertyChanged(nameof(SelectedDescriptor));
    }
  }

  [RelayCommand]
  private void LoadCatalog() {
    var listed = _kubeConfig.ListContexts();
    if (!listed.IsSuccess) {
      Status = string.Join("; ", listed.Messages);
      return;
    }

    Catalog.Clear();
    foreach (var ctx in listed.Value ?? []) {
      Catalog.Add(new CatalogItemViewModel {
        Context = ctx,
        Select = item => _ = OpenOrSwitchAsync(item),
        Close = item => DisconnectNamed(item.Name)
      });
    }

    DropMissingPages();
    SyncCatalogFlags();
    Status = Catalog.Count == 0
      ? "No kubeconfig contexts found. Open Connections to add one."
      : $"{Catalog.Count} context(s) in kubeconfig · {_pages.Count} connected.";
  }

  [RelayCommand]
  private void OpenConnections() =>
    ConnectionsRequested?.Invoke(this, EventArgs.Empty);

  public event EventHandler? ConnectionsRequested;

  public event Action<VolumeFilesViewModel>? VolumeFilesRequested;

  public ConnectionsViewModel CreateConnectionsViewModel() =>
    new(_kubeConfig);

  public Task ConnectNamedAsync(string name) {
    var item = Catalog.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
    return OpenOrSwitchAsync(item);
  }

  private async Task OpenOrSwitchAsync(CatalogItemViewModel? item) {
    if (item is null)
      return;

    if (_pages.TryGetValue(item.Name, out var existing)) {
      ActivatePage(existing);
      return;
    }

    Status = $"Connecting to {item.Name}…";
    var created = _sessions.Create(item.Name);
    if (!created.IsSuccess || created.Value is null) {
      Status = string.Join("; ", created.Messages);
      return;
    }

    var page = CreatePage(item.Context);
    var started = await page.StartAsync(created.Value);
    if (!started.IsSuccess) {
      page.Dispose();
      Status = string.Join("; ", started.Messages);
      return;
    }

    _pages[item.Name] = page;
    ActivatePage(page);
  }

  [RelayCommand]
  private void Disconnect() {
    if (ActivePage is null)
      return;

    DisconnectNamed(ActivePage.Name);
  }

  private void DisconnectNamed(string name) {
    if (!_pages.TryGetValue(name, out var page))
      return;

    var wasActive = ActivePage == page;
    if (wasActive)
      page.PausePolling();

    page.Dispose();
    _pages.Remove(name);

    if (wasActive) {
      ActivePage = null;
      var next = _pages.Values.FirstOrDefault();
      if (next is not null)
        ActivatePage(next);
      else
        Status = "Disconnected.";
    }

    SyncCatalogFlags();
    PersistOpenState();
  }

  [RelayCommand]
  private async Task RestoreSessionsAsync() {
    var names = (_configuration.Current.OpenContexts ?? [])
      .Where(n => !string.IsNullOrWhiteSpace(n))
      .Distinct(StringComparer.Ordinal)
      .ToList();
    if (names.Count == 0)
      return;

    var preferred = _configuration.Current.ActiveContext;
    foreach (var name in names) {
      var item = Catalog.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
      if (item is null)
        continue;

      var created = _sessions.Create(item.Name);
      if (!created.IsSuccess || created.Value is null)
        continue;

      var page = CreatePage(item.Context);
      var started = await page.StartAsync(created.Value);
      if (!started.IsSuccess) {
        page.Dispose();
        continue;
      }

      page.PausePolling();
      _pages[item.Name] = page;
    }

    var active = _pages.TryGetValue(preferred ?? "", out var preferredPage)
      ? preferredPage
      : _pages.Values.FirstOrDefault();
    if (active is not null)
      ActivatePage(active);
    else
      SyncCatalogFlags();
  }

  private ClusterPageViewModel CreatePage(KubeContextInfo context) {
    var page = new ClusterPageViewModel(context, new ClusterWorkspace(), _configuration, _ollama, text => Status = text);
    page.VolumeFilesRequested += vm => VolumeFilesRequested?.Invoke(vm);
    return page;
  }

  private void ActivatePage(ClusterPageViewModel page) {
    if (ActivePage == page) {
      SyncCatalogFlags();
      return;
    }

    ActivePage?.PausePolling();
    ActivePage = page;
    page.ResumePolling();
    _ = page.RefreshRowsCommand.ExecuteAsync(null);
    SyncCatalogFlags();
    PersistOpenState();
    Status = $"Connected to {page.Name}";
  }

  private void SyncCatalogFlags() {
    var current = _kubeConfig.GetCurrentContext();
    var kubectlCurrent = current.IsSuccess ? current.Value : null;
    foreach (var item in Catalog) {
      item.IsConnected = _pages.ContainsKey(item.Name);
      item.IsActive = ActivePage?.Name == item.Name;
      item.IsKubectlCurrent = string.Equals(item.Name, kubectlCurrent, StringComparison.Ordinal);
    }
  }

  private void DropMissingPages() {
    foreach (var name in _pages.Keys.Except(Catalog.Select(c => c.Name), StringComparer.Ordinal).ToList())
      DisconnectNamed(name);
  }

  private void PersistOpenState() {
    var cfg = _configuration.Current;
    cfg.OpenContexts = [.. _pages.Keys];
    cfg.ActiveContext = ActivePage?.Name;
    _configuration.Save(cfg);
  }

  public void Dispose() {
    foreach (var page in _pages.Values)
      page.Dispose();
    _pages.Clear();
  }
}
