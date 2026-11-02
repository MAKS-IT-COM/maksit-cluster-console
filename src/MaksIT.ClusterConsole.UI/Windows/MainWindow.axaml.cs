using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Controls;
using MaksIT.ClusterConsole.UI.Services;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class MainWindow : Window {
  private LayoutPersistence? _layout;
  private ScreenSaverController? _screenSaver;
  private WindowState _windowedState = WindowState.Normal;
  private MainViewModel? _viewModel;

  public MainWindow() {
    InitializeComponent();
  }

  public MainWindow(MainViewModel viewModel, ConfigurationFileService configuration, ScreenshotTourOptions? tour = null) : this() {
    DataContext = viewModel;
    _viewModel = viewModel;
    _layout = new LayoutPersistence(
      this,
      configuration,
      () => viewModel.ActivePage?.Name,
      () => viewModel.SelectedDescriptor?.Id);

    var overlay = this.FindControl<BubblesScreenSaverControl>("ScreenSaverOverlay")
      ?? throw new InvalidOperationException("ScreenSaverOverlay is missing.");
    _screenSaver = new ScreenSaverController(this, viewModel, configuration, overlay);

    Opened += async (_, _) => {
      _layout.Attach();
      _screenSaver.Attach();

      if (tour is not null) {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        await ScreenshotTour.RunAsync(this, viewModel, tour, code => desktop?.Shutdown(code));

        return;
      }

      await WhatsNewWindow.ShowIfNeededAsync(this, configuration);
    };

    Closed += (_, _) => _screenSaver?.Dispose();

    PropertyChanged += OnWindowPropertyChanged;
    viewModel.FullScreenToggleRequested += (_, _) => ToggleFullScreen();
    viewModel.ConnectionsRequested += async (_, _) => await OpenConnectionsAsync(viewModel);
    viewModel.AiSettingsRequested += async (_, _) => await OpenAiSettingsAsync(viewModel);
    viewModel.ScreenSaverSettingsRequested += async (_, _) => await OpenScreenSaverSettingsAsync(viewModel);
    viewModel.VolumeFilesRequested += OpenVolumeFiles;
    viewModel.ShowRetainReclaim = ShowRetainReclaimAsync;
    viewModel.ShowDrainPreview = ShowDrainPreviewAsync;
    viewModel.ConfirmLonghornAttach = dialog => LonghornAttachWindow.ShowAsync(this, dialog);
    viewModel.ConfirmLonghornDetach = dialog => LonghornDetachWindow.ShowAsync(this, dialog);
    viewModel.ConfirmLonghornBackupJob = dialog => LonghornBackupJobWindow.ShowAsync(this, dialog);

    AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
  }

  private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
    if (e.Property != WindowStateProperty || _viewModel is null)
      return;

    var state = WindowState;

    if (state is WindowState.Normal or WindowState.Maximized)
      _windowedState = state;

    _viewModel.IsFullScreen = state == WindowState.FullScreen;
  }

  private void ToggleFullScreen() {
    if (WindowState == WindowState.FullScreen)
      ExitFullScreen();
    else
      EnterFullScreen();
  }

  private void EnterFullScreen() {
    if (WindowState is WindowState.Normal or WindowState.Maximized)
      _windowedState = WindowState;

    WindowState = WindowState.FullScreen;
  }

  private void ExitFullScreen() {
    if (WindowState != WindowState.FullScreen)
      return;

    WindowState = _windowedState is WindowState.Maximized
      ? WindowState.Maximized
      : WindowState.Normal;
  }

  private void OnPreviewKeyDown(object? sender, KeyEventArgs e) {
    if (e.Key != Key.Escape)
      return;

    if (_screenSaver?.TryDismiss() == true) {
      e.Handled = true;

      return;
    }

    if (WindowState == WindowState.FullScreen) {
      ExitFullScreen();
      e.Handled = true;
    }
  }

  private void OnLogsClick(object? sender, RoutedEventArgs e) =>
    _ = LogWindow.ShowAsync(this);

  private void OnAboutClick(object? sender, RoutedEventArgs e) =>
    _ = AboutWindow.ShowAsync(this);

  private void OpenVolumeFiles(VolumeFilesViewModel files) {
    var window = new VolumeFilesWindow(files);
    window.Show(this);
  }

  private Task<bool> ShowDrainPreviewAsync(DrainConfirmViewModel dialog) =>
    DrainPreviewWindow.ShowAsync(this, dialog);

  private async Task ShowRetainReclaimAsync(RetainReclaimViewModel reclaim) {
    var window = new RetainReclaimWindow(reclaim);
    await window.ShowDialog(this);
  }

  private async Task OpenAiSettingsAsync(MainViewModel viewModel) {
    var window = new AiSettingsWindow(viewModel.CreateAiSettingsViewModel());
    await window.ShowDialog<bool>(this);
  }

  private async Task OpenScreenSaverSettingsAsync(MainViewModel viewModel) {
    var window = new ScreenSaverSettingsWindow(viewModel.CreateScreenSaverSettingsViewModel());
    await window.ShowDialog<bool>(this);
  }

  private async Task OpenConnectionsAsync(MainViewModel viewModel) {
    var window = new ConnectionsWindow(viewModel.CreateConnectionsViewModel());
    var connect = await window.ShowDialog<string?>(this);
    viewModel.LoadCatalogCommand.Execute(null);

    if (!string.IsNullOrWhiteSpace(connect))
      await viewModel.ConnectNamedAsync(connect);
  }
}
