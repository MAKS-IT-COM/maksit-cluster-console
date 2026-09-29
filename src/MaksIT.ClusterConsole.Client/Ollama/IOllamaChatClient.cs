using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client.Ollama;

public interface IOllamaChatClient {
  Task<Result<IReadOnlyList<string>>> ListModelsAsync(
    string endpoint,
    CancellationToken cancellationToken = default);

  Task<Result<OllamaChatResponse>> ChatAsync(
    string endpoint,
    OllamaChatRequest request,
    CancellationToken cancellationToken = default);
}
