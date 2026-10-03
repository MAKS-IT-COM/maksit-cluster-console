using MaksIT.ClusterConsole.Shared.Chat;


namespace MaksIT.ClusterConsole.Shared;

public sealed class AiSettings {
  public bool Enabled { get; set; }

  public bool AgentEnabled { get; set; }

  public string Endpoint { get; set; } = ClusterChatService.DefaultEndpoint;

  public string Model { get; set; } = ClusterChatService.DefaultModel;

  public int TimeoutSeconds { get; set; } = ClusterChatService.DefaultTimeoutSeconds;

  public void Ensure() {
    if (string.IsNullOrWhiteSpace(Endpoint))
      Endpoint = ClusterChatService.DefaultEndpoint;

    if (string.IsNullOrWhiteSpace(Model))
      Model = ClusterChatService.DefaultModel;

    TimeoutSeconds = ClusterChatService.NormalizeTimeout(TimeoutSeconds);

    if (!Enabled)
      AgentEnabled = false;
  }
}
