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

  private static readonly AttachedProperty<double> ExpandedWidthProperty =
    AvaloniaProperty.RegisterAttached<Grid, double>("ExpandedWidth", typeof(ShoulderPane));

  private static readonly AttachedProperty<bool> WatchingProperty =
    AvaloniaProperty.RegisterAttached<Grid, bool>("Watching", typeof(ShoulderPane));

  static ShoulderPane() {
    CollapsedProperty.Changed.AddClassHandler<Grid>((grid, _) => Apply(grid));
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

    if (!grid.GetValue(CollapsedProperty)) {
      var expanded = grid.GetValue(ExpandedWidthProperty);

      if (expanded < 180)
        return;

      content.Width = new GridLength(expanded);

      if (splitter is not null)
        splitter.Width = new GridLength(4);

      return;
    }

    if (content.Width.IsAbsolute && content.Width.Value > RailWidth + 1)
      grid.SetValue(ExpandedWidthProperty, content.Width.Value);

    content.Width = new GridLength(RailWidth);

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

      if (content.Width.IsAbsolute && Math.Abs(content.Width.Value - RailWidth) < 0.5)
        return;

      if (content.Width.IsAbsolute && content.Width.Value > RailWidth + 1)
        grid.SetValue(ExpandedWidthProperty, content.Width.Value);

      content.Width = new GridLength(RailWidth);
    };
  }

  private static ColumnDefinition? Column(Grid grid, int index) =>
    index >= 0 && index < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[index] : null;
}
