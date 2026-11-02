using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;


namespace MaksIT.ClusterConsole.UI.Controls;

public sealed class BubblesScreenSaverControl : Control {
  private const double RefWidth = 1920;
  private const double RefHeight = 1080;
  private const double RefArea = RefWidth * RefHeight;
  private const double RefBubbleCount = 30;
  private const double RefMinRadius = 28;
  private const double RefMaxRadius = 78;
  private const double FrameSeconds = 1.0 / 60.0;
  private const double EdgeOverhang = 0.55;
  private const double SweepDeadlineSeconds = 55;
  private const double SpeedSpread = 3.2;
  private const double MaxSteerRadians = 0.028;

  private static readonly Color[] ThemePalette = [
    Color.Parse("#33A5CF"),
    Color.Parse("#006199"),
    Color.Parse("#8ab4f8"),
    Color.Parse("#c5d9fb"),
    Color.Parse("#00a7a0"),
    Color.Parse("#5ec8e8"),
    Color.Parse("#4a90c8"),
    Color.Parse("#E8F4FC"),
    Color.Parse("#7eb8d4"),
    Color.Parse("#3d7ea8")
  ];

  private readonly List<Bubble> _bubbles = [];
  private readonly DispatcherTimer _timer;
  private readonly Random _random = new();
  private readonly List<int> _coldCellOrder = [];
  private double[] _lastVisit = [];
  private int _coverCols;
  private int _coverRows;
  private Size _lastSize;
  private double _scale = 1;
  private double _time;
  private bool _running;

  public BubblesScreenSaverControl() {
    IsHitTestVisible = false;
    ClipToBounds = true;
    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    _timer.Tick += (_, _) => {
      Step();
      InvalidateVisual();
    };
  }

  public void Start() {
    if (_running)
      return;

    RebuildBubbles(force: true);
    _time = 0;
    _running = true;
    IsVisible = true;
    IsHitTestVisible = true;
    _timer.Start();
  }

  public void Stop() {
    if (!_running)
      return;

    _running = false;
    _timer.Stop();
    IsHitTestVisible = false;
    IsVisible = false;
    _bubbles.Clear();
    _lastVisit = [];
  }

  public override void Render(DrawingContext context) {
    if (Bounds.Width <= 0 || Bounds.Height <= 0)
      return;

    foreach (var bubble in _bubbles)
      DrawBubble(context, bubble);
  }

  protected override void OnSizeChanged(SizeChangedEventArgs e) {
    base.OnSizeChanged(e);
    _lastSize = e.NewSize;

    if (_running)
      RebuildBubbles(force: false);
  }

  private void RebuildBubbles(bool force) {
    var size = CurrentSize();

    if (size.Width <= 0 || size.Height <= 0)
      return;

    var scale = Math.Clamp(Math.Sqrt((size.Width * size.Height) / RefArea), 0.55, 2.4);

    if (!force && _bubbles.Count > 0 && Math.Abs(scale - _scale) < 0.08) {
      EnsureCoverageGrid(size);

      return;
    }

    _scale = scale;
    _bubbles.Clear();
    EnsureCoverageGrid(size);

    var count = (int)Math.Clamp(Math.Round(RefBubbleCount * (size.Width * size.Height) / RefArea), 16, 56);
    PlaceBubbles(size, count);
    _bubbles.Sort((a, b) => a.Z.CompareTo(b.Z));

    foreach (var bubble in _bubbles)
      MarkCoverage(size, bubble);
  }

  private void EnsureCoverageGrid(Size size) {
    var cell = Math.Max(28, RefMaxRadius * _scale * 0.5);
    var cols = Math.Max(8, (int)Math.Ceiling(size.Width / cell));
    var rows = Math.Max(6, (int)Math.Ceiling(size.Height / cell));

    if (_lastVisit.Length == cols * rows && _coverCols == cols && _coverRows == rows)
      return;

    _coverCols = cols;
    _coverRows = rows;
    _lastVisit = new double[cols * rows];

    for (var i = 0; i < _lastVisit.Length; i++)
      _lastVisit[i] = -SweepDeadlineSeconds;
  }

  private void PlaceBubbles(Size size, int count) {
    var minR = RefMinRadius * _scale;
    var maxR = RefMaxRadius * _scale;
    var cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * size.Width / Math.Max(1, size.Height))));
    var rows = Math.Max(1, (int)Math.Ceiling(count / (double)cols));

    while (rows * cols < count) {
      if (size.Width >= size.Height)
        cols++;
      else
        rows++;
    }

    var meanR = (minR + maxR) * 0.5;
    var margin = meanR * EdgeOverhang;
    var spanX = size.Width + margin * 2;
    var spanY = size.Height + margin * 2;
    var cellW = spanX / cols;
    var cellH = spanY / rows;
    var index = 0;

    for (var row = 0; row < rows && index < count; row++) {
      for (var col = 0; col < cols && index < count; col++) {
        var radius = minR + _random.NextDouble() * (maxR - minR);
        var x = -margin + (col + 0.5) * cellW + (_random.NextDouble() - 0.5) * cellW * 0.5;
        var y = -margin + (row + 0.5) * cellH + (_random.NextDouble() - 0.5) * cellH * 0.5;
        var dx = (_random.NextDouble() - 0.5) * SpeedSpread * _scale;
        var dy = (_random.NextDouble() - 0.5) * SpeedSpread * _scale;

        if (Math.Abs(dx) < 0.35 * _scale)
          dx = 0.35 * _scale * (_random.Next(2) == 0 ? 1 : -1);

        if (Math.Abs(dy) < 0.35 * _scale)
          dy = 0.35 * _scale * (_random.Next(2) == 0 ? 1 : -1);

        var bubble = new Bubble {
          X = x,
          Y = y,
          Radius = radius,
          Vx = dx,
          Vy = dy,
          Color = RandomBubbleColor(),
          Z = _random.NextDouble()
        };
        ClampToBounds(bubble, size);
        _bubbles.Add(bubble);
        index++;
      }
    }
  }

  private Color RandomBubbleColor() =>
    ThemePalette[_random.Next(ThemePalette.Length)];

  private static (double MinX, double MaxX, double MinY, double MaxY) BoundsFor(Size size, double radius) {
    var margin = radius * EdgeOverhang;

    return (-margin, size.Width + margin, -margin, size.Height + margin);
  }

  private void Step() {
    var size = CurrentSize();

    if (size.Width <= 0 || size.Height <= 0)
      return;

    EnsureCoverageGrid(size);
    _time += FrameSeconds;
    NudgeHeadingsTowardStaleCells(size);

    foreach (var bubble in _bubbles) {
      bubble.X += bubble.Vx;
      bubble.Y += bubble.Vy;
      BounceWalls(bubble, size);
      MarkCoverage(size, bubble);
    }
  }

  private void NudgeHeadingsTowardStaleCells(Size size) {
    if (_bubbles.Count == 0 || _lastVisit.Length == 0)
      return;

    _coldCellOrder.Clear();

    for (var i = 0; i < _lastVisit.Length; i++)
      _coldCellOrder.Add(i);

    _coldCellOrder.Sort((a, b) => _lastVisit[a].CompareTo(_lastVisit[b]));

    var targets = Math.Min(_bubbles.Count / 3, Math.Max(3, _coverCols / 2));
    var assigned = new bool[_bubbles.Count];
    var cellW = size.Width / _coverCols;
    var cellH = size.Height / _coverRows;

    for (var t = 0; t < targets && t < _coldCellOrder.Count; t++) {
      var cell = _coldCellOrder[t];
      var age = _time - _lastVisit[cell];

      if (age < SweepDeadlineSeconds * 0.45 && _time > 3)
        break;

      var col = cell % _coverCols;
      var row = cell / _coverCols;
      var tx = (col + 0.5) * cellW;
      var ty = (row + 0.5) * cellH;
      var best = -1;
      var bestDist = double.MaxValue;

      for (var i = 0; i < _bubbles.Count; i++) {
        if (assigned[i])
          continue;

        var b = _bubbles[i];
        var dx = tx - b.X;
        var dy = ty - b.Y;
        var dist = dx * dx + dy * dy;

        if (dist < bestDist) {
          bestDist = dist;
          best = i;
        }
      }

      if (best < 0)
        break;

      assigned[best] = true;
      var bubble = _bubbles[best];
      var speed = Math.Sqrt(bubble.Vx * bubble.Vx + bubble.Vy * bubble.Vy);

      if (speed < 0.001)
        continue;

      var current = Math.Atan2(bubble.Vy, bubble.Vx);
      var target = Math.Atan2(ty - bubble.Y, tx - bubble.X);
      var delta = NormalizeAngle(target - current);
      var urgency = Math.Clamp(age / SweepDeadlineSeconds, 0.35, 1.25);
      var turn = Math.Clamp(delta, -MaxSteerRadians * urgency, MaxSteerRadians * urgency);
      var heading = current + turn;
      bubble.Vx = Math.Cos(heading) * speed;
      bubble.Vy = Math.Sin(heading) * speed;
    }
  }

  private static double NormalizeAngle(double radians) {
    while (radians > Math.PI)
      radians -= Math.PI * 2;

    while (radians < -Math.PI)
      radians += Math.PI * 2;

    return radians;
  }

  private void MarkCoverage(Size size, Bubble bubble) {
    if (_lastVisit.Length == 0 || size.Width <= 0 || size.Height <= 0)
      return;

    var cellW = size.Width / _coverCols;
    var cellH = size.Height / _coverRows;
    var r = bubble.Radius * 0.95;
    var minCol = Math.Max(0, (int)Math.Floor((bubble.X - r) / cellW));
    var maxCol = Math.Min(_coverCols - 1, (int)Math.Floor((bubble.X + r) / cellW));
    var minRow = Math.Max(0, (int)Math.Floor((bubble.Y - r) / cellH));
    var maxRow = Math.Min(_coverRows - 1, (int)Math.Floor((bubble.Y + r) / cellH));
    var rSq = r * r;

    for (var row = minRow; row <= maxRow; row++) {
      for (var col = minCol; col <= maxCol; col++) {
        var cx = (col + 0.5) * cellW;
        var cy = (row + 0.5) * cellH;
        var dx = cx - bubble.X;
        var dy = cy - bubble.Y;

        if (dx * dx + dy * dy <= rSq)
          _lastVisit[row * _coverCols + col] = _time;
      }
    }
  }

  private void BounceWalls(Bubble bubble, Size size) {
    var (minX, maxX, minY, maxY) = BoundsFor(size, bubble.Radius);

    if (bubble.X < minX || bubble.X > maxX) {
      bubble.Vx = -bubble.Vx;
      bubble.X = Math.Clamp(bubble.X, minX, maxX);
    }

    if (bubble.Y < minY || bubble.Y > maxY) {
      bubble.Vy = -bubble.Vy;
      bubble.Y = Math.Clamp(bubble.Y, minY, maxY);
    }
  }

  private static void ClampToBounds(Bubble bubble, Size size) {
    var (minX, maxX, minY, maxY) = BoundsFor(size, bubble.Radius);
    bubble.X = Math.Clamp(bubble.X, minX, Math.Max(minX, maxX));
    bubble.Y = Math.Clamp(bubble.Y, minY, Math.Max(minY, maxY));
  }

  private static void DrawBubble(DrawingContext context, Bubble bubble) {
    var x = bubble.X;
    var y = bubble.Y;
    var r = bubble.Radius;
    var color = bubble.Color;
    var clear = Color.FromArgb(0, color.R, color.G, color.B);
    var fill = CreateBubbleGradient(x, y, r, color, clear);

    var body = new EllipseGeometry(new Rect(x - r, y - r, r * 2, r * 2));
    context.DrawGeometry(fill, null, body);
    DrawRotatedEllipse(
      context,
      x - r / 2 - r * 0.05,
      y - r / 2 - r * 0.15,
      r / 20,
      r / 6,
      Math.PI / 3.5,
      Brushes.White);
  }

  private static RadialGradientBrush CreateBubbleGradient(
    double x,
    double y,
    double radius,
    Color color,
    Color clear) =>
    new() {
      Center = new RelativePoint(x, y, RelativeUnit.Absolute),
      GradientOrigin = new RelativePoint(x, y, RelativeUnit.Absolute),
      RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
      RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
      GradientStops = [
        new GradientStop(clear, 0),
        new GradientStop(clear, 0.7),
        new GradientStop(color, 1)
      ]
    };

  private static void DrawRotatedEllipse(
    DrawingContext context,
    double cx,
    double cy,
    double rx,
    double ry,
    double radians,
    IBrush brush) {
    var transform = Matrix.CreateTranslation(-cx, -cy)
      * Matrix.CreateRotation(radians)
      * Matrix.CreateTranslation(cx, cy);

    using (context.PushTransform(transform)) {
      var geometry = new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));
      context.DrawGeometry(brush, null, geometry);
    }
  }

  private Size CurrentSize() {
    var size = Bounds.Size;

    if (size.Width <= 0 || size.Height <= 0)
      size = _lastSize;

    return size;
  }

  private sealed class Bubble {
    public double X;
    public double Y;
    public double Radius;
    public double Vx;
    public double Vy;
    public Color Color;
    public double Z;
  }
}
