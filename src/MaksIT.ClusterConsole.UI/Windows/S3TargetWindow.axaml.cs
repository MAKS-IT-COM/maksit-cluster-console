using Avalonia.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class S3TargetWindow : Window {
  public S3TargetWindow() {
    InitializeComponent();
  }

  public S3TargetWindow(S3TargetViewModel model) : this() {
    DataContext = model;
    Actions.Items = [
      new FooterButton("Cancel", new RelayCommand(() => Close(false))) { Edge = FooterEdge.Trailing, IsCancel = true },
      new FooterButton("Save", new RelayCommand(() => Save(model))) { Edge = FooterEdge.Trailing, IsDefault = true }
    ];
  }

  public static Task<bool> ShowAsync(Window owner, S3TargetViewModel model) {
    var window = new S3TargetWindow(model);

    return window.ShowDialog<bool>(owner);
  }

  private void Save(S3TargetViewModel model) {
    if (!model.TryAccept())
      return;

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
