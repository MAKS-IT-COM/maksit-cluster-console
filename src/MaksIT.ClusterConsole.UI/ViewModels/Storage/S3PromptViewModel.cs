using CommunityToolkit.Mvvm.ComponentModel;


namespace MaksIT.ClusterConsole.UI.ViewModels.Storage;

public partial class S3PromptViewModel : ObservableObject {
  public S3PromptViewModel(string title, string message, bool ask, string value) {
    Title = title;
    Message = message;
    Ask = ask;
    this.value = value;
  }

  public string Title { get; }

  public string Message { get; }

  public bool Ask { get; }

  [ObservableProperty]
  private string value;
}
