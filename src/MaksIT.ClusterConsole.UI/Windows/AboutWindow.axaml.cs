using Avalonia.Controls;
using Avalonia.Interactivity;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI.Windows;


public partial class AboutWindow : Window {
  public AboutWindow() {
    InitializeComponent();
  }

  public static Task ShowAsync(Window owner) {
    var window = new AboutWindow {
      DataContext = new AboutViewModel(),
      WindowStartupLocation = WindowStartupLocation.CenterOwner
    };
    return window.ShowDialog(owner);
  }

  private void OnCloseClick(object? sender, RoutedEventArgs e) =>
    Close();
}
