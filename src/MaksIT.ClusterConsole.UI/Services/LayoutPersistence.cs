using Avalonia.Controls;
using Avalonia.Threading;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.Services;

internal sealed class LayoutPersistence {
  private readonly Window _window;
  private readonly ConfigurationFileService _configuration;
  private readonly Func<string?> _contextName;
  private readonly Func<string?> _resourceTableId;
  private readonly DispatcherTimer _saveTimer;
  private readonly List<ILayoutOriginator> _originators = [];
  private int _applyDepth;
  private bool _attached;
  private string? _lastSaved;

  public LayoutPersistence(
    Window window,
    ConfigurationFileService configuration,
    Func<string?> contextName,
    Func<string?> resourceTableId) {
    _window = window;
    _configuration = configuration;
    _contextName = contextName;
    _resourceTableId = resourceTableId;
    _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
    _saveTimer.Tick += (_, _) => {
      _saveTimer.Stop();
      SaveNow();
    };
  }

  public void Attach() {
    if (_attached)
      return;

    _attached = true;
    Subscribe(new WindowLayoutOriginator(_window));
    Subscribe(GridBandOriginator.Column(
      _window.FindControl<Grid>("ShellGrid"),
      0, 120, 900, 248,
      layout => layout.CatalogWidth,
      (layout, value) => layout.CatalogWidth = value,
      _window.FindControl<Control>("CatalogPane")));
    Subscribe(GridBandOriginator.Column(
      _window.FindControl<Grid>("ShellGrid"),
      2, 120, 900, 228,
      layout => layout.NavigatorWidth,
      (layout, value) => layout.NavigatorWidth = value,
      _window.FindControl<Control>("NavigatorPane")));
    Subscribe(GridBandOriginator.Column(
      _window.FindControl<Grid>("ResourceTableGrid"),
      2, 180, 1600, 380,
      layout => layout.DetailsWidth,
      (layout, value) => layout.DetailsWidth = value,
      _window.FindControl<Control>("DetailsPane")));
    Subscribe(GridBandOriginator.Row(
      _window.FindControl<Grid>("HelmHistoryGrid"),
      1, 80, 1600, 220,
      layout => layout.HelmHistoryHeight,
      (layout, value) => layout.HelmHistoryHeight = value,
      minHostExtent: 160));
    Subscribe(Table("ResourceGrid", () => LayoutSettings.ResourceTable(_resourceTableId())));
    Subscribe(Table("OverviewWarningsGrid", () => LayoutSettings.OverviewWarningsTable));
    Subscribe(Table("OverviewErrorsGrid", () => LayoutSettings.OverviewErrorsTable));
    Subscribe(Table("OverviewLimitsGrid", () => LayoutSettings.OverviewLimitsTable));
    Subscribe(Table("DataEntriesGrid", () => LayoutSettings.DataEditorTable));
    Subscribe(Table("HelmRevisionsGrid", () => LayoutSettings.HelmHistoryTable));

    Apply();
    _window.Closing += (_, _) => {
      _saveTimer.Stop();
      SaveNow();
    };
  }

  public IDisposable SuspendSave() {
    _applyDepth++;
    return new ApplyScope(this);
  }

  public void RestoreTables() {
    using (SuspendSave()) {
      var layout = _configuration.Current.Layout;
      foreach (var originator in _originators) {
        if (originator is DataGridLayoutOriginator)
          originator.Apply(layout);
      }
    }
  }

  public void ScheduleSave() {
    if (_applyDepth > 0 || _originators.Any(originator => originator.DeferSave))
      return;
    _saveTimer.Stop();
    _saveTimer.Start();
  }

  public void SaveNow() {
    if (_applyDepth > 0)
      return;

    var cfg = _configuration.Current;
    cfg.EnsureDefaults();
    var layout = cfg.Layout;
    foreach (var originator in _originators)
      originator.Capture(layout);

    var snapshot = System.Text.Json.JsonSerializer.Serialize(layout);
    if (snapshot == _lastSaved)
      return;

    _configuration.Save(cfg);
    _lastSaved = snapshot;
  }

  private void Subscribe(ILayoutOriginator originator) {
    _originators.Add(originator);
    originator.Attach(ScheduleSave);
  }

  private DataGridLayoutOriginator Table(string name, Func<string> key) =>
    new(
      _window.FindControl<DataGrid>(name),
      _contextName,
      key,
      () => _configuration.Current.Layout);

  private void Apply() {
    using (SuspendSave()) {
      var layout = _configuration.Current.Layout;
      foreach (var originator in _originators)
        originator.Apply(layout);
    }
  }

  private void ReleaseApply() {
    if (_applyDepth > 0)
      _applyDepth--;
  }

  private sealed class ApplyScope(LayoutPersistence owner) : IDisposable {
    public void Dispose() => owner.ReleaseApply();
  }
}
