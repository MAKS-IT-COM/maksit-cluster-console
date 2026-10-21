using Avalonia.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class S3PromptWindow : Window {
  public S3PromptWindow() {
    InitializeComponent();
  }

  public S3PromptWindow(S3PromptViewModel model) : this() {
    DataContext = model;
    Actions.Items = [
      new FooterButton("Cancel", new RelayCommand(() => Close(false))) { Edge = FooterEdge.Trailing, IsCancel = true },
      new FooterButton("OK", new RelayCommand(() => Close(true))) { Edge = FooterEdge.Trailing, IsDefault = true }
    ];
  }

  public static Task<bool> ConfirmAsync(Window owner, string title, string message) {
    var window = new S3PromptWindow(new S3PromptViewModel(title, message, false, ""));

    return window.ShowDialog<bool>(owner);
  }

  public static async Task<string?> AskAsync(Window owner, string title, string message, string initial) {
    var model = new S3PromptViewModel(title, message, true, initial);
    var window = new S3PromptWindow(model);
    var accepted = await window.ShowDialog<bool>(owner);

    if (!accepted)
      return null;

    var text = model.Value.Trim();

    return text.Length == 0 ? null : text;
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
