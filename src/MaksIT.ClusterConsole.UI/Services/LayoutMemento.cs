using Avalonia;
using Avalonia.Controls;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.Services;

public enum LayoutSlot {
  CatalogWidth,
  NavigatorWidth,
  DetailsWidth,
  HelmHistoryHeight,
}

public enum LayoutAxis {
  Column,
  Row,
}

public sealed class LayoutBand {
  public LayoutSlot Slot { get; set; }

  public LayoutAxis Axis { get; set; } = LayoutAxis.Column;

  public int Index { get; set; } = -1;

  public double Min { get; set; }

  public double Max { get; set; }

  public double Fallback { get; set; }

  public double MinHostExtent { get; set; }
}

/// <summary>
/// Each control registers its own originator. The caretaker does not name the control.
/// </summary>
public static class LayoutMemento {
  public static readonly AttachedProperty<bool> PersistWindowProperty =
    AvaloniaProperty.RegisterAttached<Window, bool>("PersistWindow", typeof(LayoutMemento));

  public static readonly AttachedProperty<string?> TableProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, string?>("Table", typeof(LayoutMemento));

  public static readonly AttachedProperty<bool> ResourceTableProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, bool>("ResourceTable", typeof(LayoutMemento));

  public static readonly AttachedProperty<LayoutBand?> BandProperty =
    AvaloniaProperty.RegisterAttached<Control, LayoutBand?>("Band", typeof(LayoutMemento));

  private static readonly AttachedProperty<LayoutPersistence?> CaretakerProperty =
    AvaloniaProperty.RegisterAttached<Window, LayoutPersistence?>("Caretaker", typeof(LayoutMemento));

  private static readonly AttachedProperty<bool> WatchingProperty =
    AvaloniaProperty.RegisterAttached<Control, bool>("Watching", typeof(LayoutMemento));

  private static readonly AttachedProperty<bool> RegisteredProperty =
    AvaloniaProperty.RegisterAttached<Control, bool>("Registered", typeof(LayoutMemento));

  private static readonly List<WeakReference<Control>> Pending = [];

  static LayoutMemento() {
    PersistWindowProperty.Changed.AddClassHandler<Window>((control, _) => Watch(control));
    TableProperty.Changed.AddClassHandler<DataGrid>((control, _) => Watch(control));
    ResourceTableProperty.Changed.AddClassHandler<DataGrid>((control, _) => Watch(control));
    BandProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));
    CaretakerProperty.Changed.AddClassHandler<Window>((control, _) => TryRegister(control));
  }

  public static bool GetPersistWindow(Window window) =>
    window.GetValue(PersistWindowProperty);

  public static void SetPersistWindow(Window window, bool value) =>
    window.SetValue(PersistWindowProperty, value);

  public static string? GetTable(DataGrid grid) =>
    grid.GetValue(TableProperty);

  public static void SetTable(DataGrid grid, string? value) =>
    grid.SetValue(TableProperty, value);

  public static bool GetResourceTable(DataGrid grid) =>
    grid.GetValue(ResourceTableProperty);

  public static void SetResourceTable(DataGrid grid, bool value) =>
    grid.SetValue(ResourceTableProperty, value);

  public static LayoutBand? GetBand(Control control) =>
    control.GetValue(BandProperty);

  public static void SetBand(Control control, LayoutBand? value) =>
    control.SetValue(BandProperty, value);

  internal static void Publish(Window window, LayoutPersistence caretaker) {
    window.SetValue(CaretakerProperty, caretaker);

    foreach (var reference in Pending.ToArray()) {
      if (reference.TryGetTarget(out var control))
        TryRegister(control);
    }

    Pending.RemoveAll(reference => !reference.TryGetTarget(out var control) || control.GetValue(RegisteredProperty));
  }

  internal static IDisposable SuspendSave(Control control) =>
    CaretakerFor(control)?.SuspendSave() ?? Noop.Instance;

  internal static void RestoreTables(Control control) =>
    CaretakerFor(control)?.RestoreTables();

  private static void Watch(Control control) {
    if (control.GetValue(WatchingProperty))
      return;

    control.SetValue(WatchingProperty, true);
    control.AttachedToLogicalTree += (_, _) => TryRegister(control);
    TryRegister(control);

    if (!control.GetValue(RegisteredProperty))
      Pending.Add(new WeakReference<Control>(control));
  }

  private static void TryRegister(Control control) {
    if (control.GetValue(RegisteredProperty))
      return;

    var caretaker = CaretakerFor(control);

    if (caretaker is null)
      return;

    var originator = Create(control, caretaker);

    if (originator is null)
      return;

    control.SetValue(RegisteredProperty, true);
    caretaker.Register(originator);
  }

  private static LayoutPersistence? CaretakerFor(StyledElement control) {
    for (StyledElement? node = control; node is not null; node = node.Parent) {
      if (node is Window window && window.GetValue(CaretakerProperty) is LayoutPersistence caretaker)
        return caretaker;
    }

    return null;
  }

  private static ILayoutOriginator? Create(Control control, LayoutPersistence caretaker) {
    if (control is Window window && window.GetValue(PersistWindowProperty))
      return new WindowLayoutOriginator(window);

    if (control.GetValue(BandProperty) is LayoutBand band)
      return CreateBand(control, band);

    if (control is not DataGrid grid)
      return null;

    if (grid.GetValue(ResourceTableProperty)) {
      return new DataGridLayoutOriginator(
        grid,
        caretaker.ContextName,
        caretaker.ResourceTableKey,
        caretaker.Layout);
    }

    var key = grid.GetValue(TableProperty);

    if (string.IsNullOrWhiteSpace(key))
      return null;

    return new DataGridLayoutOriginator(
      grid,
      caretaker.ContextName,
      () => key,
      caretaker.Layout);
  }

  private static ILayoutOriginator? CreateBand(Control control, LayoutBand band) {
    var targetSelf = control is Grid && band.Index >= 0;
    var grid = targetSelf ? (Grid)control : control.Parent as Grid;

    if (grid is null)
      return null;

    var index = band.Index >= 0
      ? band.Index
      : band.Axis == LayoutAxis.Row ? Grid.GetRow(control) : Grid.GetColumn(control);
    var prefer = targetSelf ? null : control;
    var (read, write) = Bind(band.Slot);

    if (band.Axis == LayoutAxis.Row) {
      return GridBandOriginator.Row(
        grid, index, band.Min, band.Max, band.Fallback, read, write, band.MinHostExtent);
    }

    return GridBandOriginator.Column(
      grid, index, band.Min, band.Max, band.Fallback, read, write, prefer);
  }

  private static (Func<LayoutSettings, double> Read, Action<LayoutSettings, double> Write) Bind(LayoutSlot slot) =>
    slot switch {
      LayoutSlot.CatalogWidth => (static layout => layout.CatalogWidth, static (layout, value) => layout.CatalogWidth = value),
      LayoutSlot.NavigatorWidth => (static layout => layout.NavigatorWidth, static (layout, value) => layout.NavigatorWidth = value),
      LayoutSlot.DetailsWidth => (static layout => layout.DetailsWidth, static (layout, value) => layout.DetailsWidth = value),
      LayoutSlot.HelmHistoryHeight => (static layout => layout.HelmHistoryHeight, static (layout, value) => layout.HelmHistoryHeight = value),
      _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

  private sealed class Noop : IDisposable {
    public static readonly Noop Instance = new();

    public void Dispose() {
    }
  }
}
