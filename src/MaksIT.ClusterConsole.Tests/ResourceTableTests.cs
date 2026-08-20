using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ResourceTableTests {
  [Theory]
  [InlineData("Running", ResourceStatusTone.Healthy)]
  [InlineData("Ready", ResourceStatusTone.Healthy)]
  [InlineData("deployed", ResourceStatusTone.Healthy)]
  [InlineData("Pending", ResourceStatusTone.Warning)]
  [InlineData("Unknown", ResourceStatusTone.Warning)]
  [InlineData("Failed", ResourceStatusTone.Error)]
  [InlineData("CrashLoopBackOff", ResourceStatusTone.Error)]
  [InlineData("Orphaned", ResourceStatusTone.Warning)]
  [InlineData("Progressing", ResourceStatusTone.Warning)]
  [InlineData("Stopped", ResourceStatusTone.Warning)]
  [InlineData("Succeeded", ResourceStatusTone.Info)]
  [InlineData("Bound", ResourceStatusTone.Info)]
  [InlineData("Resolved", ResourceStatusTone.Info)]
  public void Status_tone_matches_kubernetes_phase(string status, ResourceStatusTone tone) =>
    Assert.Equal(tone, ResourceStatusPaint.Tone(status));

  [Fact]
  public void Age_sort_orders_by_duration_not_text() {
    Assert.True(ResourceColumnSort.AgeToSeconds("2d") > ResourceColumnSort.AgeToSeconds("3h5m"));
    Assert.True(ResourceColumnSort.Compare("Age", "30s", "2m") < 0);
    Assert.True(ResourceColumnSort.Compare("Age", "2d", "5h") > 0);
  }

  [Fact]
  public void Ip_columns_sort_as_addresses_not_text() {
    Assert.True(ResourceColumnSort.Compare("Cluster IP", "10.1.1.2", "10.1.1.10") < 0);
    Assert.True(ResourceColumnSort.Compare("Cluster IP", "10.1.1.10", "10.1.1.2") > 0);
    Assert.Equal(0, ResourceColumnSort.Compare("External IP", "192.168.0.1", "192.168.0.1"));
    Assert.True(ResourceColumnSort.Compare("Cluster IP", "None", "10.0.0.1") < 0);
    Assert.True(ResourceColumnSort.Compare("External IP", "10.0.0.2,10.0.0.10", "10.0.0.2,10.0.0.3") > 0);
    Assert.True(ResourceColumnSort.Compare("Cluster IP", "127.0.0.1", "::1") < 0);
    Assert.True(ResourceColumnSort.Compare("Cluster IP", "2001:db8::1", "2001:db8::10") < 0);
  }

  [Fact]
  public void Ready_and_restarts_sort_numerically() {
    Assert.True(ResourceColumnSort.Compare("Ready", "1/2", "2/2") < 0);
    Assert.True(ResourceColumnSort.Compare("Restarts", "12", "3") > 0);
    Assert.True(ResourceColumnSort.Compare("CPU", "250m", "1") < 0);
  }

  [Fact]
  public void ResourceRowComparer_sorts_by_named_cell() {
    var older = Row("web-a", "2d");
    var newer = Row("web-b", "30s");
    var comparer = new ResourceRowComparer("Age");
    Assert.True(comparer.Compare(newer, older) < 0);
  }

  [Fact]
  public void Column_filter_matches_contains_and_excluded_values() {
    var running = Row("web-a", "2d", "Running");
    var pending = Row("web-b", "30s", "Pending");
    var filter = new ResourceColumnFilter { Header = "Status", Text = "run" };
    Assert.True(filter.Matches(running));
    Assert.False(filter.Matches(pending));

    filter.Text = "";
    filter.Excluded.Add("Pending");
    Assert.True(filter.Matches(running));
    Assert.False(filter.Matches(pending));
    Assert.Equal(["Pending", "Running"], ResourceColumnFilter.DistinctValues([running, pending], "Status"));
  }

  [Fact]
  public void Namespace_scope_is_the_single_included_value() {
    var filter = new ResourceColumnFilter { Header = "Namespace" };
    filter.Excluded.Add("default");
    Assert.Equal("kube-system", ResourceColumnFilter.Scope(filter, ["default", "kube-system"]));
  }

  [Fact]
  public void CopyFrom_updates_cells_on_the_same_instance() {
    var current = Row("web-a", "2d", "Running");
    var incoming = Row("web-a", "3d", "CrashLoopBackOff");
    var cellsChanged = 0;
    var statusChanged = 0;
    current.PropertyChanged += (_, e) => {
      if (e.PropertyName == nameof(ResourceRow.Cells))
        cellsChanged++;

      if (e.PropertyName == nameof(ResourceRow.Status))
        statusChanged++;
    };

    current.CopyFrom(incoming);

    Assert.Equal("3d", current.Cell("Age"));
    Assert.Equal("CrashLoopBackOff", current.Status);
    Assert.Equal(1, cellsChanged);
    Assert.Equal(1, statusChanged);
  }

  [Fact]
  public void CopyFrom_rejects_a_different_uid() {
    var current = Row("web-a", "2d");
    Assert.Throws<ArgumentException>(() => current.CopyFrom(Row("web-b", "30s")));
  }

  [Fact]
  public void FormatOverview_lists_cells_workloads_and_containers() {
    var document = JsonNode.Parse("""
      {
        "spec": {
          "workloads": [
            { "kind": null, "name": "hubble-ui" },
            { "kind": "Deployment", "name": "hubble-relay" }
          ]
        }
      }
      """) as JsonObject;
    var row = Row("cilium", "2d", "Running");
    row.Document = document!;
    var containers = new[] {
      new PodContainer("hubble-ui", "quay.io/cilium/hubble-ui:v0.13.1", "Container", true, 0, "Running")
    };

    var text = row.FormatOverview(containers);

    Assert.Contains("Status: Running", text);
    Assert.Contains("Workloads:", text);
    Assert.Contains("  Workload/hubble-ui", text);
    Assert.Contains("  Deployment/hubble-relay", text);
    Assert.Contains("Containers:", text);
    Assert.Contains("  hubble-ui  [Container]  Running  quay.io/cilium/hubble-ui:v0.13.1", text);
  }

  [Fact]
  public void FormatOverview_omits_empty_container_and_workload_sections() {
    var text = Row("web-a", "30s", "Pending").FormatOverview();
    Assert.Contains("Name: web-a", text);
    Assert.DoesNotContain("Workloads:", text);
    Assert.DoesNotContain("Containers:", text);
  }

  [Fact]
  public void Namespace_scope_is_all_when_contains_text_or_multiple_values() {
    var filter = new ResourceColumnFilter { Header = "Namespace", Text = "kube" };
    Assert.Equal(Configuration.AllNamespaces, ResourceColumnFilter.Scope(filter, ["kube-system"]));

    filter.Text = "";
    Assert.Equal(Configuration.AllNamespaces, ResourceColumnFilter.Scope(filter, ["default", "kube-system"]));
  }

  private static ResourceRow Row(string name, string age, string status = "") =>
    new() {
      Uid = name,
      Name = name,
      Document = new System.Text.Json.Nodes.JsonObject(),
      Cells = new Dictionary<string, string> {
        ["Name"] = name,
        ["Age"] = age,
        ["Status"] = status
      }
    };
}
