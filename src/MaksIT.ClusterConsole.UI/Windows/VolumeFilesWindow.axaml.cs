using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI;

public partial class VolumeFilesWindow : Window {
  public VolumeFilesWindow() {
    InitializeComponent();
  }

  public VolumeFilesWindow(VolumeFilesViewModel viewModel) : this() {
    DataContext = viewModel;
    viewModel.PickSavePath += PickSavePathAsync;
    viewModel.PickOpenFile += PickOpenFileAsync;
    Opened += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
  }

  private void OnEntriesDoubleTapped(object? sender, TappedEventArgs e) {
    if (DataContext is VolumeFilesViewModel vm && vm.SelectedEntry is VolumeEntry entry)
      vm.OpenEntryCommand.Execute(entry);
  }

  private async Task<string?> PickSavePathAsync(string suggestedName) {
    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
      Title = "Download file",
      SuggestedFileName = suggestedName
    });
    return file?.TryGetLocalPath();
  }

  private async Task<LocalFilePick?> PickOpenFileAsync() {
    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = "Upload file",
      AllowMultiple = false
    });
    if (files.Count == 0)
      return null;

    var file = files[0];
    var name = file.Name;
    await using var stream = await file.OpenReadAsync();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer);
    return new LocalFilePick(name, buffer.ToArray());
  }
}
