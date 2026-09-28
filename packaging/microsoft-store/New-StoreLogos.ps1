# Renders Microsoft Store logo PNGs from the Cluster Console icon geometry.
# Requires Windows PowerShell 5.1 (System.Drawing). Output: packaging/microsoft-store/logos/

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "logos"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

public static class StoreLogos {
  static readonly Color Bg0 = Color.FromArgb(0x33, 0xA5, 0xCF);
  static readonly Color Bg1 = Color.FromArgb(0x00, 0x2A, 0x6A);
  static readonly Color Tile0 = Color.White;
  static readonly Color Tile1 = Color.FromArgb(0xC5, 0xE8, 0xF6);
  static readonly Color Ink = Color.FromArgb(0x00, 0x2A, 0x6A);

  public static void SaveAll(string dir) {
    SaveIcon(Path.Combine(dir, "box-1x1-2160x2160.png"), 2160);
    SaveIcon(Path.Combine(dir, "box-1x1-1080x1080.png"), 1080);
    SaveIcon(Path.Combine(dir, "tile-1x1-300x300.png"), 300);
    SaveIcon(Path.Combine(dir, "tile-1x1-150x150.png"), 150);
    SaveIcon(Path.Combine(dir, "tile-1x1-71x71.png"), 71);
    SavePoster(Path.Combine(dir, "poster-9x16-1440x2160.png"), 1440, 2160);
    SavePoster(Path.Combine(dir, "poster-9x16-720x1080.png"), 720, 1080);
  }

  static void SaveIcon(string path, int size) {
    using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
    using (var g = Graphics.FromImage(bmp)) {
      Quality(g);
      DrawBackground(g, size, size);
      DrawMark(g, 0f, 0f, size / 256f, true);
      bmp.Save(path, ImageFormat.Png);
    }
  }

  static void SavePoster(string path, int w, int h) {
    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
    using (var g = Graphics.FromImage(bmp)) {
      Quality(g);
      DrawBackground(g, w, h);
      float scale = 0.62f * w / 184f;
      float markH = 148f * scale;
      using (var font = FitFont(g, "Cluster Console", w * 0.84f, w * 0.072f)) {
        SizeF text = g.MeasureString("Cluster Console", font);
        float gap = h * 0.045f;
        float block = markH + gap + text.Height;
        float top = (h - block) / 2f;
        float ox = (w / 2f) - (128f * scale);
        float oy = (top + markH / 2f) - (124f * scale);
        DrawMark(g, ox, oy, scale, false);
        using (var brush = new SolidBrush(Color.White))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near }) {
          g.DrawString("Cluster Console", font, brush, new RectangleF(0f, top + markH + gap, w, text.Height + 4f), fmt);
        }
      }
      bmp.Save(path, ImageFormat.Png);
    }
  }

  static void DrawBackground(Graphics g, int w, int h) {
    using (var brush = new LinearGradientBrush(
      new PointF(w * 0.2f, 0f),
      new PointF(w * 0.9f, h),
      Bg0,
      Bg1)) {
      g.FillRectangle(brush, 0, 0, w, h);
    }
  }

  static void DrawMark(Graphics g, float ox, float oy, float s, bool shine) {
    if (shine) {
      using (var brush = new SolidBrush(Color.FromArgb(41, 255, 255, 255))) {
        float rx = 150f * s;
        float ry = 88f * s;
        g.FillEllipse(brush, ox + (128f * s) - rx, oy + (8f * s) - ry, rx * 2f, ry * 2f);
      }
    }
    Tile(g, ox, oy, s, 36f, 50f);
    Tile(g, ox, oy, s, 132f, 50f);
    Tile(g, ox, oy, s, 84f, 110f);
    using (var pen = new Pen(Ink, 14f * s)) {
      pen.StartCap = LineCap.Round;
      pen.EndCap = LineCap.Round;
      pen.LineJoin = LineJoin.Round;
      g.DrawLines(pen, new[] {
        new PointF(ox + 112f * s, oy + 134f * s),
        new PointF(ox + 152f * s, oy + 154f * s),
        new PointF(ox + 112f * s, oy + 174f * s)
      });
    }
  }

  static void Tile(Graphics g, float ox, float oy, float s, float x, float y) {
    float rx = ox + x * s;
    float ry = oy + y * s;
    float side = 88f * s;
    float radius = 20f * s;
    using (var path = RoundedRect(rx, ry, side, side, radius))
    using (var brush = new LinearGradientBrush(
      new PointF(rx, ry),
      new PointF(rx + side, ry + side),
      Tile0,
      Tile1)) {
      g.FillPath(brush, path);
    }
  }

  static GraphicsPath RoundedRect(float x, float y, float w, float h, float r) {
    float d = r * 2f;
    var path = new GraphicsPath();
    path.AddArc(x, y, d, d, 180f, 90f);
    path.AddArc(x + w - d, y, d, d, 270f, 90f);
    path.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
    path.AddArc(x, y + h - d, d, d, 90f, 90f);
    path.CloseFigure();
    return path;
  }

  static Font FitFont(Graphics g, string text, float maxWidth, float startPx) {
    float size = startPx;
    Font font = null;
    while (size > 12f) {
      if (font != null) font.Dispose();
      font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
      if (g.MeasureString(text, font).Width <= maxWidth) return font;
      size -= 1f;
    }
    return font;
  }

  static void Quality(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
  }
}
"@

[StoreLogos]::SaveAll($outDir)

Add-Type -AssemblyName System.Drawing
Get-ChildItem $outDir -Filter *.png | ForEach-Object {
  $img = [System.Drawing.Image]::FromFile($_.FullName)
  "{0}`t{1}x{2}`t{3:N0} KB" -f $_.Name, $img.Width, $img.Height, ($_.Length / 1KB)
  $img.Dispose()
}
