using MaksIT.Results;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.Shared.Chat;

public sealed class ClusterChatService(IOllamaChatClient ollama, ClusterWorkspace workspace) {
  public const string DefaultModel = "qwen3:8b";
  public const string DefaultEndpoint = "http://127.0.0.1:11434";
  public const int ReadOnlyContext = 8192;
  public const int AgentContext = 16384;
  private const int MaxToolRounds = 8;
  private const int MaxAgentRounds = 12;

  private readonly ClusterChatTools _tools = new(workspace);

  public async Task<Result<string>> AskAsync(
    string endpoint,
    string model,
    IReadOnlyList<OllamaChatMessage> history,
    ClusterChatContext context,
    Action<string>? status,
    CancellationToken cancellationToken = default,
    bool agent = false,
    Func<string, string, CancellationToken, Task<bool>>? approve = null) {
    var ready = await EnsureModelAsync(endpoint, model, cancellationToken).ConfigureAwait(false);
    if (!ready.IsSuccess)
      return new Result<string>(null, false, ready.Messages, ready.StatusCode);

    var messages = new List<OllamaChatMessage> {
      new() { Role = "system", Content = context.SystemPrompt(agent) }
    };
    messages.AddRange(history);
    var rounds = agent ? MaxAgentRounds : MaxToolRounds;
    var tools = agent ? _tools.AgentDefinitions : _tools.ReadOnlyDefinitions;

    for (var round = 0; round < rounds; round++) {
      status?.Invoke("Processing");
      var request = new OllamaChatRequest {
        Model = model,
        Messages = messages,
        Tools = tools,
        Stream = false,
        Think = false,
        KeepAlive = "5m",
        Options = new OllamaChatOptions { Temperature = 0.2, NumCtx = agent ? AgentContext : ReadOnlyContext }
      };
      var chat = await ollama.ChatAsync(endpoint, request, cancellationToken).ConfigureAwait(false);
      if (!chat.IsSuccess || chat.Value?.Message is null)
        return chat.IsSuccess
          ? Result<string>.UnprocessableEntity(null, "Ollama returned no message.")
          : new Result<string>(null, false, chat.Messages, chat.StatusCode);

      var message = chat.Value.Message;
      var calls = message.ToolCalls?
        .Where(call => !string.IsNullOrWhiteSpace(call.Function?.Name))
        .ToList() ?? [];
      if (calls.Count == 0)
        calls = [.. ClusterChatToolMarkup.Parse(message.Content)];

      message.Content = ClusterChatToolMarkup.Strip(ClusterChatContext.StripThink(message.Content));
      message.ToolCalls = calls.Count == 0 ? null : calls;
      messages.Add(message);
      if (calls.Count == 0) {
        var text = message.Content;
        return string.IsNullOrWhiteSpace(text)
          ? Result<string>.UnprocessableEntity(null, "The model returned an empty answer. Try again, or `ollama pull qwen3:8b`.")
          : Result<string>.Ok(text);
      }

      foreach (var call in calls) {
        var name = call.Function?.Name;
        if (string.IsNullOrWhiteSpace(name))
          continue;

        var args = ClusterChatTools.ParseArguments(call.Function?.Arguments ?? default);
        var allow = !ClusterChatTools.IsMutating(name);
        if (!allow && agent && approve is not null) {
          var description = _tools.Describe(name, args);
          var change = _tools.ChangePreview(name, args);
          status?.Invoke($"Confirm · {description}");
          allow = await approve(description, change, cancellationToken).ConfigureAwait(false);
        }

        if (allow)
          status?.Invoke($"Tool · {name}");

        var output = await _tools.InvokeAsync(name, args, context, allow, cancellationToken).ConfigureAwait(false);
        messages.Add(new OllamaChatMessage {
          Role = "tool",
          ToolName = name,
          Content = output
        });
      }
    }

    messages.Add(new OllamaChatMessage {
      Role = "user",
      Content = "Stop calling tools. Answer the original question from the tool results above. Say what is healthy, what is failing, and whether a change was applied."
    });
    return await ConcludeAsync(endpoint, model, messages, agent, status, cancellationToken).ConfigureAwait(false);
  }

  private async Task<Result<string>> ConcludeAsync(
    string endpoint,
    string model,
    List<OllamaChatMessage> messages,
    bool agent,
    Action<string>? status,
    CancellationToken cancellationToken) {
    status?.Invoke("Processing");
    var request = new OllamaChatRequest {
      Model = model,
      Messages = messages,
      Stream = false,
      Think = false,
      KeepAlive = "5m",
      Options = new OllamaChatOptions { Temperature = 0.2, NumCtx = agent ? AgentContext : ReadOnlyContext }
    };
    var chat = await ollama.ChatAsync(endpoint, request, cancellationToken).ConfigureAwait(false);
    if (!chat.IsSuccess || chat.Value?.Message is null)
      return chat.IsSuccess
        ? Result<string>.UnprocessableEntity(null, "Ollama returned no message.")
        : new Result<string>(null, false, chat.Messages, chat.StatusCode);

    var text = ClusterChatToolMarkup.Strip(ClusterChatContext.StripThink(chat.Value.Message.Content));
    return string.IsNullOrWhiteSpace(text)
      ? Result<string>.UnprocessableEntity(null, "The check gathered tool results but the model did not write a diagnosis.")
      : Result<string>.Ok(text);
  }

  public async Task<Result> EnsureModelAsync(
    string endpoint,
    string model,
    CancellationToken cancellationToken = default) {
    var listed = await ollama.ListModelsAsync(endpoint, cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return listed.ToResult();

    var names = listed.Value ?? [];
    if (HasModel(names, model))
      return Result.Ok();

    var available = names.Count == 0 ? "(none)" : string.Join(", ", names.Take(12));
    return Result.NotFound(
      $"Ollama is running, but '{model}' is not pulled. Run `ollama pull {model}`. Installed: {available}.");
  }

  public static bool HasModel(IEnumerable<string> installed, string model) {
    foreach (var name in installed) {
      if (name.Equals(model, StringComparison.OrdinalIgnoreCase)
          || name.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase)
          || model.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }
}
