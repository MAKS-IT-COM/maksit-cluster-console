using Avalonia.Controls;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class RetainReclaimWindow : Window {
  public RetainReclaimWindow() {
    InitializeComponent();
  }

  public RetainReclaimWindow(RetainReclaimViewModel viewModel) : this() {
    DataContext = viewModel;
    viewModel.CloseRequested += () => Close();
    Opened += async (_, _) => await viewModel.LoadAsync();
  }
}
