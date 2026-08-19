using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI;

internal sealed class LayoutPersistence {
  private readonly Window _window;
  private readonly ConfigurationFileService _configuration;
  private readonly Func<string?> _resourceTableId;
  private readonly DispatcherTimer _saveTimer;
  private readonly Dictionary<DataGrid, Func<string>> _tables = [];
  private bool _applying;
  private bool _attached;
  private string? _lastSaved;

  public LayoutPersistence(Window window, ConfigurationFileService configuration, Func<string?> resourceTableId) {
    _window = window;
    _configuration = configuration;
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

  public void ApplyResourceColumns(DataGrid grid) =>
    ApplyColumnWidths(grid, LayoutSettings.ResourceTable(_resourceTableId()));

  public void ScheduleSave() {
    if (_applying)
      return;
    _saveTimer.Stop();
    _saveTimer.Start();
  }

  public void SaveNow() {
    if (_applying)
      return;

    var cfg = _configuration.Current;
    cfg.EnsureDefaults();
    var layout = cfg.Layout;
    CaptureWindow(layout);
    CapturePanes(layout);
    foreach (var (grid, key) in _tables) {
      var widths = ReadColumnWidths(grid);
      if (widths.Count > 0)
        layout.SetColumns(key(), widths);
    }

    var snapshot = JsonSnapshot(layout);
    if (snapshot == _lastSaved)
      return;

    _configuration.Save(cfg);
    _lastSaved = snapshot;
  }

  private void Apply() {
    _applying = true;
    try {
      var layout = _configuration.Current.Layout;
      ApplyWindow(layout);
      ApplyPanes(layout);
      foreach (var (grid, key) in _tables)
        ApplyColumnWidths(grid, key());
    }
    finally {
      _applying = false;
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
    grid.LayoutUpdated += (_, _) => ScheduleSave();
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

  private void ApplyColumnWidths(DataGrid grid, string tableKey) {
    var saved = _configuration.Current.Layout.ColumnsFor(tableKey);
    if (saved is null)
      return;

    foreach (var column in grid.Columns) {
      var header = ColumnKey(column);
      if (header is null || !saved.TryGetValue(header, out var width) || width < 32)
        continue;
      column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
    }
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
}
