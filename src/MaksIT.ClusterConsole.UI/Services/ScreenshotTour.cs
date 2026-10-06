using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Windows;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;


namespace MaksIT.ClusterConsole.UI.Services;

internal static class ScreenshotTour {
  private static readonly JsonSerializerOptions Json = new() {
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  public static async Task RunAsync(Window window, MainViewModel model, ScreenshotTourOptions options, Action<int> shutdown) {
    var manifest = new Manifest();
    var code = 0;

    try {
      Directory.CreateDirectory(options.Directory);
      var connected = false;

      foreach (var view in options.Views) {
        if (string.Equals(view, ScreenshotTourOptions.WelcomeView, StringComparison.OrdinalIgnoreCase)) {
          await SettleAsync(options.SettleMilliseconds);
          manifest.Shots.Add(Capture(window, options, ScreenshotTourOptions.WelcomeView, manifest, saveCatalog: !connected));

          continue;
        }

        if (!await EnsureConnectedAsync(window, model, options, manifest)) {
          code = 1;
          connected = false;

          break;
        }

        connected = true;

        if (!await CaptureNavAsync(window, model, options, manifest, view))
          manifest.Skipped.Add(new SkippedRecord { Id = view, Reason = "Not in the navigator." });
      }

      if (options.AllFeatures && (connected || await EnsureConnectedAsync(window, model, options, manifest)))
        await CaptureAllFeaturesAsync(window, model, options, manifest);
      else if (options.AllFeatures && manifest.Error is not null)
        code = 1;
    }
    catch (Exception ex) {
      manifest.Error = ex.Message;
      code = 1;
    }

    try {
      var path = Path.Combine(options.Directory, "manifest.json");
      await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, Json) + Environment.NewLine);
    }
    catch (Exception ex) {
      manifest.Error ??= ex.Message;
      code = 1;
    }

    shutdown(code);
  }

  private static async Task<bool> ShowAsync(ClusterPageViewModel page, string id, int settleMilliseconds) {
    page.PausePolling();
    await page.WaitUntilQuietAsync(20_000);

    if (!page.TrySelectNav(id))
      return false;

    await page.WaitUntilQuietAsync(20_000);
    await Dispatcher.UIThread.InvokeAsync(() => {
      if (page.IsResourceTable)
        page.ShoulderCollapsed = false;

      page.SelectedTab = DetailTab.Overview;
      page.EnsureRowSelected();
    });
    await page.WaitUntilQuietAsync(20_000);
    await SettleAsync(settleMilliseconds);

    return true;
  }

  private static async Task<bool> EnsureConnectedAsync(
    Window window,
    MainViewModel model,
    ScreenshotTourOptions options,
    Manifest manifest) {
    if (manifest.Context is not null)
      return true;

    var context = await model.ConnectForCaptureAsync(options.Context);

    if (context is null) {
      manifest.Error = string.IsNullOrWhiteSpace(options.Context)
        ? "No kubeconfig context to open."
        : $"Context '{options.Context}' is not in kubeconfig or did not connect.";

      return false;
    }

    manifest.Context = context;
    model.ActivePage?.PausePolling();
    await SaveCatalogAsync(window, options, manifest);

    return true;
  }

  private static async Task CaptureAllFeaturesAsync(
    Window window,
    MainViewModel model,
    ScreenshotTourOptions options,
    Manifest manifest) {
    await CaptureToolWindowAsync(window, options, manifest, "connections", new ConnectionsWindow(model.CreateConnectionsViewModel()));
    await CaptureToolWindowAsync(window, options, manifest, "ai-settings", new AiSettingsWindow(model.CreateAiSettingsViewModel()));

    var page = model.ActivePage;

    if (page is null)
      return;

    foreach (var id in page.CaptureNavIds()) {
      if (!await CaptureNavAsync(window, model, options, manifest, id))
        manifest.Skipped.Add(new SkippedRecord { Id = id, Reason = "Not in the navigator." });
    }
  }

  private static async Task<bool> CaptureNavAsync(
    Window window,
    MainViewModel model,
    ScreenshotTourOptions options,
    Manifest manifest,
    string id) {
    var page = model.ActivePage;

    if (page is null || !await ShowAsync(page, id, options.SettleMilliseconds))
      return false;

    page.PausePolling();
    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);

    if (page.IsResourceTable && page.Rows.Count == 0) {
      manifest.Skipped.Add(new SkippedRecord { Id = id, Reason = "No rows." });

      return true;
    }

    manifest.Shots.Add(Capture(window, options, id, manifest, saveCatalog: false));
    await CaptureShoulderTabsAsync(window, page, options, manifest, id);

    return true;
  }

  private static async Task CaptureShoulderTabsAsync(
    Window window,
    ClusterPageViewModel page,
    ScreenshotTourOptions options,
    Manifest manifest,
    string id) {
    if (!page.IsResourceTable)
      return;

    var tabs = await Dispatcher.UIThread.InvokeAsync(() => VisibleShoulderTabs(window));

    if (tabs.Count == 0) {
      manifest.Skipped.Add(new SkippedRecord { Id = id, Reason = "Shoulder tabs were not found." });

      return;
    }

    foreach (var tab in tabs) {
      if (ScreenshotShoulder.Skip(id, tab))
        continue;

      await CaptureTabAsync(window, page, options, manifest, id, tab);
    }

    if (id is "persistentvolumeclaims" or "persistentvolumes")
      await CaptureVolumeFilesAsync(window, page, options, manifest);
  }

  private static async Task CaptureTabAsync(
    Window window,
    ClusterPageViewModel page,
    ScreenshotTourOptions options,
    Manifest manifest,
    string viewId,
    string tab) {
    await Dispatcher.UIThread.InvokeAsync(() => {
      page.EnsureRowSelected();
      page.SelectedTab = tab;
    });

    if (tab == DetailTab.Terminal)
      await WaitForTerminalAsync(page);

    await page.WaitUntilQuietAsync(20_000);
    await SettleAsync(options.SettleMilliseconds);

    PixelRect? redact = null;

    if (ScreenshotShoulder.Blur(viewId, tab))
      redact = await Dispatcher.UIThread.InvokeAsync(() => ShoulderBodyRect(window) ?? RightShoulder(window));

    manifest.Shots.Add(Capture(window, options, viewId + "-" + tab.ToLowerInvariant(), manifest, saveCatalog: false, redact));
    await Dispatcher.UIThread.InvokeAsync(() => {
      if (tab == DetailTab.Terminal)
        page.Terminal.Close();

      page.SelectedTab = DetailTab.Overview;
    });
  }

  private static async Task WaitForTerminalAsync(ClusterPageViewModel page) {
    var start = Environment.TickCount64;

    while (Environment.TickCount64 - start < 8_000) {
      var status = page.Terminal.TerminalStatus;

      if (!status.Contains("Connecting", StringComparison.Ordinal)
          && !status.StartsWith("Open a pod", StringComparison.Ordinal))
        return;

      await Task.Delay(100);
    }
  }

  private static IReadOnlyList<string> VisibleShoulderTabs(Window window) {
    var tabs = ShoulderTabs(window);

    if (tabs is null)
      return [];

    var headers = new List<string>();

    foreach (var item in tabs.Items) {
      if (item is not TabItem { IsVisible: true, Header: string header })
        continue;

      headers.Add(header);
    }

    return headers;
  }

  private static TabControl? ShoulderTabs(Window window) {
    foreach (var tabs in window.GetLogicalDescendants().OfType<TabControl>()) {
      var overview = false;
      var yaml = false;

      foreach (var item in tabs.Items) {
        if (item is not TabItem { Header: string header })
          continue;

        if (header == DetailTab.Overview)
          overview = true;
        else if (header == DetailTab.Yaml)
          yaml = true;
      }

      if (overview && yaml)
        return tabs;
    }

    return null;
  }

  private static PixelRect? ShoulderBodyRect(Window window) {
    if (ShoulderTabs(window)?.SelectedItem is not TabItem { Content: Control body })
      return null;

    var origin = body.TranslatePoint(new Point(0, 0), window);

    if (origin is null || body.Bounds.Width < 2 || body.Bounds.Height < 2)
      return null;

    return new PixelRect(
      (int)Math.Floor(origin.Value.X),
      (int)Math.Floor(origin.Value.Y),
      Math.Max(1, (int)Math.Ceiling(body.Bounds.Width)),
      Math.Max(1, (int)Math.Ceiling(body.Bounds.Height)));
  }

  private static PixelRect RightShoulder(Window window) {
    var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width));
    var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height));
    var pane = Math.Min(width, 480);

    return new PixelRect(width - pane, 0, pane, height);
  }

  private static async Task CaptureVolumeFilesAsync(
    Window window,
    ClusterPageViewModel page,
    ScreenshotTourOptions options,
    Manifest manifest) {
    if (!page.CanBrowseFiles || manifest.Shots.Any(shot => shot.Id == "volume-files"))
      return;

    page.BrowseFilesCommand.Execute(null);
    VolumeFilesWindow? files = null;
    var start = Environment.TickCount64;

    while (Environment.TickCount64 - start < 8000) {
      files = window.OwnedWindows.OfType<VolumeFilesWindow>().FirstOrDefault();

      if (files?.DataContext is VolumeFilesViewModel vm
          && (vm.Entries.Count > 0 || !string.Equals(vm.Status, "Resolving PVC…", StringComparison.Ordinal)))
        break;

      await Task.Delay(100);
    }

    await SettleAsync(options.SettleMilliseconds);

    if (files is null) {
      manifest.Skipped.Add(new SkippedRecord { Id = "volume-files", Reason = "Volume files did not open." });

      return;
    }

    manifest.Shots.Add(CaptureWindow(files, options, "volume-files"));
    files.Close();
  }

  private static async Task CaptureToolWindowAsync(
    Window owner,
    ScreenshotTourOptions options,
    Manifest manifest,
    string id,
    Window dialog) {
    dialog.Show(owner);
    await SettleAsync(options.SettleMilliseconds);
    manifest.Shots.Add(CaptureWindow(dialog, options, id));
    dialog.Close();
  }

  private static ShotRecord CaptureWindow(Window window, ScreenshotTourOptions options, string id) =>
    new() {
      Id = id,
      Window = Save(window, Path.Combine(options.Directory, Token(id) + ".png"))
    };

  private static async Task SaveCatalogAsync(Window window, ScreenshotTourOptions options, Manifest manifest) {
    await SettleAsync(options.SettleMilliseconds);
    manifest.Catalog = Save(window.FindControl<Control>("CatalogPane"), Path.Combine(options.Directory, "catalog.png"));
  }

  private static async Task SettleAsync(int milliseconds) {
    if (milliseconds > 0)
      await Task.Delay(milliseconds);

    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);
  }

  private static ShotRecord Capture(
    Window window,
    ScreenshotTourOptions options,
    string id,
    Manifest manifest,
    bool saveCatalog,
    PixelRect? redact = null) {
    var token = Token(id);
    var shot = new ShotRecord {
      Id = id,
      Window = Save(window, Path.Combine(options.Directory, token + ".png"), redact)
    };

    if (saveCatalog && manifest.Catalog is null)
      manifest.Catalog = Save(window.FindControl<Control>("CatalogPane"), Path.Combine(options.Directory, "catalog.png"));

    return shot;
  }

  private static string? Save(Control? control, string path, PixelRect? redact = null) {
    if (control is not { IsEffectivelyVisible: true })
      return null;

    var width = control is Window host ? host.ClientSize.Width : control.Bounds.Width;
    var height = control is Window hostWindow ? hostWindow.ClientSize.Height : control.Bounds.Height;

    if (width < 2)
      width = control.Bounds.Width;

    if (height < 2)
      height = control.Bounds.Height;

    if (width < 2 || height < 2)
      return null;

    var size = new PixelSize(
      Math.Max(1, (int)Math.Ceiling(width)),
      Math.Max(1, (int)Math.Ceiling(height)));

    using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
    bitmap.Render(control);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    if (redact is { } region)
      SaveRedacted(bitmap, size, region, path);
    else
      bitmap.Save(path, new PngBitmapEncoderOptions());

    return Path.GetFileName(path);
  }

  private static void SaveRedacted(RenderTargetBitmap bitmap, PixelSize size, PixelRect region, string path) {
    using var output = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    using (var frame = output.Lock()) {
      bitmap.CopyPixels(frame);
      Redact(frame, region);
    }

    output.Save(path, new PngBitmapEncoderOptions());
  }

  private static void Redact(ILockedFramebuffer frame, PixelRect region) {
    var width = frame.Size.Width;
    var height = frame.Size.Height;
    var stride = frame.RowBytes;
    var length = stride * height;

    if (length <= 0 || frame.Address == IntPtr.Zero)
      return;

    var pixels = new byte[length];
    Marshal.Copy(frame.Address, pixels, 0, length);

    if (frame.Format.BitsPerPixel == 32 && stride >= width * 4)
      BlurRegion(pixels, width, height, stride, region);
    else
      FillRegion(pixels, stride, region, width, height);

    Marshal.Copy(pixels, 0, frame.Address, length);
  }

  private static void BlurRegion(byte[] pixels, int width, int height, int stride, PixelRect region) {
    var left = Math.Clamp(region.X, 0, Math.Max(0, width - 1));
    var top = Math.Clamp(region.Y, 0, Math.Max(0, height - 1));
    var right = Math.Clamp(region.X + region.Width, left + 1, width);
    var bottom = Math.Clamp(region.Y + region.Height, top + 1, height);

    if (right - left < 2 || bottom - top < 2)
      return;

    const int radius = 16;
    const int passes = 3;
    var scratch = new byte[pixels.Length];

    for (var pass = 0; pass < passes; pass++) {
      BlurHorizontal(pixels, scratch, stride, left, right, top, bottom, radius);
      BlurVertical(scratch, pixels, stride, left, right, top, bottom, radius);
    }
  }

  private static void BlurHorizontal(
    byte[] source,
    byte[] dest,
    int stride,
    int left,
    int right,
    int top,
    int bottom,
    int radius) {
    var span = radius * 2 + 1;

    for (var y = top; y < bottom; y++) {
      var row = y * stride;

      for (var x = left; x < right; x++) {
        var b = 0;
        var g = 0;
        var r = 0;
        var a = 0;

        for (var dx = -radius; dx <= radius; dx++) {
          var sample = row + (Math.Clamp(x + dx, left, right - 1) * 4);
          b += source[sample];
          g += source[sample + 1];
          r += source[sample + 2];
          a += source[sample + 3];
        }

        var pixel = row + (x * 4);
        dest[pixel] = (byte)(b / span);
        dest[pixel + 1] = (byte)(g / span);
        dest[pixel + 2] = (byte)(r / span);
        dest[pixel + 3] = (byte)(a / span);
      }
    }
  }

  private static void BlurVertical(
    byte[] source,
    byte[] dest,
    int stride,
    int left,
    int right,
    int top,
    int bottom,
    int radius) {
    var span = radius * 2 + 1;

    for (var y = top; y < bottom; y++) {
      for (var x = left; x < right; x++) {
        var b = 0;
        var g = 0;
        var r = 0;
        var a = 0;

        for (var dy = -radius; dy <= radius; dy++) {
          var sample = (Math.Clamp(y + dy, top, bottom - 1) * stride) + (x * 4);
          b += source[sample];
          g += source[sample + 1];
          r += source[sample + 2];
          a += source[sample + 3];
        }

        var pixel = (y * stride) + (x * 4);
        dest[pixel] = (byte)(b / span);
        dest[pixel + 1] = (byte)(g / span);
        dest[pixel + 2] = (byte)(r / span);
        dest[pixel + 3] = (byte)(a / span);
      }
    }
  }

  private static void FillRegion(byte[] pixels, int stride, PixelRect region, int width, int height) {
    var left = Math.Clamp(region.X, 0, width);
    var top = Math.Clamp(region.Y, 0, height);
    var right = Math.Clamp(region.X + region.Width, left, width);
    var bottom = Math.Clamp(region.Y + region.Height, top, height);

    for (var y = top; y < bottom; y++) {
      for (var x = left; x < right; x++) {
        var pixel = (y * stride) + (x * 4);

        if (pixel + 3 >= pixels.Length)
          return;

        pixels[pixel] = 32;
        pixels[pixel + 1] = 36;
        pixels[pixel + 2] = 40;
        pixels[pixel + 3] = 255;
      }
    }
  }

  private static string Token(string id) {
    var buffer = new char[id.Length];

    for (var i = 0; i < id.Length; i++) {
      var c = id[i];
      buffer[i] = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-';
    }

    var token = new string(buffer).Trim('-');

    return token.Length == 0 ? "view" : token;
  }

  private sealed class Manifest {
    public string? Context { get; set; }

    public string? Catalog { get; set; }

    public List<ShotRecord> Shots { get; set; } = [];

    public List<SkippedRecord> Skipped { get; set; } = [];

    public string? Error { get; set; }
  }

  private sealed class ShotRecord {
    public string Id { get; set; } = "";

    public string? Window { get; set; }
  }

  private sealed class SkippedRecord {
    public string Id { get; set; } = "";

    public string Reason { get; set; } = "";
  }
}
