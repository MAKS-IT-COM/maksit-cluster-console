using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Shared.Chat;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public sealed class ClusterChatSelection {
  public required string ContextName { get; init; }

  public required string Namespace { get; init; }

  public string? Kind { get; init; }

  public string? ResourceName { get; init; }

  public string? PodName { get; init; }

  public string? ContainerName { get; init; }

  public string Overview { get; init; } = "";

  public string Events { get; init; } = "";

  public string LogText { get; init; } = "";

  public IReadOnlyList<ResourceRow> Targets { get; init; } = [];

  public string? DescriptorTitle { get; init; }

  public string? RelatedPodName { get; init; }

  public string? DocumentKind { get; init; }
}

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

public partial class ClusterChatViewModel : ObservableObject {
  private readonly ConfigurationFileService _configuration;
  private readonly ClusterChatService _chat;
  private readonly Func<ClusterChatSelection> _selection;
  private readonly List<OllamaChatMessage> _history = [];
  private CancellationTokenSource? _chatCts;

  public ClusterChatViewModel(
    ConfigurationFileService configuration,
    ClusterChatService chat,
    Func<ClusterChatSelection> selection) {
    _configuration = configuration;
    _chat = chat;
    _selection = selection;
    chatStatus = ModelStatus();
  }

  public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = [];

  [ObservableProperty]
  private string chatInput = string.Empty;

  [ObservableProperty]
  private string chatStatus = "";

  [ObservableProperty]
  private bool chatBusy;

  public void Cancel() =>
    _chatCts?.Cancel();

  public void NotifySettingsChanged() {
    if (!ChatBusy)
      ChatStatus = ModelStatus();
  }

  private bool CanSendChat =>
    !ChatBusy && !string.IsNullOrWhiteSpace(ChatInput);

  partial void OnChatInputChanged(string value) =>
    SendChatCommand.NotifyCanExecuteChanged();

  partial void OnChatBusyChanged(bool value) =>
    SendChatCommand.NotifyCanExecuteChanged();

  private string ModelStatus(string? activity = null) =>
    string.IsNullOrWhiteSpace(activity)
      ? $"Model · {_configuration.Current.Ai.Model}"
      : $"Model · {_configuration.Current.Ai.Model} · {activity}";

  private static string Activity(string status) {
    if (status.StartsWith("Tool · ", StringComparison.Ordinal))
      return status["Tool · ".Length..];

    if (status.StartsWith("Confirm · ", StringComparison.Ordinal))
      return "waiting for approval";

    if (status is "Processing" or "Sending…")
      return "processing";

    return status;
  }

  [RelayCommand]
  private void ClearChat() {
    _chatCts?.Cancel();
    _history.Clear();
    ChatMessages.Clear();
    ChatStatus = ModelStatus();
  }

  [RelayCommand]
  private void AskAboutSelection() {
    var selection = _selection();
    var rows = selection.Targets;

    if (rows.Count == 0) {
      ChatInput = "What is currently unhealthy in this cluster?";

      return;
    }

    if (rows.Count > 1) {
      var kind = selection.DescriptorTitle ?? "resources";
      var names = string.Join(", ", rows.Select(ResourceActionBatch.Label));
      ChatInput = $"What is wrong with these {kind}: {names}?";

      return;
    }

    var name = selection.RelatedPodName ?? rows[0].Name;
    var container = string.IsNullOrEmpty(selection.ContainerName) ? "" : $" container {selection.ContainerName}";
    ChatInput = $"What is wrong with {selection.DocumentKind ?? "this resource"} {name}{container}?";
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

    var history = _history.ToList();
    history.Add(new OllamaChatMessage { Role = "user", Content = prompt });
    var context = BuildContext();
    var ai = _configuration.Current.Ai;

    try {
      var agent = ai.Enabled && ai.AgentEnabled;
      var result = await _chat.AskAsync(
        ai.Endpoint,
        ai.Model,
        history,
        context,
        status => Dispatcher.UIThread.Post(() => {
          ChatStatus = ModelStatus(Activity(status));

          if (status.StartsWith("Tool · ", StringComparison.Ordinal))
            ChatMessages.Add(new ChatMessageViewModel { Role = "tool", Text = status["Tool · ".Length..] });
        }),
        token,
        agent,
        agent ? ConfirmToolAsync : null,
        ai.TimeoutSeconds);

      if (!result.IsSuccess) {
        var error = string.Join("; ", result.Messages);
        ChatMessages.Add(new ChatMessageViewModel { Role = "assistant", Text = error });
        ChatStatus = ModelStatus(error);

        return;
      }

      var answer = result.Value ?? "";
      _history.Add(new OllamaChatMessage { Role = "user", Content = prompt });
      _history.Add(new OllamaChatMessage { Role = "assistant", Content = answer });
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

  private ClusterChatContext BuildContext() {
    var selection = _selection();

    return new ClusterChatContext(
      selection.ContextName,
      selection.Namespace,
      selection.Kind,
      selection.ResourceName,
      selection.PodName,
      selection.ContainerName,
      selection.Overview,
      selection.Events,
      selection.LogText);
  }
}
