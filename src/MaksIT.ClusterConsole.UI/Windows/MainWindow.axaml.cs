using Avalonia.Controls;
using Avalonia.Interactivity;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Services;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class MainWindow : Window {
  private LayoutPersistence? _layout;

  public MainWindow() {
    InitializeComponent();
  }

  public MainWindow(MainViewModel viewModel, ConfigurationFileService configuration) : this() {
    DataContext = viewModel;
    _layout = new LayoutPersistence(
      this,
      configuration,
      () => viewModel.ActivePage?.Name,
      () => viewModel.SelectedDescriptor?.Id);

    Opened += async (_, _) => {
      _layout.Attach();
      await WhatsNewWindow.ShowIfNeededAsync(this, configuration);
    };

    viewModel.ConnectionsRequested += async (_, _) => await OpenConnectionsAsync(viewModel);
    viewModel.AiSettingsRequested += async (_, _) => await OpenAiSettingsAsync(viewModel);
    viewModel.VolumeFilesRequested += OpenVolumeFiles;
    viewModel.ShowRetainReclaim = ShowRetainReclaimAsync;
    viewModel.ShowDrainPreview = ShowDrainPreviewAsync;
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

  private async Task OpenConnectionsAsync(MainViewModel viewModel) {
    var window = new ConnectionsWindow(viewModel.CreateConnectionsViewModel());
    var connect = await window.ShowDialog<string?>(this);
    viewModel.LoadCatalogCommand.Execute(null);

    if (!string.IsNullOrWhiteSpace(connect))
      await viewModel.ConnectNamedAsync(connect);
  }
}
