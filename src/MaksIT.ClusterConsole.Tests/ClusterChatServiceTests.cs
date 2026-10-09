using System.Text.Json;
using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Shared.Chat;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.Tests;

public class ClusterChatServiceTests {
  [Fact]
  public async Task Ask_runs_a_tool_then_returns_the_diagnosis() {
    var session = new FakeClusterSession { LogsText = "bind: address already in use" };
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var ollama = new SequencedOllama([
      ToolReply("get_logs", """{"pod":"web-0","namespace":"apps","tailLines":12}"""),
      TextReply("The container cannot bind port 8080.")
    ]);
    var chat = new ClusterChatService(ollama, workspace);

    var answer = await chat.AskAsync(
      "http://127.0.0.1:11434",
      "qwen3:8b",
      [new OllamaChatMessage { Role = "user", Content = "why is it down?" }],
      Context(),
      _ => { },
      TestContext.Current.CancellationToken);

    Assert.True(answer.IsSuccess, string.Join("; ", answer.Messages));
    Assert.Equal("The container cannot bind port 8080.", answer.Value);
    Assert.Equal(12, session.LastTail);
    Assert.Contains(ollama.Requests[1].Messages, message =>
      message.Role == "tool" && message.Content.Contains("address already in use", StringComparison.Ordinal));
    Assert.Equal(ClusterChatService.ReadOnlyContext, ollama.Requests[0].Options!.NumCtx);
  }

  [Fact]
  public async Task Ask_reads_a_tool_call_embedded_in_the_answer() {
    var session = new FakeClusterSession { Gotten = JsonNode.Parse("""{ "kind": "Service", "metadata": { "name": "web" } }""") as JsonObject };
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var ollama = new SequencedOllama([
      TextReply("""
        <think>look it up</think>
        <tool_call>
        <function=get_resource>
        <parameter=kind>
        Service
        </parameter>
        <parameter=name>
        web
        </parameter>
        </function>
        </tool_call>
        """),
      TextReply("The Service exists.")
    ]);
    var chat = new ClusterChatService(ollama, workspace);
    var answer = await chat.AskAsync("http://127.0.0.1:11434", "qwen3:8b", [], Context(), null, TestContext.Current.CancellationToken);

    Assert.Equal("The Service exists.", answer.Value);
    Assert.Equal(("Service", "web", "apps"), session.LastGet);
  }

  [Fact]
  public async Task Ask_reports_a_missing_model_an_empty_answer_and_a_chat_failure() {
    var workspace = new ClusterWorkspace();
    var missing = new SequencedOllama([]) {
      Models = Result<IReadOnlyList<string>>.Ok((IReadOnlyList<string>)["llama3"])
    };
    var notPulled = await new ClusterChatService(missing, workspace).AskAsync(
      "http://127.0.0.1:11434", "qwen3:8b", [], Context(), null, TestContext.Current.CancellationToken);
    Assert.False(notPulled.IsSuccess);
    Assert.Contains("not pulled", notPulled.Messages[0], StringComparison.OrdinalIgnoreCase);

    var empty = new SequencedOllama([TextReply("   ")]);
    var blank = await new ClusterChatService(empty, workspace).AskAsync(
      "http://127.0.0.1:11434", "qwen3:8b", [], Context(), null, TestContext.Current.CancellationToken);
    Assert.Contains("empty answer", blank.Messages[0], StringComparison.OrdinalIgnoreCase);

    var failed = new SequencedOllama([]) {
      ChatError = Result<OllamaChatResponse>.InternalServerError(null, "connection refused")
    };
    var down = await new ClusterChatService(failed, workspace).AskAsync(
      "http://127.0.0.1:11434", "qwen3:8b", [], Context(), null, TestContext.Current.CancellationToken);
    Assert.Contains("connection refused", down.Messages[0], StringComparison.Ordinal);
  }

  [Fact]
  public async Task Ask_maps_an_ollama_stall_to_the_timeout_message() {
    var ollama = new SequencedOllama([]) { CancelChat = true };
    var chat = new ClusterChatService(ollama, new ClusterWorkspace());
    var stalled = await chat.AskAsync(
      "http://127.0.0.1:11434", "qwen3:8b", [], Context(), null, TestContext.Current.CancellationToken, timeoutSeconds: 5);

    Assert.Contains("did not answer within 5 seconds", stalled.Messages[0], StringComparison.Ordinal);

    using var canceled = new CancellationTokenSource();
    await canceled.CancelAsync();
    ollama.HonorCallerCancel = true;
    ollama.CancelChat = false;
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chat.AskAsync(
      "http://127.0.0.1:11434", "qwen3:8b", [], Context(), null, canceled.Token));
  }

  [Fact]
  public async Task Agent_restart_runs_only_after_approval() {
    var session = new FakeClusterSession();
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var approved = new SequencedOllama([
      ToolReply("restart_workload", """{"kind":"Deployment","name":"web","namespace":"apps"}"""),
      TextReply("Restarted the deployment.")
    ]);
    var chat = new ClusterChatService(approved, workspace);
    var answer = await chat.AskAsync(
      "http://127.0.0.1:11434",
      "qwen3:8b",
      [],
      Context(),
      null,
      TestContext.Current.CancellationToken,
      agent: true,
      approve: (_, _, _) => Task.FromResult(true));

    Assert.Equal("Restarted the deployment.", answer.Value);
    Assert.Equal(1, session.Restarts);
    Assert.Equal(ClusterChatService.AgentContext, approved.Requests[0].Options!.NumCtx);

    var rejected = new SequencedOllama([
      ToolReply("restart_workload", """{"kind":"Deployment","name":"web","namespace":"apps"}"""),
      TextReply("Left it alone.")
    ]);
    answer = await new ClusterChatService(rejected, workspace).AskAsync(
      "http://127.0.0.1:11434",
      "qwen3:8b",
      [],
      Context(),
      null,
      TestContext.Current.CancellationToken,
      agent: true,
      approve: (_, _, _) => Task.FromResult(false));
    Assert.Equal("Left it alone.", answer.Value);
    Assert.Equal(1, session.Restarts);
    Assert.Contains(rejected.Requests[1].Messages, message =>
      message.Role == "tool" && message.Content.Contains(ClusterChatTools.RejectedByOperator, StringComparison.Ordinal));
  }

  [Fact]
  public async Task AssessDrain_rejects_an_empty_note() {
    var ollama = new SequencedOllama([TextReply("<think>only</think>")]);
    var note = await new ClusterChatService(ollama, new ClusterWorkspace()).AssessDrainAsync(
      "http://127.0.0.1:11434", "qwen3:8b", "node-a", TestContext.Current.CancellationToken);

    Assert.Contains("did not write a drain note", note.Messages[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Tools_read_logs_events_and_resources_or_explain_why_not() {
    var tools = new ClusterChatTools(new ClusterWorkspace());
    var context = Context();
    Assert.Equal("Not connected to a cluster.", await tools.InvokeAsync("get_logs", new JsonObject(), context, true, TestContext.Current.CancellationToken));
    Assert.Equal("Unknown tool 'explode'.", await tools.InvokeAsync("explode", new JsonObject(), context, true, TestContext.Current.CancellationToken));

    var session = new FakeClusterSession {
      LogsText = "ready",
      Gotten = JsonNode.Parse("""{ "kind": "Pod", "metadata": { "name": "web-0" } }""") as JsonObject
    };
    session.Set("events", JsonNode.Parse("""
      {
        "metadata": { "name": "ev1", "namespace": "apps" },
        "involvedObject": { "name": "web" },
        "type": "Warning",
        "reason": "BackOff",
        "message": "back-off restarting"
      }
      """) as JsonObject ?? throw new InvalidOperationException("expected object"));
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    tools = new ClusterChatTools(workspace);

    var logs = await tools.InvokeAsync("get_logs", new JsonObject { ["pod"] = "web-0" }, context, true, TestContext.Current.CancellationToken);
    Assert.Equal("ready", logs);

    var events = await tools.InvokeAsync("get_events", new JsonObject { ["name"] = "web" }, context, true, TestContext.Current.CancellationToken);
    Assert.Contains("BackOff", events, StringComparison.Ordinal);

    var resource = await tools.InvokeAsync("get_resource", new JsonObject(), context, true, TestContext.Current.CancellationToken);
    Assert.Contains("kind: Pod", resource, StringComparison.Ordinal);

    var deleted = await tools.InvokeAsync(
      "delete_pod",
      new JsonObject { ["name"] = "web-0", ["namespace"] = "apps" },
      context,
      true,
      TestContext.Current.CancellationToken);
    Assert.Contains("Deleted Pod/web-0", deleted, StringComparison.Ordinal);

    var scaled = await tools.InvokeAsync(
      "scale_workload",
      new JsonObject { ["kind"] = "Deployment", ["name"] = "web", ["namespace"] = "apps", ["replicas"] = "2" },
      context,
      true,
      TestContext.Current.CancellationToken);
    Assert.Contains("Scaled Deployment/web to 2", scaled, StringComparison.Ordinal);
    Assert.Equal(2, session.LastReplicas);

    Assert.Equal("get_logs needs a pod name. Select a pod in the UI or pass pod.", await tools.InvokeAsync(
      "get_logs", new JsonObject(), new ClusterChatContext("c", "apps", "Deployment", "web", null, null, "", "", ""), true, TestContext.Current.CancellationToken));
    Assert.Equal("No events found for missing.", await tools.InvokeAsync(
      "get_events", new JsonObject { ["name"] = "missing" }, context, true, TestContext.Current.CancellationToken));
    Assert.StartsWith("Unknown kind", await tools.InvokeAsync(
      "get_resource", new JsonObject { ["kind"] = "NoSuch", ["name"] = "x" }, context, true, TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Tools_describe_changes_and_truncate_long_results() {
    var tools = new ClusterChatTools(new ClusterWorkspace());
    Assert.Equal("Restart Deployment/web in apps.", tools.Describe("restart_workload", Args()));
    Assert.Equal("Delete Pod/web in apps. The controller can recreate it.", tools.Describe("delete_pod", Args()));
    Assert.Equal("Scale Deployment/web in apps to 2 replicas.", tools.Describe("scale_workload", new JsonObject {
      ["kind"] = "Deployment", ["name"] = "web", ["namespace"] = "apps", ["replicas"] = 2
    }));
    Assert.Contains("Apply ConfigMap/app", tools.Describe("apply_manifest", new JsonObject { ["yaml"] = Manifest() }), StringComparison.Ordinal);
    Assert.Equal("Apply a manifest.", tools.Describe("apply_manifest", new JsonObject()));
    Assert.Equal("No YAML was provided. Nothing will be applied.", tools.ChangePreview("apply_manifest", new JsonObject()));
    Assert.Equal("The YAML could not be parsed. Nothing will be applied.", tools.ChangePreview("apply_manifest", new JsonObject { ["yaml"] = "[]" }));

    var huge = new JsonObject { ["yaml"] = "key: " + new string('a', 7000) };
    Assert.Contains("truncated", tools.ChangePreview("apply_manifest", huge), StringComparison.Ordinal);

    var session = new FakeClusterSession { LogsText = new string('x', ClusterChatTools.MaxResultChars + 20) };
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var text = await new ClusterChatTools(workspace).InvokeAsync(
      "get_logs",
      new JsonObject { ["pod"] = "web-0" },
      Context(),
      true,
      TestContext.Current.CancellationToken);
    Assert.Contains("truncated", text, StringComparison.Ordinal);

    Assert.Empty(ClusterChatTools.ParseArguments(default));
  }

  [Fact]
  public async Task Issues_tool_prints_warnings_or_says_the_cluster_is_clear() {
    var session = new FakeClusterSession();
    session.Set("nodes", JsonNode.Parse("""
      {
        "metadata": { "name": "n1", "uid": "n1", "creationTimestamp": "2026-08-19T10:00:00Z" },
        "status": { "conditions": [
          { "type": "Ready", "status": "True" },
          { "type": "MemoryPressure", "status": "True", "message": "kubelet has memory pressure" }
        ] }
      }
      """) as JsonObject ?? throw new InvalidOperationException("expected object"));
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var tools = new ClusterChatTools(workspace);
    var text = await tools.InvokeAsync("get_cluster_issues", new JsonObject(), Context(), true, TestContext.Current.CancellationToken);
    Assert.Contains("memory pressure", text, StringComparison.OrdinalIgnoreCase);

    session.Set("nodes");
    text = await tools.InvokeAsync("get_cluster_issues", new JsonObject(), Context(), true, TestContext.Current.CancellationToken);
    Assert.Contains("No cluster warnings", text, StringComparison.Ordinal);
  }

  private static ClusterChatContext Context() =>
    new("homelab", "apps", "Pod", "web-0", "web-0", "app", "", "", "");

  private static JsonObject Args() =>
    new() { ["kind"] = "Deployment", ["name"] = "web", ["namespace"] = "apps" };

  private static string Manifest() => """
    apiVersion: v1
    kind: ConfigMap
    metadata:
      name: app
      namespace: default
    data:
      key: value
    """;

  private static OllamaChatResponse TextReply(string content) =>
    new() { Message = new OllamaChatMessage { Role = "assistant", Content = content } };

  private static OllamaChatResponse ToolReply(string name, string arguments) =>
    new() {
      Message = new OllamaChatMessage {
        Role = "assistant",
        ToolCalls = [
          new OllamaToolCall {
            Function = new OllamaToolCallFunction {
              Name = name,
              Arguments = JsonDocument.Parse(arguments).RootElement.Clone()
            }
          }
        ]
      }
    };

  private sealed class SequencedOllama(IEnumerable<OllamaChatResponse> replies) : IOllamaChatClient {
    private readonly Queue<OllamaChatResponse> _replies = new(replies);

    public List<OllamaChatRequest> Requests { get; } = [];

    public Result<IReadOnlyList<string>> Models { get; set; } =
      Result<IReadOnlyList<string>>.Ok((IReadOnlyList<string>)["qwen3:8b"]);

    public Result<OllamaChatResponse>? ChatError { get; set; }

    public bool CancelChat { get; set; }

    public bool HonorCallerCancel { get; set; }

    public Task<Result<IReadOnlyList<string>>> ListModelsAsync(
      string endpoint,
      CancellationToken cancellationToken = default) {
      if (HonorCallerCancel)
        cancellationToken.ThrowIfCancellationRequested();

      return Task.FromResult(Models);
    }

    public Task<Result<OllamaChatResponse>> ChatAsync(
      string endpoint,
      OllamaChatRequest request,
      CancellationToken cancellationToken = default) {
      Requests.Add(request);

      if (HonorCallerCancel)
        cancellationToken.ThrowIfCancellationRequested();

      if (CancelChat)
        throw new OperationCanceledException();

      if (ChatError is not null)
        return Task.FromResult(ChatError);

      return Task.FromResult(Result<OllamaChatResponse>.Ok(_replies.Dequeue()));
    }
  }
}
