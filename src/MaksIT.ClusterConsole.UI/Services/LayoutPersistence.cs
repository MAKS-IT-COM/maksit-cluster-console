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
  private bool _applied;
  private string? _lastSaved;

  internal Func<string?> ContextName => _contextName;

  internal Func<string> ResourceTableKey => () => LayoutSettings.ResourceTable(_resourceTableId());

  internal Func<LayoutSettings> Layout => () => _configuration.Current.Layout;

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
    LayoutMemento.Publish(_window, this);
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
        if (originator.RestoreOnTableChange)
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

  internal void Register(ILayoutOriginator originator) {
    _originators.Add(originator);
    originator.Attach(ScheduleSave);

    if (!_applied)
      return;

    using (SuspendSave())
      originator.Apply(_configuration.Current.Layout);
  }

  private void Apply() {
    using (SuspendSave()) {
      var layout = _configuration.Current.Layout;

      foreach (var originator in _originators)
        originator.Apply(layout);
    }

    _applied = true;
  }

  private void ReleaseApply() {
    if (_applyDepth > 0)
      _applyDepth--;
  }

  private sealed class ApplyScope(LayoutPersistence owner) : IDisposable {
    public void Dispose() => owner.ReleaseApply();
  }
}
