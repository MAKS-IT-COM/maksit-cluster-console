using Avalonia;
using Avalonia.Controls;


namespace MaksIT.ClusterConsole.UI.Controls;

public static class DataGridColumns {
  public static readonly AttachedProperty<bool> IndependentResizeProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, bool>("IndependentResize", typeof(DataGridColumns));

  public static bool GetIndependentResize(DataGrid grid) =>
    grid.GetValue(IndependentResizeProperty);

  public static void SetIndependentResize(DataGrid grid, bool value) =>
    grid.SetValue(IndependentResizeProperty, value);

  static DataGridColumns() {
    IndependentResizeProperty.Changed.AddClassHandler<DataGrid>(OnIndependentResizeChanged);
  }

  private static void OnIndependentResizeChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e) {
    grid.LayoutUpdated -= OnLayoutUpdated;
    if (e.GetNewValue<bool>())
      grid.LayoutUpdated += OnLayoutUpdated;
  }

  private static void OnLayoutUpdated(object? sender, EventArgs e) {
    if (sender is not DataGrid grid)
      return;

    foreach (var column in grid.Columns) {
      if (column.Width.UnitType == DataGridLengthUnitType.Pixel || column.ActualWidth <= 0)
        continue;
      column.Width = new DataGridLength(column.ActualWidth, DataGridLengthUnitType.Pixel);
    }
  }
}
