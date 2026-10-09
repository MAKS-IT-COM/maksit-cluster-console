using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using MaksIT.ClusterConsole.Shared;
using MaksIT.Core.UI.Snapshots;
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
          await AppViewSnapshot.SettleAsync(options.SettleMilliseconds);
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
    await AppViewSnapshot.SettleAsync(settleMilliseconds);

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
    await AppViewSnapshot.SettleAsync(options.SettleMilliseconds);

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

    return AppViewSnapshot.BoundsIn(body, window);
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

    await AppViewSnapshot.SettleAsync(options.SettleMilliseconds);

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
    await AppViewSnapshot.SettleAsync(options.SettleMilliseconds);
    manifest.Shots.Add(CaptureWindow(dialog, options, id));
    dialog.Close();
  }

  private static ShotRecord CaptureWindow(Window window, ScreenshotTourOptions options, string id) =>
    new() {
      Id = id,
      Window = AppViewSnapshot.Save(window, Path.Combine(options.Directory, AppViewSnapshot.FileToken(id) + ".png"))
    };

  private static async Task SaveCatalogAsync(Window window, ScreenshotTourOptions options, Manifest manifest) {
    await AppViewSnapshot.SettleAsync(options.SettleMilliseconds);
    manifest.Catalog = AppViewSnapshot.Save(window.FindControl<Control>("CatalogPane"), Path.Combine(options.Directory, "catalog.png"));
  }

  private static ShotRecord Capture(
    Window window,
    ScreenshotTourOptions options,
    string id,
    Manifest manifest,
    bool saveCatalog,
    PixelRect? redact = null) {
    var shot = new ShotRecord {
      Id = id,
      Window = AppViewSnapshot.Save(window, Path.Combine(options.Directory, AppViewSnapshot.FileToken(id) + ".png"), redact)
    };

    if (saveCatalog && manifest.Catalog is null)
      manifest.Catalog = AppViewSnapshot.Save(window.FindControl<Control>("CatalogPane"), Path.Combine(options.Directory, "catalog.png"));

    return shot;
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
