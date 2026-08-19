using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;


namespace MaksIT.ClusterConsole.UI.Controls;

public sealed class ResourceBar : Control {
  public static readonly StyledProperty<double> UsedProperty =
    AvaloniaProperty.Register<ResourceBar, double>(nameof(Used));

  public static readonly StyledProperty<double> RequestsProperty =
    AvaloniaProperty.Register<ResourceBar, double>(nameof(Requests));

  public static readonly StyledProperty<double> LimitsProperty =
    AvaloniaProperty.Register<ResourceBar, double>(nameof(Limits));

  public static readonly StyledProperty<double> AllocatableProperty =
    AvaloniaProperty.Register<ResourceBar, double>(nameof(Allocatable));

  public static readonly StyledProperty<double> CapacityProperty =
    AvaloniaProperty.Register<ResourceBar, double>(nameof(Capacity));

  public static readonly StyledProperty<bool> ShowRequestsProperty =
    AvaloniaProperty.Register<ResourceBar, bool>(nameof(ShowRequests), true);

  static ResourceBar() {
    AffectsRender<ResourceBar>(
      UsedProperty, RequestsProperty, LimitsProperty, AllocatableProperty, CapacityProperty, ShowRequestsProperty);
  }

  public double Used {
    get => GetValue(UsedProperty);
    set => SetValue(UsedProperty, value);
  }

  public double Requests {
    get => GetValue(RequestsProperty);
    set => SetValue(RequestsProperty, value);
  }

  public double Limits {
    get => GetValue(LimitsProperty);
    set => SetValue(LimitsProperty, value);
  }

  public double Allocatable {
    get => GetValue(AllocatableProperty);
    set => SetValue(AllocatableProperty, value);
  }

  public double Capacity {
    get => GetValue(CapacityProperty);
    set => SetValue(CapacityProperty, value);
  }

  public bool ShowRequests {
    get => GetValue(ShowRequestsProperty);
    set => SetValue(ShowRequestsProperty, value);
  }

  protected override Size MeasureOverride(Size availableSize) {
    var width = double.IsInfinity(availableSize.Width) ? 200 : Math.Max(0, availableSize.Width);
    var height = double.IsInfinity(availableSize.Height) ? 20 : Math.Max(0, availableSize.Height);
    return new Size(width, height);
  }

  public override void Render(DrawingContext context) {
    var bounds = new Rect(Bounds.Size);
    var track = new Rect(0, 4, Math.Max(1, bounds.Width), Math.Max(8, bounds.Height - 8));
    context.FillRectangle(new SolidColorBrush(Color.Parse("#1a1d20")), track, 3);

    var scale = Math.Max(1, new[] { Used, Requests, Limits, Allocatable, Capacity }.Max());
    Rect Bar(double value, double top, double height) =>
      new(0, top, track.Width * Math.Clamp(value / scale, 0, 1), height);

    context.FillRectangle(new SolidColorBrush(Color.Parse("#3f454c")), Bar(Capacity, track.Y, track.Height), 3);
    context.FillRectangle(new SolidColorBrush(Color.Parse("#6b7280")), Bar(Allocatable, track.Y + 1, track.Height - 2), 3);
    if (ShowRequests) {
      context.FillRectangle(new SolidColorBrush(Color.Parse("#66ff4d6d")), Bar(Limits, track.Y + 3, track.Height - 6), 2);
      context.FillRectangle(new SolidColorBrush(Color.Parse("#ccff9f0a")), Bar(Requests, track.Y + 4, track.Height - 8), 2);
    }

    context.FillRectangle(new SolidColorBrush(Color.Parse("#00a7a0")), Bar(Used, track.Y + 5, track.Height - 10), 2);

    if (ShowRequests && Limits > Capacity && Capacity > 0) {
      var x = track.Width * (Capacity / scale);
      context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#ff8a80")), 1), new Point(x, 0), new Point(x, bounds.Height));
    }
  }
}
