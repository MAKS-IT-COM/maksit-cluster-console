using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Shared.Chat;


namespace MaksIT.ClusterConsole.Tests;

public class OperatorSettingsTests {
  [Fact]
  public void Ai_settings_fill_blanks_and_disable_the_agent_when_chat_is_off() {
    var settings = new AiSettings {
      Enabled = false,
      AgentEnabled = true,
      Endpoint = " ",
      Model = "",
      TimeoutSeconds = 1
    };
    settings.Ensure();

    Assert.False(settings.AgentEnabled);
    Assert.Equal(ClusterChatService.DefaultEndpoint, settings.Endpoint);
    Assert.Equal(ClusterChatService.DefaultModel, settings.Model);
    Assert.Equal(ClusterChatService.DefaultTimeoutSeconds, settings.TimeoutSeconds);
  }

  [Fact]
  public void Port_forward_directory_replaces_the_same_local_port() {
    var directory = new PortForwardDirectory { Items = null! };
    directory.Upsert(Forward("homelab", 8080, "web"));
    directory.Upsert(Forward("homelab", 8080, "web-2"));
    directory.Upsert(Forward("dev", 8080, "api"));

    Assert.Equal("web-2", Assert.Single(directory.For("homelab")).PodName);
    Assert.Equal("api", Assert.Single(directory.For("dev")).PodName);

    directory.Remove("dev", 8080);
    Assert.Empty(directory.For("dev"));
    Assert.Equal("mailto:security@maks-it.com", AppInfo.Security.Uri);
    Assert.Contains(DateTime.UtcNow.Year.ToString(), AppInfo.Copyright, StringComparison.Ordinal);
    Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
  }

  [Fact]
  public void Persisted_port_forward_row_uses_the_saved_remote_port() {
    var row = PortForwardRow.FromPersisted(Forward("homelab", 18080, "web"), "Stopped");

    Assert.Equal("Stopped", row.Cells["Status"]);
    Assert.Equal("18080", row.Cells["Local"]);
    Assert.Equal("web", row.Cells["Pod"]);
    Assert.True(PortForwardRow.TryLocalPort(row, out var port));
    Assert.Equal(18080, port);
  }

  private static PersistedPortForward Forward(string context, int port, string pod) =>
    new() {
      Context = context,
      Name = pod,
      Namespace = "apps",
      PodName = pod,
      LocalPort = port,
      RemotePort = 80
    };
}
