using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class S3FileWindow : Window {
  private readonly FooterLabel _unsaved = new("Unsaved") {
    IsVisible = false,
    Margin = new Thickness(8, 0, 0, 0)
  };
  private bool _closing;

  public S3FileWindow() {
    InitializeComponent();
  }

  public S3FileWindow(S3FileViewModel model) : this() {
    DataContext = model;
    model.PropertyChanged += (_, change) => {
      if (change.PropertyName == nameof(S3FileViewModel.IsDirty))
        _unsaved.IsVisible = model.IsDirty;
    };
    Actions.Items = [
      _unsaved,
      new FooterButton("Save", model.SaveCommand) { Edge = FooterEdge.Trailing },
      new FooterButton("Close", new RelayCommand(() => Close())) { Edge = FooterEdge.Trailing, IsCancel = true }
    ];
  }

  public static async Task<bool> ShowAsync(Window owner, S3FileViewModel model) {
    var window = new S3FileWindow(model);
    await window.ShowDialog(owner);

    return model.Saved;
  }

  protected override void OnClosing(WindowClosingEventArgs e) {
    base.OnClosing(e);

    if (e.Cancel || _closing || DataContext is not S3FileViewModel { IsDirty: true })
      return;

    e.Cancel = true;
    _ = ConfirmCloseAsync();
  }

  private async Task ConfirmCloseAsync() {
    if (DataContext is not S3FileViewModel)
      return;

    var discard = await S3PromptWindow.ConfirmAsync(this, "Unsaved changes", "This file has unsaved changes. Close and drop them?");

    if (!discard)
      return;

    _closing = true;
    Close();
  }
}
