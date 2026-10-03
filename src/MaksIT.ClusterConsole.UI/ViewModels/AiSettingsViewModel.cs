using Avalonia;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.UI.Controls.Footer;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class AiSettingsViewModel : ObservableObject {
  private readonly Action<bool, bool, string, string, int> _apply;

  public AiSettingsViewModel(
    bool enabled,
    bool agent,
    string endpoint,
    string model,
    int timeoutSeconds,
    Action<bool, bool, string, string, int> apply) {
    _apply = apply;
    enabledChat = enabled;
    agentEnabled = enabled && agent;
    endpointText = endpoint;
    modelText = model;
    this.timeoutSeconds = timeoutSeconds;
    FooterItems = [
      new FooterButton("Cancel", CancelCommand) {
        Edge = FooterEdge.Trailing,
        IsCancel = true,
        Padding = new Thickness(12, 7)
      },
      new FooterButton("Save", SaveCommand) {
        Edge = FooterEdge.Trailing,
        IsDefault = true,
        Padding = new Thickness(12, 7)
      }
    ];
  }

  [ObservableProperty]
  private bool enabledChat;

  [ObservableProperty]
  private bool agentEnabled;

  [ObservableProperty]
  private string endpointText;

  [ObservableProperty]
  private string modelText;

  [ObservableProperty]
  private int timeoutSeconds;

  partial void OnEnabledChatChanged(bool value) {
    if (!value)
      AgentEnabled = false;
  }

  public event Action<bool>? CloseRequested;

  public IReadOnlyList<FooterItem> FooterItems { get; }

  [RelayCommand]
  private void Save() {
    _apply(EnabledChat, EnabledChat && AgentEnabled, EndpointText, ModelText, TimeoutSeconds);
    CloseRequested?.Invoke(true);
  }

  [RelayCommand]
  private void Cancel() =>
    CloseRequested?.Invoke(false);
}
