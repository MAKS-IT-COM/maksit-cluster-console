using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class DrainPreviewWindow : Window {
  public DrainPreviewWindow() {
    InitializeComponent();
  }

  public DrainPreviewWindow(DrainPreview preview) : this() {
    DataContext = new DrainConfirmViewModel(preview);
  }

  public static Task<bool> ShowAsync(Window owner, DrainPreview preview) {
    var window = new DrainPreviewWindow(preview);
    return window.ShowDialog<bool>(owner);
  }

  private void OnCancel(object? sender, RoutedEventArgs e) =>
    Close(false);

  private void OnDrain(object? sender, RoutedEventArgs e) =>
    Close(true);

  protected override void OnKeyDown(KeyEventArgs e) {
    if (e.Key == Key.Escape) {
      Close(false);
      e.Handled = true;
      return;
    }

    base.OnKeyDown(e);
  }
}
