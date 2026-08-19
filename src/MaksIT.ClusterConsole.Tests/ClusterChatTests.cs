using System.Text.Json;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ClusterChatTests {
  [Fact]
  public void StripThink_removes_qwen_reasoning_blocks() {
    var text = ClusterChatContext.StripThink("<think>plan</think>\nThe pod is CrashLooping.");
    Assert.Equal("The pod is CrashLooping.", text);
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
  public void HasModel_matches_tag_or_bare_name() {
    Assert.True(ClusterChatService.HasModel(["qwen3:8b"], "qwen3:8b"));
    Assert.True(ClusterChatService.HasModel(["qwen3:8b"], "qwen3"));
    Assert.False(ClusterChatService.HasModel(["qwen2.5:7b"], "qwen3:8b"));
  }

  [Fact]
  public void Configuration_defaults_to_qwen3_8b_on_local_ollama() {
    var cfg = new Configuration();
    cfg.EnsureDefaults();
    Assert.Equal(ClusterChatService.DefaultModel, cfg.OllamaModel);
    Assert.Equal(ClusterChatService.DefaultEndpoint, cfg.OllamaEndpoint);
  }
}
