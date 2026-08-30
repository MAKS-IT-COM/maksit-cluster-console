using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Tests;

public class ResourceActionBatchTests {
  [Fact]
  public void Targets_falls_back_to_the_current_row() {
    var current = Row("web");
    Assert.Equal([current], ResourceActionBatch.Targets([], current));
  }

  [Fact]
  public void Targets_uses_every_selected_row() {
    var first = Row("web-a");
    var second = Row("web-b");
    Assert.Equal([first, second], ResourceActionBatch.Targets([first, second], first));
  }

  [Fact]
  public async Task RunAsync_runs_the_action_for_each_row() {
    var names = new List<string>();
    var outcome = await ResourceActionBatch.RunAsync(
      [Row("web-a", "apps"), Row("web-b", "apps")],
      row => {
        names.Add(row.Name);
        return Task.FromResult(Result.Ok());
      });

    Assert.Equal(["web-a", "web-b"], names);
    Assert.Equal(2, outcome.Succeeded);
    Assert.Equal(2, outcome.Total);
    Assert.Empty(outcome.Failures);
    Assert.Equal("Restarted 2.", outcome.Format("Restarted.", "Restarted 2."));
  }

  [Fact]
  public async Task RunAsync_keeps_going_after_a_failure() {
    var outcome = await ResourceActionBatch.RunAsync(
      [Row("web-a", "apps"), Row("web-b", "apps")],
      row => Task.FromResult(
        row.Name == "web-a"
          ? Result.InternalServerError("forbidden")
          : Result.Ok()));

    Assert.Equal(1, outcome.Succeeded);
    Assert.Equal(["apps/web-a: forbidden"], outcome.Failures);
    Assert.Equal(
      "1 of 2 succeeded. apps/web-a: forbidden",
      outcome.Format("Restarted.", "Restarted 2."));
  }

  [Fact]
  public void Format_keeps_the_singular_status_for_one_row() {
    var outcome = new ResourceActionBatchOutcome(1, 1, []);
    Assert.Equal("Restarted.", outcome.Format("Restarted.", "Restarted 2."));
  }

  private static ResourceRow Row(string name, string? ns = null) =>
    new() {
      Uid = name,
      Name = name,
      Namespace = ns,
      Document = new JsonObject(),
      Cells = new Dictionary<string, string> { ["Name"] = name }
    };
}
