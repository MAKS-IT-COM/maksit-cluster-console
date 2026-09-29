using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class AiSettingsViewModel : ObservableObject {
  private readonly Action<bool, bool, string, string> _apply;

  public AiSettingsViewModel(
    bool enabled,
    bool agent,
    string endpoint,
    string model,
    Action<bool, bool, string, string> apply) {
    _apply = apply;
    enabledChat = enabled;
    agentEnabled = enabled && agent;
    endpointText = endpoint;
    modelText = model;
  }

  [ObservableProperty]
  private bool enabledChat;

  [ObservableProperty]
  private bool agentEnabled;

  [ObservableProperty]
  private string endpointText;

  [ObservableProperty]
  private string modelText;

  partial void OnEnabledChatChanged(bool value) {
    if (!value)
      AgentEnabled = false;
  }

  public event Action<bool>? CloseRequested;

  [RelayCommand]
  private void Save() {
    _apply(EnabledChat, EnabledChat && AgentEnabled, EndpointText, ModelText);
    CloseRequested?.Invoke(true);
  }

  [RelayCommand]
  private void Cancel() =>
    CloseRequested?.Invoke(false);
}
