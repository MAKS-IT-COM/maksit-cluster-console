using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Connections;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class ConnectionWizardWindow : Window {
  public ConnectionWizardWindow() {
    InitializeComponent();
  }

  public ConnectionWizardWindow(ConnectionWizardViewModel viewModel) : this() {
    DataContext = viewModel;
    var next = new FooterButton(viewModel.NextLabel, new RelayCommand(Advance)) {
      Edge = FooterEdge.Trailing
    };
    viewModel.PropertyChanged += (_, e) => {
      if (e.PropertyName == nameof(ConnectionWizardViewModel.NextLabel))
        next.Text = viewModel.NextLabel;
    };
    Actions.Items = [
      new FooterButton("Cancel", new RelayCommand(() => Close(null))) { Edge = FooterEdge.Trailing },
      new FooterButton("Back", viewModel.BackCommand) { Edge = FooterEdge.Trailing },
      next
    ];
  }

  public KubeConnectionRequestResult? Result { get; private set; }

  private void Advance() {
    if (DataContext is not ConnectionWizardViewModel vm)
      return;

    if (!vm.TryAdvance())
      return;

    Result = new KubeConnectionRequestResult(vm.ToRequest());
    Close(Result);
  }

  private async void OnBrowseCaClick(object? sender, RoutedEventArgs e) {
    if (DataContext is ConnectionWizardViewModel vm)
      vm.CaFile = await PickFileAsync("Cluster CA", CertTypes()) ?? vm.CaFile;
  }

  private async void OnBrowseCertClick(object? sender, RoutedEventArgs e) {
    if (DataContext is ConnectionWizardViewModel vm)
      vm.ClientCertFile = await PickFileAsync("Client certificate", CertTypes()) ?? vm.ClientCertFile;
  }

  private async void OnBrowseKeyClick(object? sender, RoutedEventArgs e) {
    if (DataContext is ConnectionWizardViewModel vm)
      vm.ClientKeyFile = await PickFileAsync("Client key", KeyTypes()) ?? vm.ClientKeyFile;
  }

  private async Task<string?> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> types) {
    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = title,
      AllowMultiple = false,
      FileTypeFilter = types
    });

    return files.Count == 0 ? null : files[0].TryGetLocalPath();
  }

  private static FilePickerFileType[] CertTypes() =>
    [
      new("Certificates") { Patterns = ["*.crt", "*.pem", "*.cer"] },
      FilePickerFileTypes.All
    ];

  private static FilePickerFileType[] KeyTypes() =>
    [
      new("Keys") { Patterns = ["*.key", "*.pem"] },
      FilePickerFileTypes.All
    ];
}

public sealed record KubeConnectionRequestResult(MaksIT.ClusterConsole.Client.KubeConfig.KubeConnectionRequest Request);
