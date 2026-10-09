using System.Net;
using System.Text;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.Tests;

public class OllamaChatClientTests {
  [Fact]
  public async Task ListModels_reads_names_and_skips_blanks() {
    var handler = new ScriptHandler(_ => Json(HttpStatusCode.OK, """
      { "models": [{ "name": "qwen3:8b" }, { "name": " " }, { "name": "llama3" }] }
      """));
    var client = new OllamaChatClient(new HttpClient(handler));

    var listed = await client.ListModelsAsync("http://127.0.0.1:11434/", TestContext.Current.CancellationToken);

    Assert.Equal(["qwen3:8b", "llama3"], listed.Value);
    Assert.Equal("http://127.0.0.1:11434/api/tags", handler.LastRequest!.RequestUri!.ToString());
  }

  [Fact]
  public async Task ListModels_surfaces_http_errors_and_unreachable_hosts() {
    var handler = new ScriptHandler(_ => Json(HttpStatusCode.NotFound, """{ "error": "model runner stopped" }"""));
    var client = new OllamaChatClient(new HttpClient(handler));
    var missing = await client.ListModelsAsync("", TestContext.Current.CancellationToken);
    Assert.Equal("model runner stopped", missing.Messages[0]);
    Assert.StartsWith("http://127.0.0.1:11434/api/tags", handler.LastRequest!.RequestUri!.ToString(), StringComparison.Ordinal);

    handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("") };
    var empty = await client.ListModelsAsync("http://ollama", TestContext.Current.CancellationToken);
    Assert.Contains("HTTP 502", empty.Messages[0], StringComparison.Ordinal);

    handler.Respond = _ => Json(HttpStatusCode.InternalServerError, new string('x', 500));
    var clipped = await client.ListModelsAsync("http://ollama", TestContext.Current.CancellationToken);
    Assert.Equal(400, clipped.Messages[0].Length);

    handler.Respond = _ => throw new HttpRequestException("refused");
    var down = await client.ListModelsAsync("http://ollama", TestContext.Current.CancellationToken);
    Assert.Contains("Cannot reach Ollama", down.Messages[0], StringComparison.Ordinal);
    Assert.Contains("refused", down.Messages[0], StringComparison.Ordinal);
  }

  [Fact]
  public async Task Chat_returns_the_message_or_the_ollama_error() {
    var handler = new ScriptHandler(_ => Json(HttpStatusCode.OK, """
      { "message": { "role": "assistant", "content": "the pod is pending" } }
      """));
    var client = new OllamaChatClient(new HttpClient(handler));
    var request = new OllamaChatRequest {
      Model = "qwen3:8b",
      Messages = [new OllamaChatMessage { Role = "user", Content = "status?" }]
    };

    var chat = await client.ChatAsync("http://127.0.0.1:11434", request, TestContext.Current.CancellationToken);
    Assert.Equal("the pod is pending", chat.Value!.Message!.Content);
    Assert.Equal("http://127.0.0.1:11434/api/chat", handler.LastRequest!.RequestUri!.ToString());

    handler.Respond = _ => Json(HttpStatusCode.OK, """{ "error": "model not found" }""");
    var failed = await client.ChatAsync("http://127.0.0.1:11434", request, TestContext.Current.CancellationToken);
    Assert.Equal("model not found", failed.Messages[0]);

    handler.Respond = _ => Json(HttpStatusCode.OK, "null");
    var empty = await client.ChatAsync("http://127.0.0.1:11434", request, TestContext.Current.CancellationToken);
    Assert.Contains("empty chat response", empty.Messages[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Cancellation_is_not_turned_into_an_error_result() {
    var handler = new ScriptHandler(_ => Json(HttpStatusCode.OK, "{}"));
    var client = new OllamaChatClient(new HttpClient(handler));
    using var canceled = new CancellationTokenSource();
    await canceled.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
      client.ListModelsAsync("http://127.0.0.1:11434", canceled.Token));
  }

  private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
    new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

  private sealed class ScriptHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      LastRequest = request;
      cancellationToken.ThrowIfCancellationRequested();

      return Task.FromResult(Respond(request));
    }
  }
}
