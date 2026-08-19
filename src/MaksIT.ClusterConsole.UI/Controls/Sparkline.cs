using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;


namespace MaksIT.ClusterConsole.UI.Controls;

public sealed class Sparkline : Control {
  public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
    AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

  public static readonly StyledProperty<IBrush> LineBrushProperty =
    AvaloniaProperty.Register<Sparkline, IBrush>(nameof(LineBrush), new SolidColorBrush(Color.Parse("#00a7a0")));

  static Sparkline() {
    AffectsRender<Sparkline>(ValuesProperty, LineBrushProperty);
  }

  public Sparkline() {
    ClipToBounds = true;
  }

  public IReadOnlyList<double>? Values {
    get => GetValue(ValuesProperty);
    set => SetValue(ValuesProperty, value);
  }

  public IBrush LineBrush {
    get => GetValue(LineBrushProperty);
    set => SetValue(LineBrushProperty, value);
  }

  protected override Size MeasureOverride(Size availableSize) {
    var width = double.IsInfinity(availableSize.Width) ? 200 : Math.Max(0, availableSize.Width);
    var height = double.IsInfinity(availableSize.Height) ? 80 : Math.Max(0, availableSize.Height);
    return new Size(width, height);
  }

  public override void Render(DrawingContext context) {
    var values = Values;
    var bounds = new Rect(Bounds.Size);
    context.FillRectangle(new SolidColorBrush(Color.Parse("#1a1d20")), bounds);
    if (values is null || values.Count == 0)
      return;

    var w = Math.Max(1, bounds.Width - 8);
    var h = Math.Max(1, bounds.Height - 8);
    var origin = new Point(4, 4);
    var max = Math.Max(1, values.Max());
    var step = values.Count == 1 ? 0 : w / (values.Count - 1);

    Point PointAt(int i) {
      var y = origin.Y + h - h * (values[i] / max);
      return new Point(origin.X + step * i, y);
    }

    var fill = new StreamGeometry();
    using (var ctx = fill.Open()) {
      ctx.BeginFigure(new Point(origin.X, origin.Y + h), isFilled: true);
      for (var i = 0; i < values.Count; i++)
        ctx.LineTo(PointAt(i));
      ctx.LineTo(new Point(PointAt(values.Count - 1).X, origin.Y + h));
      ctx.EndFigure(true);
    }

    if (LineBrush is ISolidColorBrush solid) {
      var c = solid.Color;
      context.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, c.R, c.G, c.B)), null, fill);
    }

    var line = new StreamGeometry();
    using (var ctx = line.Open()) {
      ctx.BeginFigure(PointAt(0), isFilled: false);
      for (var i = 1; i < values.Count; i++)
        ctx.LineTo(PointAt(i));
      ctx.EndFigure(false);
    }

    context.DrawGeometry(null, new Pen(LineBrush, 1.5), line);
  }
}
