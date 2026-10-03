using Avalonia.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class DrainPreviewWindow : Window {
  public DrainPreviewWindow() {
    InitializeComponent();
  }

  public DrainPreviewWindow(DrainConfirmViewModel model) : this() {
    DataContext = model;
    Actions.Items = [
      new FooterButton("Cancel", new RelayCommand(() => Close(false))) { Edge = FooterEdge.Trailing },
      new FooterButton("Drain", new RelayCommand(() => Close(true))) { Edge = FooterEdge.Trailing }
    ];
  }

  public static Task<bool> ShowAsync(Window owner, DrainConfirmViewModel model) {
    var window = new DrainPreviewWindow(model);

    return window.ShowDialog<bool>(owner);
  }

  protected override void OnKeyDown(KeyEventArgs e) {
    if (e.Key == Key.Escape) {
      Close(false);
      e.Handled = true;

      return;
    }

    base.OnKeyDown(e);
  }

  protected override void OnClosed(EventArgs e) {
    if (DataContext is DrainConfirmViewModel dialog)
      dialog.CancelAdvice();

    base.OnClosed(e);
  }
}
