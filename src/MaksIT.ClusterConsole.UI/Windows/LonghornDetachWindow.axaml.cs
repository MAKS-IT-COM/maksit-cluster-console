using Avalonia.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class LonghornDetachWindow : Window {
  public LonghornDetachWindow() {
    InitializeComponent();
  }

  public LonghornDetachWindow(LonghornDetachViewModel model) : this() {
    DataContext = model;
    Actions.Items = [
      new FooterButton("Cancel", new RelayCommand(() => Close(false))) { Edge = FooterEdge.Trailing },
      new FooterButton("Detach", new RelayCommand(() => Close(true))) { Edge = FooterEdge.Trailing }
    ];
  }

  public static Task<bool> ShowAsync(Window owner, LonghornDetachViewModel model) {
    var window = new LonghornDetachWindow(model);

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
}
