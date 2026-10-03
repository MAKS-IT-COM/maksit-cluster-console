using System.Text.Json;
using System.Text.Json.Nodes;
using k8s;
using MaksIT.Results;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Shared.Chat;
using MaksIT.ClusterConsole.Client.Ollama;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.Tests;

public class ClusterChatTests {
  [Fact]
  public void StripThink_removes_qwen_reasoning_blocks() {
    var text = ClusterChatContext.StripThink("<think>plan</think>\nThe pod is CrashLooping.");
    Assert.Equal("The pod is CrashLooping.", text);
  }

  [Fact]
  public void Qwen_markup_becomes_a_get_resource_call_and_leaves_the_prose() {
    var raw = """
      I need to check the current service configuration to understand how to change the external IP address.
      <tool_call>
      <function=get_resource>
      <parameter=kind>
      Service
      </parameter>
      <parameter=name>
      maksit-reactredux-bgp
      </parameter>
      <parameter=namespace>
      maksit-reactredux
      </parameter>
      </function>
      </tool_call>
      """;

    var calls = ClusterChatToolMarkup.Parse(raw);
    var call = Assert.Single(calls);
    Assert.Equal("get_resource", call.Function?.Name);
    var args = ClusterChatTools.ParseArguments(call.Function!.Arguments);
    Assert.Equal("Service", args["kind"]?.ToString());
    Assert.Equal("maksit-reactredux-bgp", args["name"]?.ToString());
    Assert.Equal("maksit-reactredux", args["namespace"]?.ToString());

    var visible = ClusterChatToolMarkup.Strip(raw);
    Assert.DoesNotContain("<function", visible, StringComparison.Ordinal);
    Assert.DoesNotContain("tool_call", visible, StringComparison.Ordinal);
    Assert.Contains("external IP", visible, StringComparison.Ordinal);
  }

  [Fact]
  public void Qwen_markup_without_the_opening_tool_call_tag_still_parses() {
    var raw = """
      <function=get_resource>
      <parameter=kind>
      Service
      </parameter>
      </function>
      </tool_call>
      """;

    var call = Assert.Single(ClusterChatToolMarkup.Parse(raw));
    Assert.Equal("get_resource", call.Function?.Name);
    Assert.Equal("", ClusterChatToolMarkup.Strip(raw));
  }

  [Fact]
  public void Plain_answer_has_no_embedded_tool_call() {
    const string raw = "The Service has no external IP.";
    Assert.Empty(ClusterChatToolMarkup.Parse(raw));
    Assert.Equal(raw, ClusterChatToolMarkup.Strip(raw));
  }

  [Fact]
  public void SystemPrompt_includes_selection_and_forbids_writes() {
    var prompt = new ClusterChatContext(
      "homelab",
      "kube-system",
      "Pod",
      "hubble-ui-1",
      "hubble-ui-1",
      "frontend",
      "Status: CrashLoopBackOff",
      "BackOff restarting",
      "listen tcp :8080: bind: address already in use").SystemPrompt();

    Assert.Contains("homelab", prompt, StringComparison.Ordinal);
    Assert.Contains("hubble-ui-1", prompt, StringComparison.Ordinal);
    Assert.Contains("frontend", prompt, StringComparison.Ordinal);
    Assert.Contains("CrashLoopBackOff", prompt, StringComparison.Ordinal);
    Assert.Contains("You can only read the cluster", prompt, StringComparison.Ordinal);
  }

  [Fact]
  public void AgentPrompt_allows_repairs_and_drops_the_read_only_rule() {
    var prompt = new ClusterChatContext("homelab", "apps", "Deployment", "web", null, null, "", "", "")
      .SystemPrompt(agent: true);

    Assert.DoesNotContain("You can only read the cluster", prompt, StringComparison.Ordinal);
    Assert.Contains("restart_workload", prompt, StringComparison.Ordinal);
    Assert.Contains("apply_manifest", prompt, StringComparison.Ordinal);
    Assert.Contains("continue and answer the original question", prompt, StringComparison.Ordinal);
  }

  [Fact]
  public void ReadOnly_definitions_omit_write_tools() {
    var tools = new ClusterChatTools(new ClusterWorkspace());
    Assert.DoesNotContain(tools.ReadOnlyDefinitions, tool => tool.Function.Name == "restart_workload");
    Assert.Contains(tools.AgentDefinitions, tool => tool.Function.Name == "restart_workload");
    Assert.Contains(tools.AgentDefinitions, tool => tool.Function.Name == "apply_manifest");
  }

  [Fact]
  public async Task Rejected_restart_and_apply_do_not_call_the_session() {
    var (tools, session) = await ConnectedAsync();
    var context = SampleContext();
    var restart = new JsonObject { ["kind"] = "Deployment", ["name"] = "web", ["namespace"] = "apps" };
    var rejected = await tools.InvokeAsync("restart_workload", restart, context, approved: false, CancellationToken.None);
    Assert.Equal(ClusterChatTools.RejectedByOperator, rejected);
    Assert.Equal(0, session.Restarts);

    var apply = new JsonObject { ["yaml"] = ManifestYaml() };
    rejected = await tools.InvokeAsync("apply_manifest", apply, context, approved: false, CancellationToken.None);
    Assert.Equal(ClusterChatTools.RejectedByOperator, rejected);
    Assert.Equal(0, session.Applies);
  }

  [Fact]
  public async Task Approved_restart_calls_the_session_once() {
    var (tools, session) = await ConnectedAsync();
    var args = new JsonObject { ["kind"] = "Deployment", ["name"] = "web", ["namespace"] = "apps" };
    var text = await tools.InvokeAsync("restart_workload", args, SampleContext(), approved: true, CancellationToken.None);
    Assert.Contains("Restarted Deployment/web", text, StringComparison.Ordinal);
    Assert.Equal(1, session.Restarts);
  }

  [Fact]
  public async Task Scale_outside_0_to_100_does_not_call_the_session() {
    var (tools, session) = await ConnectedAsync();
    var args = new JsonObject {
      ["kind"] = "Deployment",
      ["name"] = "web",
      ["namespace"] = "apps",
      ["replicas"] = 101
    };
    var text = await tools.InvokeAsync("scale_workload", args, SampleContext(), approved: true, CancellationToken.None);
    Assert.Contains("0 to 100", text, StringComparison.Ordinal);
    Assert.Equal(0, session.Scales);
  }

  [Fact]
  public void ChangePreview_shows_the_manifest_that_will_be_applied() {
    var tools = new ClusterChatTools(new ClusterWorkspace());
    var preview = tools.ChangePreview("apply_manifest", new JsonObject { ["yaml"] = ManifestYaml() });
    Assert.Contains("kind: ConfigMap", preview, StringComparison.Ordinal);
    Assert.Contains("key: value", preview, StringComparison.Ordinal);
    Assert.DoesNotContain("phase: Ready", preview, StringComparison.Ordinal);
    Assert.Equal("replicas: 3", tools.ChangePreview("scale_workload", new JsonObject { ["replicas"] = 3 }));
    Assert.Equal("", tools.ChangePreview("restart_workload", new JsonObject()));
  }

  [Fact]
  public void PrepareManifest_strips_status() {
    var prepared = ClusterChatTools.PrepareManifest(ManifestYaml());
    Assert.NotNull(prepared);
    Assert.Null(prepared["status"]);
    Assert.Equal("ConfigMap", prepared["kind"]?.GetValue<string>());
  }

  [Fact]
  public async Task Approved_apply_sends_a_document_without_status() {
    var (tools, session) = await ConnectedAsync();
    var args = new JsonObject { ["yaml"] = ManifestYaml() };
    var text = await tools.InvokeAsync("apply_manifest", args, SampleContext(), approved: true, CancellationToken.None);
    Assert.Contains("Applied ConfigMap/app", text, StringComparison.Ordinal);
    Assert.Equal(1, session.Applies);
    Assert.Null(session.Applied?["status"]);
  }

  [Fact]
  public void Agent_flag_stays_off_when_chat_is_disabled() {
    var cfg = new Configuration { AiEnabled = false, AiAgentEnabled = true };
    cfg.EnsureDefaults();
    Assert.False(cfg.AiAgentEnabled);
  }

  [Fact]
  public void ParseArguments_accepts_object_and_json_string() {
    using var obj = JsonDocument.Parse("""{"pod":"web","container":"frontend"}""");
    var fromObject = ClusterChatTools.ParseArguments(obj.RootElement);
    Assert.Equal("web", fromObject["pod"]?.GetValue<string>());
    Assert.Equal("frontend", fromObject["container"]?.GetValue<string>());

    using var quoted = JsonDocument.Parse("\"{\\\"pod\\\":\\\"web\\\"}\"");
    var fromString = ClusterChatTools.ParseArguments(quoted.RootElement);
    Assert.Equal("web", fromString["pod"]?.GetValue<string>());
  }

  [Fact]
  public void Drain_note_prompt_asks_whether_it_is_safe_and_which_pods_remain() {
    var prompt = DrainAdvice.SystemPrompt();
    Assert.Contains("safe to continue", prompt, StringComparison.Ordinal);
    Assert.Contains("expected to remain", prompt, StringComparison.Ordinal);
    Assert.Contains("will move", prompt, StringComparison.Ordinal);
    Assert.Contains("Do not invent", prompt, StringComparison.Ordinal);

    var plan = "node-a\nWill move (1)\n  apps/api-0\nWill remain (1)\n  kube-system/cilium  DaemonSet";
    Assert.Equal(plan, DrainAdvice.UserPrompt(plan));
    Assert.Contains("…", DrainAdvice.UserPrompt(new string('x', 7000)));
  }

  [Fact]
  public async Task AssessDrain_sends_the_plan_and_returns_the_note() {
    var ollama = new ScriptedOllama("<think>check</think>\nSafe to continue. cilium remains because it is a DaemonSet.");
    var chat = new ClusterChatService(ollama, new ClusterWorkspace());
    var note = await chat.AssessDrainAsync(
      "http://127.0.0.1:11434",
      "qwen3:8b",
      "node-a\nWill remain (1)\n  kube-system/cilium",
      TestContext.Current.CancellationToken);

    Assert.True(note.IsSuccess);
    Assert.Equal("Safe to continue. cilium remains because it is a DaemonSet.", note.Value);
    Assert.Null(ollama.Request?.Tools);
    Assert.Contains("kube-system/cilium", ollama.Request?.Messages[1].Content, StringComparison.Ordinal);
    Assert.Contains("safe to continue", ollama.Request?.Messages[0].Content, StringComparison.Ordinal);
  }

  [Fact]
  public void Timeout_outside_5_to_3600_seconds_returns_to_the_default() {
    var cfg = new Configuration { OllamaTimeoutSeconds = 1 };
    cfg.EnsureDefaults();
    Assert.Equal(ClusterChatService.DefaultTimeoutSeconds, cfg.OllamaTimeoutSeconds);
    Assert.Equal(90, ClusterChatService.NormalizeTimeout(90));
    Assert.Equal("Ollama did not answer within 90 seconds.", ClusterChatService.TimedOut(90));
  }

  [Fact]
  public void HasModel_matches_tag_or_bare_name() {
    Assert.True(ClusterChatService.HasModel(["qwen3:8b"], "qwen3:8b"));
    Assert.True(ClusterChatService.HasModel(["qwen3:8b"], "qwen3"));
    Assert.False(ClusterChatService.HasModel(["qwen2.5:7b"], "qwen3:8b"));
  }

  [Fact]
  public void Configuration_defaults_to_qwen3_8b_on_local_ollama() {
    var cfg = new Configuration();
    cfg.EnsureDefaults();
    Assert.False(cfg.AiEnabled);
    Assert.False(cfg.AiAgentEnabled);
    Assert.Equal(ClusterChatService.DefaultModel, cfg.OllamaModel);
    Assert.Equal(ClusterChatService.DefaultEndpoint, cfg.OllamaEndpoint);
    Assert.Equal(ClusterChatService.DefaultTimeoutSeconds, cfg.OllamaTimeoutSeconds);
  }

  private static ClusterChatContext SampleContext() =>
    new("homelab", "apps", "Deployment", "web", null, null, "", "", "");

  private static string ManifestYaml() => """
    apiVersion: v1
    kind: ConfigMap
    metadata:
      name: app
      namespace: default
    data:
      key: value
    status:
      phase: Ready
    """;

  private static async Task<(ClusterChatTools Tools, RecordingSession Session)> ConnectedAsync() {
    var session = new RecordingSession();
    var workspace = new ClusterWorkspace();
    var connected = await workspace.ConnectAsync(session);
    Assert.True(connected.IsSuccess);

    return (new ClusterChatTools(workspace), session);
  }

  private sealed class ScriptedOllama(string answer) : IOllamaChatClient {
    public OllamaChatRequest? Request { get; private set; }

    public Task<Result<IReadOnlyList<string>>> ListModelsAsync(
      string endpoint,
      CancellationToken cancellationToken = default) =>
      Task.FromResult(Result<IReadOnlyList<string>>.Ok((IReadOnlyList<string>)["qwen3:8b"]));

    public Task<Result<OllamaChatResponse>> ChatAsync(
      string endpoint,
      OllamaChatRequest request,
      CancellationToken cancellationToken = default) {
      Request = request;

      return Task.FromResult(Result<OllamaChatResponse>.Ok(new OllamaChatResponse {
        Message = new OllamaChatMessage { Role = "assistant", Content = answer }
      }));
    }
  }

  private sealed class RecordingSession : IClusterSession {
    public int Restarts { get; private set; }

    public int Scales { get; private set; }

    public int Applies { get; private set; }

    public JsonObject? Applied { get; private set; }

    public string ContextName => "test";

    public IKubernetes Kubernetes => throw new NotSupportedException();

    public Task<Result<JsonObject>> ApplyAsync(
      JsonObject document,
      ResourceRef? resource = null,
      CancellationToken cancellationToken = default) {
      Applies++;
      Applied = document;

      return Task.FromResult(Result<JsonObject>.Ok(document));
    }

    public Task<Result> RestartAsync(
      ResourceRef resource,
      string name,
      string? @namespace,
      CancellationToken cancellationToken = default) {
      Restarts++;

      return Task.FromResult(Result.Ok());
    }

    public Task<Result> ScaleAsync(
      ResourceRef resource,
      string name,
      string? @namespace,
      int replicas,
      CancellationToken cancellationToken = default) {
      Scales++;

      return Task.FromResult(Result.Ok());
    }

    public Task<Result<IReadOnlyList<JsonObject>>> ListCustomResourceDefinitionsAsync(
      CancellationToken cancellationToken = default) =>
      Task.FromResult(Result<IReadOnlyList<JsonObject>>.Ok(Array.Empty<JsonObject>()));

    public void Dispose() {
    }

    public Task<Result<IReadOnlyList<JsonObject>>> ListAsync(
      ResourceRef resource,
      string? @namespace,
      CancellationToken cancellationToken = default,
      ResourceListOptions? options = null) =>
      Unused<Result<IReadOnlyList<JsonObject>>>();

    public IAsyncEnumerable<ClusterWatchEvent> WatchAsync(
      ResourceRef resource,
      string? @namespace,
      string? resourceVersion,
      string? labelSelector,
      CancellationToken cancellationToken = default) =>
      throw new NotSupportedException();

    public Task<Result<JsonObject>> GetAsync(
      ResourceRef resource,
      string name,
      string? @namespace,
      CancellationToken cancellationToken = default) =>
      Unused<Result<JsonObject>>();

    public Task<Result> DeleteAsync(
      ResourceRef resource,
      string name,
      string? @namespace,
      bool force = false,
      CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result> ForceDeleteNamespaceAsync(string name, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result<string>> GetLogsAsync(
      string podName,
      string @namespace,
      string? container,
      bool previous,
      int tailLines,
      CancellationToken cancellationToken = default) =>
      Unused<Result<string>>();

    public IAsyncEnumerable<string> FollowLogsAsync(
      string podName,
      string @namespace,
      string? container,
      CancellationToken cancellationToken = default) =>
      throw new NotSupportedException();

    public Task<Result<PortForwardHandle>> PortForwardAsync(
      string podName,
      string @namespace,
      int containerPort,
      int localPort,
      int requestedPort = 0,
      Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget = null,
      CancellationToken cancellationToken = default) =>
      Unused<Result<PortForwardHandle>>();

    public Task<Result<bool>> HasApiGroupAsync(string group, CancellationToken cancellationToken = default) =>
      Unused<Result<bool>>();

    public Task<Result<ClusterSummary>> GetSummaryAsync(CancellationToken cancellationToken = default) =>
      Unused<Result<ClusterSummary>>();

    public Task<Result<ClusterUsage>> GetClusterUsageAsync(CancellationToken cancellationToken = default) =>
      Unused<Result<ClusterUsage>>();

    public Task<Result<double>> GetClusterCpuAllocatableAsync(CancellationToken cancellationToken = default) =>
      Unused<Result<double>>();

    public Task<Result> PatchContainerResourcesAsync(
      WorkloadContainerLimit row,
      string cpuLimit,
      string memoryLimit,
      CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetPodMetricsAsync(
      string? @namespace,
      CancellationToken cancellationToken = default) =>
      Unused<Result<IReadOnlyDictionary<string, ResourceMetrics>>>();

    public Task<Result<IReadOnlyDictionary<string, ResourceMetrics>>> GetNodeMetricsAsync(
      CancellationToken cancellationToken = default) =>
      Unused<Result<IReadOnlyDictionary<string, ResourceMetrics>>>();

    public Task<Result<IReadOnlyList<JsonObject>>> ListHelmReleaseDocumentsAsync(
      string? @namespace,
      string? releaseName,
      CancellationToken cancellationToken = default) =>
      Unused<Result<IReadOnlyList<JsonObject>>>();

    public Task<Result<string>> ExecAsync(
      string podName,
      string @namespace,
      string? container,
      IReadOnlyList<string> command,
      CancellationToken cancellationToken = default) =>
      Unused<Result<string>>();

    public Task<Result<ExecBytesResult>> ExecBytesAsync(
      string podName,
      string @namespace,
      string? container,
      IReadOnlyList<string> command,
      byte[]? stdin = null,
      CancellationToken cancellationToken = default) =>
      Unused<Result<ExecBytesResult>>();

    public Task<Result<PodTerminalConnection>> OpenTerminalAsync(
      string podName,
      string @namespace,
      string? container,
      string? shell,
      CancellationToken cancellationToken = default) =>
      Unused<Result<PodTerminalConnection>>();

    public Task<Result> CordonAsync(string nodeName, bool unschedulable, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result> DrainAsync(string nodeName, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result> TriggerCronJobAsync(string name, string @namespace, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result> ResizePersistentVolumeClaimAsync(
      string name,
      string @namespace,
      string storage,
      CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result> PauseRolloutAsync(string name, string @namespace, bool paused, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result<IReadOnlyList<string>>> RolloutHistoryAsync(
      string name,
      string @namespace,
      CancellationToken cancellationToken = default) =>
      Unused<Result<IReadOnlyList<string>>>();

    public Task<Result> UndoRolloutAsync(string name, string @namespace, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result> SetCertificateApprovalAsync(string name, bool approved, CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result<string>> CreateServiceAccountTokenAsync(
      string name,
      string @namespace,
      CancellationToken cancellationToken = default) =>
      Unused<Result<string>>();

    public Task<Result<string>> AttachAsync(
      string podName,
      string @namespace,
      string? container,
      CancellationToken cancellationToken = default) =>
      Unused<Result<string>>();

    public Task<Result> AddEphemeralContainerAsync(
      string podName,
      string @namespace,
      string image,
      string? targetContainer,
      CancellationToken cancellationToken = default) =>
      Unused<Result>();

    public Task<Result<StorageReclaimPreview>> PreviewStorageClassReclaimAsync(
      string name,
      CancellationToken cancellationToken = default) =>
      Unused<Result<StorageReclaimPreview>>();

    public Task<Result<StorageReclaimPreview>> PreviewPersistentVolumeReclaimAsync(
      IReadOnlyList<string> names,
      CancellationToken cancellationToken = default) =>
      Unused<Result<StorageReclaimPreview>>();

    public Task<Result<StorageReclaimOutcome>> ApplyStorageClassReclaimAsync(
      string name,
      string policy,
      bool updateVolumes,
      bool updateClass,
      CancellationToken cancellationToken = default) =>
      Unused<Result<StorageReclaimOutcome>>();

    public Task<Result<StorageReclaimOutcome>> ApplyPersistentVolumeReclaimAsync(
      IReadOnlyList<string> names,
      string policy,
      CancellationToken cancellationToken = default) =>
      Unused<Result<StorageReclaimOutcome>>();

    private static Task<T> Unused<T>() =>
      Task.FromException<T>(new NotSupportedException());
  }
}
