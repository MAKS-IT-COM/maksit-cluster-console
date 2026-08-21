using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI;

internal sealed class LayoutPersistence {
  private readonly Window _window;
  private readonly ConfigurationFileService _configuration;
  private readonly Func<string?> _contextName;
  private readonly Func<string?> _resourceTableId;
  private readonly DispatcherTimer _saveTimer;
  private readonly Dictionary<DataGrid, Func<string>> _tables = [];
  private readonly Dictionary<DataGrid, PendingColumnSort> _pendingSorts = [];
  private int _applyDepth;
  private int _restoreSortPending;
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
    Track(_window.FindControl<DataGrid>("ResourceGrid"), () => LayoutSettings.ResourceTable(_resourceTableId()));
    Track(_window.FindControl<DataGrid>("OverviewWarningsGrid"), () => LayoutSettings.OverviewWarningsTable);
    Track(_window.FindControl<DataGrid>("OverviewErrorsGrid"), () => LayoutSettings.OverviewErrorsTable);
    Track(_window.FindControl<DataGrid>("OverviewLimitsGrid"), () => LayoutSettings.OverviewLimitsTable);
    Track(_window.FindControl<DataGrid>("DataEntriesGrid"), () => LayoutSettings.DataEditorTable);

    Apply();
    TrackPane("ShellGrid");
    TrackPane("ResourceTableGrid");
    _window.Resized += (_, _) => ScheduleSave();
    _window.PositionChanged += (_, _) => ScheduleSave();
    _window.PropertyChanged += (_, e) => {
      if (e.Property.Name == nameof(Window.WindowState))
        ScheduleSave();
    };
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
      foreach (var (grid, key) in _tables)
        ApplyColumnState(grid, key());
    }
  }

  public void ScheduleSave() {
    if (_applyDepth > 0 || _restoreSortPending > 0)
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
    var context = _contextName();
    CaptureWindow(layout);
    CapturePanes(layout);
    foreach (var (grid, key) in _tables) {
      var tableKey = key();
      var widths = ReadColumnWidths(grid);
      if (widths.Count > 0)
        layout.SetColumns(context, tableKey, widths);
    }

    var snapshot = JsonSnapshot(layout);
    if (snapshot == _lastSaved)
      return;

    _configuration.Save(cfg);
    _lastSaved = snapshot;
  }

  private void Apply() {
    using (SuspendSave()) {
      var layout = _configuration.Current.Layout;
      ApplyWindow(layout);
      ApplyPanes(layout);
      foreach (var (grid, key) in _tables)
        ApplyColumnState(grid, key());
    }
  }

  private void TrackPane(string gridName) {
    var grid = _window.FindControl<Grid>(gridName);
    if (grid is not null)
      grid.LayoutUpdated += (_, _) => ScheduleSave();
  }

  private void Track(DataGrid? grid, Func<string> key) {
    if (grid is null)
      return;
    _tables[grid] = key;
    grid.LayoutUpdated += (_, _) => {
      TryApplyPendingSort(grid);
      ScheduleSave();
    };
    grid.Sorting += (_, e) => PersistSort(grid, e.Column);
  }

  private void ApplyWindow(LayoutSettings layout) {
    var width = Clamp(layout.WindowWidth, _window.MinWidth, 10000, 1400);
    var height = Clamp(layout.WindowHeight, _window.MinHeight, 10000, 860);
    _window.Width = width;
    _window.Height = height;

    if (layout.WindowX is int x && layout.WindowY is int y && IsOnScreen(x, y))
      _window.Position = new PixelPoint(x, y);

    if (Enum.TryParse<WindowState>(layout.WindowState, true, out var state) && state != WindowState.Minimized)
      _window.WindowState = state;
  }

  private void CaptureWindow(LayoutSettings layout) {
    if (_window.WindowState == WindowState.Normal) {
      layout.WindowWidth = _window.Width;
      layout.WindowHeight = _window.Height;
      layout.WindowX = _window.Position.X;
      layout.WindowY = _window.Position.Y;
    }

    layout.WindowState = _window.WindowState == WindowState.Minimized
      ? WindowState.Normal.ToString()
      : _window.WindowState.ToString();
  }

  private void ApplyPanes(LayoutSettings layout) {
    SetColumnWidth("ShellGrid", 0, Clamp(layout.CatalogWidth, 120, 900, 248));
    SetColumnWidth("ShellGrid", 2, Clamp(layout.NavigatorWidth, 120, 900, 228));
    SetColumnWidth("ResourceTableGrid", 2, Clamp(layout.DetailsWidth, 180, 1600, 380));
  }

  private void CapturePanes(LayoutSettings layout) {
    var catalog = PaneWidth("CatalogPane", "ShellGrid", 0);
    var navigator = PaneWidth("NavigatorPane", "ShellGrid", 2);
    var details = PaneWidth("DetailsPane", "ResourceTableGrid", 2);
    if (catalog >= 120)
      layout.CatalogWidth = catalog;
    if (navigator >= 120)
      layout.NavigatorWidth = navigator;
    if (details >= 180)
      layout.DetailsWidth = details;
  }

  private double PaneWidth(string paneName, string gridName, int column) {
    var pane = _window.FindControl<Control>(paneName);
    if (pane is { IsVisible: true } && pane.Bounds.Width > 0)
      return pane.Bounds.Width;

    var grid = _window.FindControl<Grid>(gridName);
    if (grid is null || column < 0 || column >= grid.ColumnDefinitions.Count)
      return 0;

    var definition = grid.ColumnDefinitions[column];
    if (definition.Width.IsAbsolute)
      return definition.Width.Value;
    return 0;
  }

  private void SetColumnWidth(string gridName, int index, double width) {
    var grid = _window.FindControl<Grid>(gridName);
    if (grid is null || index < 0 || index >= grid.ColumnDefinitions.Count)
      return;
    grid.ColumnDefinitions[index].Width = new GridLength(width);
  }

  private void ApplyColumnState(DataGrid grid, string tableKey) {
    ApplyColumnWidths(grid, tableKey);
    ApplyColumnSort(grid, tableKey);
  }

  private void ApplyColumnWidths(DataGrid grid, string tableKey) {
    var saved = _configuration.Current.Layout.ColumnsFor(_contextName(), tableKey);
    if (saved is null)
      return;

    foreach (var column in grid.Columns) {
      var header = ColumnKey(column);
      if (header is null || !saved.TryGetValue(header, out var width) || width < 32)
        continue;
      column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
    }
  }

  private void ApplyColumnSort(DataGrid grid, string tableKey) {
    var saved = _configuration.Current.Layout.SortFor(_contextName(), tableKey);
    if (saved is null) {
      _pendingSorts.Remove(grid);
      return;
    }
    if (!Enum.TryParse<ListSortDirection>(saved.Direction, true, out var direction))
      direction = ListSortDirection.Ascending;

    _pendingSorts[grid] = new PendingColumnSort(saved.Header, direction);
    Dispatcher.UIThread.Post(() => TryApplyPendingSort(grid), DispatcherPriority.Loaded);
  }

  private void TryApplyPendingSort(DataGrid grid) {
    if (!_pendingSorts.TryGetValue(grid, out var pending))
      return;

    var column = FindColumn(grid, pending.Header);
    if (column is null) {
      _pendingSorts.Remove(grid);
      return;
    }

    // Sort() NREs when the column is detached or the header has not been generated yet.
    if (!grid.IsAttachedToVisualTree() || !grid.IsEffectivelyVisible || column.ActualWidth <= 0)
      return;

    _pendingSorts.Remove(grid);
    _restoreSortPending++;
    column.Sort(pending.Direction);
    Dispatcher.UIThread.Post(() => {
      if (_restoreSortPending > 0)
        _restoreSortPending--;
    }, DispatcherPriority.Background);
  }

  private static DataGridColumn? FindColumn(DataGrid grid, string header) {
    foreach (var candidate in grid.Columns) {
      if (string.Equals(ColumnKey(candidate), header, StringComparison.Ordinal))
        return candidate;
    }

    return null;
  }

  private void PersistSort(DataGrid grid, DataGridColumn column) {
    if (_applyDepth > 0 || _restoreSortPending > 0)
      return;
    if (!_tables.TryGetValue(grid, out var key))
      return;
    var header = ColumnKey(column);
    if (header is null)
      return;

    var context = _contextName();
    var tableKey = key();
    var previous = _configuration.Current.Layout.SortFor(context, tableKey);
    var direction = ListSortDirection.Ascending;
    if (previous is not null
        && string.Equals(previous.Header, header, StringComparison.Ordinal)
        && string.Equals(previous.Direction, nameof(ListSortDirection.Ascending), StringComparison.OrdinalIgnoreCase))
      direction = ListSortDirection.Descending;

    var cfg = _configuration.Current;
    cfg.EnsureDefaults();
    cfg.Layout.SetSort(context, tableKey, new SavedColumnSort {
      Header = header,
      Direction = direction.ToString()
    });
    ScheduleSave();
  }

  private static Dictionary<string, double> ReadColumnWidths(DataGrid grid) {
    var widths = new Dictionary<string, double>(StringComparer.Ordinal);
    foreach (var column in grid.Columns) {
      var header = ColumnKey(column);
      var width = column.ActualWidth > 0 ? column.ActualWidth : column.Width.Value;
      if (header is null || width < 32)
        continue;
      widths[header] = width;
    }

    return widths;
  }

  private static string? ColumnKey(DataGridColumn column) =>
    column.Tag as string ?? column.Header as string ?? column.Header?.ToString();

  private bool IsOnScreen(int x, int y) {
    var screens = _window.Screens?.All;
    if (screens is null || screens.Count == 0)
      return true;
    return screens.Any(screen => screen.WorkingArea.Contains(new PixelPoint(x, y)));
  }

  private static double Clamp(double value, double min, double max, double fallback) =>
    value < min || value > max || double.IsNaN(value) ? fallback : value;

  private static string JsonSnapshot(LayoutSettings layout) =>
    System.Text.Json.JsonSerializer.Serialize(layout);

  private void ReleaseApply() {
    if (_applyDepth > 0)
      _applyDepth--;
  }

  private sealed record PendingColumnSort(string Header, ListSortDirection Direction);

  private sealed class ApplyScope(LayoutPersistence owner) : IDisposable {
    public void Dispose() => owner.ReleaseApply();
  }
}
