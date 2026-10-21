using Avalonia.Controls;
using MaksIT.ClusterConsole.UI.ViewModels.Connections;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class ConnectionsWindow : Window {
  public ConnectionsWindow() {
    InitializeComponent();
  }

  public ConnectionsWindow(ConnectionsViewModel viewModel) : this() {
    DataContext = viewModel;
    viewModel.CloseRequested += name => Close(name);
    viewModel.AddRequested += () => AddConnectionAsync(viewModel);
    viewModel.CloudRequested += () => AddCloudAsync(viewModel);
  }

  private async Task AddConnectionAsync(ConnectionsViewModel viewModel) {
    var wizard = new ConnectionWizardWindow(new ConnectionWizardViewModel());
    var result = await wizard.ShowDialog<KubeConnectionRequestResult?>(this);

    if (result?.Request is null)
      return;

    viewModel.TryAdd(result.Request);
  }

  private async Task AddCloudAsync(ConnectionsViewModel viewModel) {
    var cloud = new CloudConnectionWindow(new CloudConnectionViewModel(viewModel.KubeConfigPath));
    var note = await cloud.ShowDialog<string?>(this);

    if (note is null)
      return;

    viewModel.Reload();
    viewModel.Status = note;
  }
}
