using Avalonia.Controls;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class ScreenSaverSettingsWindow : Window {
  public ScreenSaverSettingsWindow() {
    InitializeComponent();
  }

  public ScreenSaverSettingsWindow(ScreenSaverSettingsViewModel viewModel) : this() {
    DataContext = viewModel;
    viewModel.CloseRequested += saved => Close(saved);
  }
}
