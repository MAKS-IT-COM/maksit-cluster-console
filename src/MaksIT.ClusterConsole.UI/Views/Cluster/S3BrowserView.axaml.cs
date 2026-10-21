using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MaksIT.ClusterConsole.UI.ViewModels.Storage;
using MaksIT.ClusterConsole.UI.Windows;


namespace MaksIT.ClusterConsole.UI.Views.Cluster;

public partial class S3BrowserView : UserControl {
  public S3BrowserView() {
    InitializeComponent();
    DataContextChanged += (_, _) => Wire();
  }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
    base.OnAttachedToVisualTree(e);
    Wire();
  }

  private void Wire() {
    if (DataContext is not S3BrowserViewModel viewModel)
      return;

    var window = TopLevel.GetTopLevel(this) as Window;

    if (window is null)
      return;

    viewModel.EditTarget = model => S3TargetWindow.ShowAsync(window, model);
    viewModel.Preview = model => S3FileWindow.ShowAsync(window, model);
    viewModel.Confirm = (title, message) => S3PromptWindow.ConfirmAsync(window, title, message);
    viewModel.Ask = (title, message, initial) => S3PromptWindow.AskAsync(window, title, message, initial);
    viewModel.PickFiles = PickFilesAsync;
    viewModel.PickFolder = PickFolderAsync;
    viewModel.PickSavePath = PickSavePathAsync;
  }

  private void OnBucketDoubleTapped(object? sender, TappedEventArgs e) {
    if (DataContext is not S3BrowserViewModel viewModel || !viewModel.BucketRootCommand.CanExecute(null))
      return;

    viewModel.BucketRootCommand.Execute(null);
  }

  private void OnEntriesDoubleTapped(object? sender, TappedEventArgs e) {
    if (DataContext is not S3BrowserViewModel viewModel || !viewModel.OpenEntryCommand.CanExecute(null))
      return;

    viewModel.OpenEntryCommand.Execute(null);
  }

  private async Task<IReadOnlyList<string>?> PickFilesAsync() {
    var top = TopLevel.GetTopLevel(this);

    if (top is null)
      return null;

    var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = "Upload files",
      AllowMultiple = true
    });

    if (files.Count == 0)
      return null;

    var paths = new List<string>();

    foreach (var file in files) {
      var path = file.TryGetLocalPath();

      if (!string.IsNullOrWhiteSpace(path))
        paths.Add(path);
    }

    return paths;
  }

  private async Task<string?> PickFolderAsync(string title) {
    var top = TopLevel.GetTopLevel(this);

    if (top is null)
      return null;

    var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
      Title = title,
      AllowMultiple = false
    });

    if (folders.Count == 0)
      return null;

    return folders[0].TryGetLocalPath();
  }

  private async Task<string?> PickSavePathAsync(string suggestedName) {
    var top = TopLevel.GetTopLevel(this);

    if (top is null)
      return null;

    var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
      Title = "Download file",
      SuggestedFileName = suggestedName
    });

    return file?.TryGetLocalPath();
  }
}
