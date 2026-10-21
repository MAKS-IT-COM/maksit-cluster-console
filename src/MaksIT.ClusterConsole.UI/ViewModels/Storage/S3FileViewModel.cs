using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.UI.ViewModels.Storage;

public partial class S3FileViewModel : ObservableObject {
  private readonly Func<string, Task<Result>> _save;
  private bool _loading = true;

  public S3FileViewModel(string key, string text, Func<string, Task<Result>> save) {
    Key = key;
    FileName = key;
    _save = save;
    Text = text;
    _loading = false;
  }

  public string Key { get; }

  public string FileName { get; }

  public bool Saved { get; private set; }

  [ObservableProperty]
  private string text;

  [ObservableProperty]
  private bool isDirty;

  [ObservableProperty]
  private string status = "";

  partial void OnTextChanged(string value) {
    if (_loading)
      return;

    IsDirty = true;
  }

  partial void OnIsDirtyChanged(bool value) =>
    SaveCommand.NotifyCanExecuteChanged();

  [RelayCommand(CanExecute = nameof(CanSave))]
  private async Task SaveAsync() {
    var saved = await _save(Text);

    if (!saved.IsSuccess) {
      Status = saved.Messages.Count == 0 ? "The request failed." : string.Join("; ", saved.Messages);

      return;
    }

    IsDirty = false;
    Saved = true;
    Status = "Saved.";
  }

  private bool CanSave() =>
    IsDirty;
}
