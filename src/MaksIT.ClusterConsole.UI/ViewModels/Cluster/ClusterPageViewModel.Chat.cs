using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Shared.Chat;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class ChatMessageViewModel : ObservableObject {
  private TaskCompletionSource<bool>? _decision;

  public required string Role { get; init; }

  public required string Text { get; init; }

  public string Change { get; init; } = "";

  public bool HasChange => !string.IsNullOrWhiteSpace(Change);

  public bool IsUser => Role == "user";

  public bool IsAssistant => Role == "assistant";

  public bool IsTool => Role == "tool";

  public bool IsConfirm => Role == "confirm";

  [ObservableProperty]
  private bool isPending;

  public void Arm(TaskCompletionSource<bool> decision) =>
    _decision = decision;

  [RelayCommand]
  private void Approve() =>
    Finish(true);

  [RelayCommand]
  private void Reject() =>
    Finish(false);

  private void Finish(bool approved) {
    if (!IsPending)
      return;

    IsPending = false;
    _decision?.TrySetResult(approved);
  }
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

  private bool CanSendChat =>
    !ChatBusy && !string.IsNullOrWhiteSpace(ChatInput);

  partial void OnChatInputChanged(string value) =>
    SendChatCommand.NotifyCanExecuteChanged();

  partial void OnChatBusyChanged(bool value) =>
    SendChatCommand.NotifyCanExecuteChanged();

  private string ModelStatus(string? activity = null) =>
    string.IsNullOrWhiteSpace(activity)
      ? $"Model · {_configuration.Current.OllamaModel}"
      : $"Model · {_configuration.Current.OllamaModel} · {activity}";

  private static string Activity(string status) {
    if (status.StartsWith("Tool · ", StringComparison.Ordinal))
      return status["Tool · ".Length..];

    if (status.StartsWith("Confirm · ", StringComparison.Ordinal))
      return "waiting for approval";

    if (status is "Processing" or "Sending…")
      return "processing";

    return status;
  }

  public void NotifyChatSettingsChanged() {
    if (!ChatBusy)
      ChatStatus = ModelStatus();
  }

  [RelayCommand]
  private void ClearChat() {
    _chatCts?.Cancel();
    _chatHistory.Clear();
    ChatMessages.Clear();
    ChatStatus = ModelStatus();
  }

  [RelayCommand]
  private void AskAboutSelection() {
    var rows = ActionTargets;
    if (rows.Count == 0) {
      ChatInput = "What is currently unhealthy in this cluster?";
      return;
    }

    if (rows.Count > 1) {
      var kind = SelectedDescriptor?.Title ?? "resources";
      var names = string.Join(", ", rows.Select(ResourceActionBatch.Label));
      ChatInput = $"What is wrong with these {kind}: {names}?";
      return;
    }

    var name = SelectedRelatedPod?.Name ?? rows[0].Name;
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
    ChatStatus = ModelStatus("processing");

    var history = _chatHistory.ToList();
    history.Add(new OllamaChatMessage { Role = "user", Content = prompt });
    var context = BuildChatContext();
    var cfg = _configuration.Current;

    try {
      var agent = cfg.AiEnabled && cfg.AiAgentEnabled;
      var result = await _chat.AskAsync(
        cfg.OllamaEndpoint,
        cfg.OllamaModel,
        history,
        context,
        status => Dispatcher.UIThread.Post(() => {
          ChatStatus = ModelStatus(Activity(status));
          if (status.StartsWith("Tool · ", StringComparison.Ordinal))
            ChatMessages.Add(new ChatMessageViewModel { Role = "tool", Text = status["Tool · ".Length..] });
        }),
        token,
        agent,
        agent ? ConfirmToolAsync : null);

      if (!result.IsSuccess) {
        var error = string.Join("; ", result.Messages);
        ChatMessages.Add(new ChatMessageViewModel { Role = "assistant", Text = error });
        ChatStatus = ModelStatus(error);
        return;
      }

      var answer = result.Value ?? "";
      _chatHistory.Add(new OllamaChatMessage { Role = "user", Content = prompt });
      _chatHistory.Add(new OllamaChatMessage { Role = "assistant", Content = answer });
      ChatMessages.Add(new ChatMessageViewModel { Role = "assistant", Text = answer });
      ChatStatus = ModelStatus();
    }
    catch (OperationCanceledException) {
      ChatStatus = ModelStatus("cancelled");
    }
    finally {
      ChatBusy = false;
    }
  }

  private Task<bool> ConfirmToolAsync(string description, string change, CancellationToken cancellationToken) {
    var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    Dispatcher.UIThread.Post(() => {
      var message = new ChatMessageViewModel {
        Role = "confirm",
        Text = description,
        Change = change,
        IsPending = true
      };
      message.Arm(decision);
      ChatMessages.Add(message);
    });
    cancellationToken.Register(() => decision.TrySetCanceled(cancellationToken));
    return decision.Task;
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
