using System.Text;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Tests;

public class PodLogsTests {
  [Fact]
  public async Task ReadLogLines_splits_on_newlines() {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a\nb\n"));
    var lines = new List<string>();
    await foreach (var line in ClusterSession.ReadLogLinesAsync(stream, TestContext.Current.CancellationToken))
      lines.Add(line);

    Assert.Equal(["a", "b"], lines);
  }

  [Fact]
  public async Task ReadLogLines_keeps_a_final_line_without_newline() {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("only"));
    var lines = new List<string>();
    await foreach (var line in ClusterSession.ReadLogLinesAsync(stream, TestContext.Current.CancellationToken))
      lines.Add(line);

    Assert.Equal(["only"], lines);
  }

  [Fact]
  public async Task ReadLogLines_stops_when_cancelled() {
    using var stream = new MemoryStream();
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var lines = new List<string>();
    await foreach (var line in ClusterSession.ReadLogLinesAsync(stream, cts.Token))
      lines.Add(line);

    Assert.Empty(lines);
  }
}
