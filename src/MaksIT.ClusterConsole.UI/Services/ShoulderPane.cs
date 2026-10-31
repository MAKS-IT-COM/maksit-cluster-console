using Avalonia;
using Avalonia.Controls;


namespace MaksIT.ClusterConsole.UI.Services;

/// <summary>
/// Shrinks a grid column to a rail while the right shoulder is collapsed, and restores the previous width.
/// </summary>
public static class ShoulderPane {
  public const double RailWidth = 36;

  public static readonly AttachedProperty<bool> CollapsedProperty =
    AvaloniaProperty.RegisterAttached<Grid, bool>("Collapsed", typeof(ShoulderPane));

  public static readonly AttachedProperty<int> ContentColumnProperty =
    AvaloniaProperty.RegisterAttached<Grid, int>("ContentColumn", typeof(ShoulderPane), 2);

  public static readonly AttachedProperty<int> SplitterColumnProperty =
    AvaloniaProperty.RegisterAttached<Grid, int>("SplitterColumn", typeof(ShoulderPane), 1);

  public static readonly AttachedProperty<double> CollapsedWidthProperty =
    AvaloniaProperty.RegisterAttached<Grid, double>("CollapsedWidth", typeof(ShoulderPane), RailWidth);

  public static readonly AttachedProperty<double> ExpandedFallbackProperty =
    AvaloniaProperty.RegisterAttached<Grid, double>("ExpandedFallback", typeof(ShoulderPane), 248);

  private static readonly AttachedProperty<double> ExpandedWidthProperty =
    AvaloniaProperty.RegisterAttached<Grid, double>("ExpandedWidth", typeof(ShoulderPane));

  private static readonly AttachedProperty<bool> WatchingProperty =
    AvaloniaProperty.RegisterAttached<Grid, bool>("Watching", typeof(ShoulderPane));

  static ShoulderPane() {
    CollapsedProperty.Changed.AddClassHandler<Grid>((grid, _) => Apply(grid));
    CollapsedWidthProperty.Changed.AddClassHandler<Grid>((grid, _) => Apply(grid));
  }

  public static bool GetCollapsed(Grid grid) =>
    grid.GetValue(CollapsedProperty);

  public static void SetCollapsed(Grid grid, bool value) =>
    grid.SetValue(CollapsedProperty, value);

  public static int GetContentColumn(Grid grid) =>
    grid.GetValue(ContentColumnProperty);

  public static void SetContentColumn(Grid grid, int value) =>
    grid.SetValue(ContentColumnProperty, value);

  public static int GetSplitterColumn(Grid grid) =>
    grid.GetValue(SplitterColumnProperty);

  public static void SetSplitterColumn(Grid grid, int value) =>
    grid.SetValue(SplitterColumnProperty, value);

  public static double GetCollapsedWidth(Grid grid) =>
    grid.GetValue(CollapsedWidthProperty);

  public static void SetCollapsedWidth(Grid grid, double value) =>
    grid.SetValue(CollapsedWidthProperty, value);

  public static double GetExpandedFallback(Grid grid) =>
    grid.GetValue(ExpandedFallbackProperty);

  public static void SetExpandedFallback(Grid grid, double value) =>
    grid.SetValue(ExpandedFallbackProperty, value);

  /// <summary>
  /// Remembers the expanded column width from layout settings while the rail is showing.
  /// </summary>
  public static void SeedExpandedWidth(Grid grid, double width) {
    if (width < 180)
      return;

    grid.SetValue(ExpandedWidthProperty, width);
  }

  private static void Apply(Grid grid) {
    if (grid.ColumnDefinitions.Count == 0) {
      grid.AttachedToVisualTree -= OnAttached;
      grid.AttachedToVisualTree += OnAttached;

      return;
    }

    Watch(grid);
    var content = Column(grid, grid.GetValue(ContentColumnProperty));

    if (content is null)
      return;

    var splitter = Column(grid, grid.GetValue(SplitterColumnProperty));
    var rail = Rail(grid);

    if (!grid.GetValue(CollapsedProperty)) {
      var expanded = grid.GetValue(ExpandedWidthProperty);

      if (expanded < 180 && content.Width.IsAbsolute && content.Width.Value > rail + 1)
        expanded = content.Width.Value;

      if (expanded < 180)
        expanded = Fallback(grid);

      content.Width = new GridLength(expanded);

      if (splitter is not null)
        splitter.Width = new GridLength(4);

      return;
    }

    if (content.Width.IsAbsolute && content.Width.Value > rail + 1)
      grid.SetValue(ExpandedWidthProperty, content.Width.Value);

    content.Width = new GridLength(rail);

    if (splitter is not null)
      splitter.Width = new GridLength(0);
  }

  private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) {
    if (sender is Grid grid)
      Apply(grid);
  }

  private static void Watch(Grid grid) {
    if (grid.GetValue(WatchingProperty))
      return;

    grid.SetValue(WatchingProperty, true);
    var content = Column(grid, grid.GetValue(ContentColumnProperty));

    if (content is null)
      return;

    content.PropertyChanged += (_, e) => {
      if (e.Property != ColumnDefinition.WidthProperty || !grid.GetValue(CollapsedProperty))
        return;

      var rail = Rail(grid);

      if (content.Width.IsAbsolute && Math.Abs(content.Width.Value - rail) < 0.5)
        return;

      if (content.Width.IsAbsolute && content.Width.Value > rail + 1)
        grid.SetValue(ExpandedWidthProperty, content.Width.Value);

      content.Width = new GridLength(rail);
    };
  }

  private static double Rail(Grid grid) {
    var width = grid.GetValue(CollapsedWidthProperty);

    return width > 0 ? width : RailWidth;
  }

  private static double Fallback(Grid grid) {
    var width = grid.GetValue(ExpandedFallbackProperty);

    return width >= 180 ? width : 248;
  }

  private static ColumnDefinition? Column(Grid grid, int index) =>
    index >= 0 && index < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[index] : null;
}
