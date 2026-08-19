using MaksIT.ClusterConsole.Client;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public sealed class ClusterChatService(IOllamaChatClient ollama, ClusterWorkspace workspace) {
  public const string DefaultModel = "qwen3:8b";
  public const string DefaultEndpoint = "http://127.0.0.1:11434";
  private const int MaxToolRounds = 5;

  private readonly ClusterChatTools _tools = new(workspace);

  public async Task<Result<string>> AskAsync(
    string endpoint,
    string model,
    IReadOnlyList<OllamaChatMessage> history,
    ClusterChatContext context,
    Action<string>? status,
    CancellationToken cancellationToken = default) {
    var ready = await EnsureModelAsync(endpoint, model, cancellationToken).ConfigureAwait(false);
    if (!ready.IsSuccess)
      return new Result<string>(null, false, ready.Messages, ready.StatusCode);

    var messages = new List<OllamaChatMessage> {
      new() { Role = "system", Content = context.SystemPrompt() }
    };
    messages.AddRange(history);

    for (var round = 0; round < MaxToolRounds; round++) {
      status?.Invoke($"Ollama · {model}…");
      var request = new OllamaChatRequest {
        Model = model,
        Messages = messages,
        Tools = _tools.Definitions,
        Stream = false,
        Think = false,
        KeepAlive = "5m",
        Options = new OllamaChatOptions { Temperature = 0.2, NumCtx = 8192 }
      };
      var chat = await ollama.ChatAsync(endpoint, request, cancellationToken).ConfigureAwait(false);
      if (!chat.IsSuccess || chat.Value?.Message is null)
        return chat.IsSuccess
          ? Result<string>.UnprocessableEntity(null, "Ollama returned no message.")
          : new Result<string>(null, false, chat.Messages, chat.StatusCode);

      var message = chat.Value.Message;
      messages.Add(message);
      var calls = message.ToolCalls ?? [];
      if (calls.Count == 0) {
        var text = ClusterChatContext.StripThink(message.Content);
        return string.IsNullOrWhiteSpace(text)
          ? Result<string>.UnprocessableEntity(null, "The model returned an empty answer. Try again, or `ollama pull qwen3:8b`.")
          : Result<string>.Ok(text);
      }

      foreach (var call in calls) {
        var name = call.Function?.Name;
        if (string.IsNullOrWhiteSpace(name))
          continue;

        status?.Invoke($"Tool · {name}");
        var args = ClusterChatTools.ParseArguments(call.Function?.Arguments ?? default);
        var output = await _tools.InvokeAsync(name, args, context, cancellationToken).ConfigureAwait(false);
        messages.Add(new OllamaChatMessage {
          Role = "tool",
          ToolName = name,
          Content = output
        });
      }
    }

    return Result<string>.UnprocessableEntity(null, "Stopped after too many tool calls. Ask a narrower question.");
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
