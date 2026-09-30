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
  }

  private async Task AddConnectionAsync(ConnectionsViewModel viewModel) {
    var wizard = new ConnectionWizardWindow(new ConnectionWizardViewModel());
    var result = await wizard.ShowDialog<KubeConnectionRequestResult?>(this);
    if (result is null)
      return;

    viewModel.TryAdd(result.Request);
  }
}
