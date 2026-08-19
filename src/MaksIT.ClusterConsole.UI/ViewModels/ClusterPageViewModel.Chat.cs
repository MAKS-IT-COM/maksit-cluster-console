using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class ChatMessageViewModel : ObservableObject {
  public required string Role { get; init; }

  public required string Text { get; init; }

  public bool IsUser => Role == "user";

  public bool IsAssistant => Role == "assistant";

  public bool IsTool => Role == "tool";
}

public partial class ClusterPageViewModel {
  private readonly List<OllamaChatMessage> _chatHistory = [];
  private CancellationTokenSource? _chatCts;

  public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = [];

  [ObservableProperty]
  private string chatInput = string.Empty;

  [ObservableProperty]
  private string chatStatus = "";

  [ObservableProperty]
  private bool chatBusy;

  public string ChatModelCaption =>
    $"Model {_configuration.Current.OllamaModel} · RTX 3060 · read-only tools (issues, get, logs, events)";

  private bool CanSendChat =>
    !ChatBusy && !string.IsNullOrWhiteSpace(ChatInput);

  partial void OnChatInputChanged(string value) =>
    SendChatCommand.NotifyCanExecuteChanged();

  partial void OnChatBusyChanged(bool value) =>
    SendChatCommand.NotifyCanExecuteChanged();

  [RelayCommand]
  private void ClearChat() {
    _chatCts?.Cancel();
    _chatHistory.Clear();
    ChatMessages.Clear();
    ChatStatus = $"Local Ollama · {_configuration.Current.OllamaModel}";
  }

  [RelayCommand]
  private void AskAboutSelection() {
    if (SelectedRow is null) {
      ChatInput = "What is currently unhealthy in this cluster?";
      return;
    }

    var name = SelectedRelatedPod?.Name ?? SelectedRow.Name;
    var container = SelectedContainer is null ? "" : $" container {SelectedContainer.Name}";
    ChatInput = $"What is wrong with {SelectedDocumentKind ?? SelectedResourceRef()?.Kind ?? "this resource"} {name}{container}?";
  }

  [RelayCommand(CanExecute = nameof(CanSendChat))]
  private async Task SendChatAsync() {
    var prompt = ChatInput.Trim();
    if (prompt.Length == 0 || ChatBusy)
      return;

    ChatInput = "";
    ChatBusy = true;
    _chatCts?.Cancel();
    _chatCts = new CancellationTokenSource();
    var token = _chatCts.Token;
    ChatMessages.Add(new ChatMessageViewModel { Role = "user", Text = prompt });
    ChatStatus = "Sending…";

    var history = _chatHistory.ToList();
    history.Add(new OllamaChatMessage { Role = "user", Content = prompt });
    var context = BuildChatContext();
    var cfg = _configuration.Current;

    try {
      var result = await _chat.AskAsync(
        cfg.OllamaEndpoint,
        cfg.OllamaModel,
        history,
        context,
        status => Dispatcher.UIThread.Post(() => {
          ChatStatus = status;
          if (status.StartsWith("Tool · ", StringComparison.Ordinal))
            ChatMessages.Add(new ChatMessageViewModel { Role = "tool", Text = status["Tool · ".Length..] });
        }),
        token);

      if (!result.IsSuccess) {
        var error = string.Join("; ", result.Messages);
        ChatMessages.Add(new ChatMessageViewModel { Role = "assistant", Text = error });
        ChatStatus = error;
        return;
      }

      var answer = result.Value ?? "";
      _chatHistory.Add(new OllamaChatMessage { Role = "user", Content = prompt });
      _chatHistory.Add(new OllamaChatMessage { Role = "assistant", Content = answer });
      ChatMessages.Add(new ChatMessageViewModel { Role = "assistant", Text = answer });
      ChatStatus = $"Ollama · {cfg.OllamaModel}";
    }
    catch (OperationCanceledException) {
      ChatStatus = "Cancelled.";
    }
    finally {
      ChatBusy = false;
    }
  }

  private ClusterChatContext BuildChatContext() =>
    new(
      Name,
      SelectedNamespace,
      SelectedDocumentKind ?? SelectedResourceRef()?.Kind ?? SelectedDescriptor?.Kind,
      SelectedRow?.Name,
      TargetPodName,
      SelectedContainer?.Name,
      OverviewText,
      EventsText,
      LogsText);
}
