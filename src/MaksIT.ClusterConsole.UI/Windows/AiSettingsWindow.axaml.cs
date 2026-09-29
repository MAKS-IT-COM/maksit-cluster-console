using Avalonia.Controls;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class AiSettingsWindow : Window {
  public AiSettingsWindow() {
    InitializeComponent();
  }

  public AiSettingsWindow(AiSettingsViewModel viewModel) : this() {
    DataContext = viewModel;
    viewModel.CloseRequested += saved => Close(saved);
  }
}
