using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
    await Dispatcher.UIThread.InvokeAsync(page.EnsureRowSelected);
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
    await CaptureFeatureExtrasAsync(window, model, page, options, manifest, id);

    return true;
  }

  private static async Task CaptureFeatureExtrasAsync(
    Window window,
    MainViewModel model,
    ClusterPageViewModel page,
    ScreenshotTourOptions options,
    Manifest manifest,
    string id) {
    if (id == "pods") {
      await CaptureTabAsync(window, page, options, manifest, DetailTab.Yaml, "pods-yaml");
      await CaptureTabAsync(window, page, options, manifest, DetailTab.Logs, "pods-logs");

      if (model.ShowChat)
        await CaptureTabAsync(window, page, options, manifest, DetailTab.Chat, "pods-chat");
    }

    if (id == "nodes")
      await CaptureTabAsync(window, page, options, manifest, DetailTab.Images, "nodes-images");

    if (id == "configmaps")
      await CaptureTabAsync(window, page, options, manifest, DetailTab.Data, "configmaps-data");

    if (id == ResourceCatalog.HelmReleasesId) {
      await CaptureTabAsync(window, page, options, manifest, DetailTab.History, "helm-history");
      await CaptureTabAsync(window, page, options, manifest, DetailTab.Values, "helm-values");
      await CaptureTabAsync(window, page, options, manifest, DetailTab.Manifest, "helm-manifest");
    }

    if (id is "persistentvolumeclaims" or "persistentvolumes")
      await CaptureVolumeFilesAsync(window, page, options, manifest);
  }

  private static async Task CaptureTabAsync(
    Window window,
    ClusterPageViewModel page,
    ScreenshotTourOptions options,
    Manifest manifest,
    string tab,
    string id) {
    await Dispatcher.UIThread.InvokeAsync(() => {
      page.EnsureRowSelected();
      page.SelectedTab = tab;
    });
    await page.WaitUntilQuietAsync(20_000);
    await SettleAsync(options.SettleMilliseconds);
    manifest.Shots.Add(Capture(window, options, id, manifest, saveCatalog: false));
    page.SelectedTab = DetailTab.Overview;
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
    bool saveCatalog) {
    var token = Token(id);
    var shot = new ShotRecord {
      Id = id,
      Window = Save(window, Path.Combine(options.Directory, token + ".png"))
    };

    if (saveCatalog && manifest.Catalog is null)
      manifest.Catalog = Save(window.FindControl<Control>("CatalogPane"), Path.Combine(options.Directory, "catalog.png"));

    return shot;
  }

  private static string? Save(Control? control, string path) {
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
    bitmap.Save(path, new PngBitmapEncoderOptions());

    return Path.GetFileName(path);
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
