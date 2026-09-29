using System.Text;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Tests;

public class PodShellTests {
  [Fact]
  public void BuildCommand_starts_an_interactive_shell_with_term() {
    var command = PodShell.BuildCommand(null);

    Assert.Equal(
      ["/bin/sh", "-c", "export TERM=xterm-256color; exec \"$0\" \"$@\"", "/bin/sh"],
      command);
  }

  [Fact]
  public void BuildCommand_keeps_shell_arguments() {
    var command = PodShell.BuildCommand("  /bin/bash   -l ");

    Assert.Equal("/bin/bash", command[^2]);
    Assert.Equal("-l", command[^1]);
  }

  [Fact]
  public void ResizePayload_uses_kubernetes_terminal_size() {
    var json = Encoding.UTF8.GetString(PodShell.ResizePayload(120, 40));

    Assert.Equal("{\"Width\":120,\"Height\":40}\n", json);
  }

  [Fact]
  public void ReadStatus_reads_a_clean_exit() {
    var status = PodShell.ReadStatus("""{"status":"Success"}""");

    Assert.Equal(0, status.Code);
  }

  [Fact]
  public void ReadStatus_reads_a_nonzero_exit() {
    const string body = """
      {
        "status": "Failure",
        "message": "command terminated with non-zero exit code",
        "reason": "NonZeroExitCode",
        "details": { "causes": [{ "reason": "ExitCode", "message": "2" }] }
      }
      """;

    var status = PodShell.ReadStatus(body);

    Assert.Equal(2, status.Code);
    Assert.Equal("command terminated with non-zero exit code", status.Message);
  }

  [Fact]
  public void ReadStatus_ignores_garbage() {
    var status = PodShell.ReadStatus("not json");

    Assert.Equal(-1, status.Code);
    Assert.Null(status.Message);
  }
}
