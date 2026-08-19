using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

public sealed class OllamaChatClient(HttpClient http) : IOllamaChatClient {
  private static readonly JsonSerializerOptions JsonOptions = new() {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
  };

  public async Task<Result<IReadOnlyList<string>>> ListModelsAsync(
    string endpoint,
    CancellationToken cancellationToken = default) {
    try {
      var response = await http.GetAsync(Combine(endpoint, "/api/tags"), cancellationToken).ConfigureAwait(false);
      var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
        return Result<IReadOnlyList<string>>.InternalServerError(null, OllamaError(response.StatusCode, body));

      var tags = JsonSerializer.Deserialize<OllamaTagsResponse>(body, JsonOptions);
      var names = (tags?.Models ?? [])
        .Select(m => m.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!)
        .ToList();
      return Result<IReadOnlyList<string>>.Ok(names);
    }
    catch (Exception ex) {
      return Result<IReadOnlyList<string>>.InternalServerError(null, OllamaUnavailable(endpoint, ex));
    }
  }

  public async Task<Result<OllamaChatResponse>> ChatAsync(
    string endpoint,
    OllamaChatRequest request,
    CancellationToken cancellationToken = default) {
    try {
      using var response = await http.PostAsJsonAsync(
        Combine(endpoint, "/api/chat"),
        request,
        JsonOptions,
        cancellationToken).ConfigureAwait(false);
      var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
        return Result<OllamaChatResponse>.InternalServerError(null, OllamaError(response.StatusCode, body));

      var parsed = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOptions);
      if (parsed is null)
        return Result<OllamaChatResponse>.UnprocessableEntity(null, "Ollama returned an empty chat response.");

      if (!string.IsNullOrWhiteSpace(parsed.Error))
        return Result<OllamaChatResponse>.InternalServerError(null, parsed.Error);

      return Result<OllamaChatResponse>.Ok(parsed);
    }
    catch (Exception ex) {
      return Result<OllamaChatResponse>.InternalServerError(null, OllamaUnavailable(endpoint, ex));
    }
  }

  private static string Combine(string endpoint, string path) {
    var root = string.IsNullOrWhiteSpace(endpoint) ? "http://127.0.0.1:11434" : endpoint.TrimEnd('/');
    return root + path;
  }

  private static string OllamaUnavailable(string endpoint, Exception ex) =>
    $"Cannot reach Ollama at {endpoint}. Start Ollama, then `ollama pull qwen3:8b`. {ex.Message}";

  private static string OllamaError(System.Net.HttpStatusCode status, string body) {
    if (string.IsNullOrWhiteSpace(body))
      return $"Ollama HTTP {(int)status}.";

    try {
      using var doc = JsonDocument.Parse(body);
      if (doc.RootElement.TryGetProperty("error", out var error))
        return error.GetString() ?? body;
    }
    catch {
    }

    return body.Length > 400 ? body[..400] : body;
  }
}
