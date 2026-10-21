using Avalonia.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class LonghornBackupJobWindow : Window {
  public LonghornBackupJobWindow() {
    InitializeComponent();
  }

  public LonghornBackupJobWindow(LonghornBackupJobViewModel model) : this() {
    DataContext = model;
    Title = model.Heading;
    Actions.Items = [
      new FooterButton("Cancel", new RelayCommand(() => Close(false))) { Edge = FooterEdge.Trailing },
      new FooterButton(model.AcceptLabel, new RelayCommand(Accept)) { Edge = FooterEdge.Trailing }
    ];
  }

  public static Task<bool> ShowAsync(Window owner, LonghornBackupJobViewModel model) {
    var window = new LonghornBackupJobWindow(model);

    return window.ShowDialog<bool>(owner);
  }

  private void Accept() {
    if (DataContext is not LonghornBackupJobViewModel model)
      return;

    var error = model.Validate();

    if (error is not null) {
      model.Error = error;

      return;
    }

    model.Error = "";
    Close(true);
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
