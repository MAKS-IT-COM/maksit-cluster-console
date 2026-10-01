using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ShoulderHintsTests {
  [Fact]
  public void Charts_and_releases_explain_how_they_differ() {
    var charts = ShoulderHints.Text(ResourceCatalog.HelmChartsId, ResourceCatalog.HelmChartsDescriptor);
    var releases = ShoulderHints.Text(ResourceCatalog.HelmReleasesId, ResourceCatalog.HelmReleasesDescriptor);

    Assert.Contains("chart version", charts);
    Assert.Contains("Revision", releases);
    Assert.Contains("Helm → Releases", charts);
    Assert.Contains("Helm → Charts", releases);
  }

  [Fact]
  public void FlowSchema_explains_what_the_object_is() {
    var schema = ResourceCatalog.Find("flowschemas")!;
    var text = ShoulderHints.Text(schema.Id, schema);

    Assert.Contains("API request", text);
    Assert.Contains("priority", text);
    Assert.Contains("Name is the object name.", text);
    Assert.Contains("Age is how long ago it was created.", text);
    Assert.Contains("This pane shows Overview, YAML, Events.", text);
    Assert.DoesNotContain("Columns:", text);
    Assert.DoesNotContain("has no explanation yet", text);
  }

  [Fact]
  public void Every_builtin_says_what_the_object_is() {
    foreach (var descriptor in ResourceCatalog.BuiltIns) {
      var text = ShoulderHints.Text(descriptor.Id, descriptor);
      Assert.True(text.Length > 80, descriptor.Id);
      Assert.DoesNotContain("has no explanation yet", text);
      Assert.DoesNotContain("lists " + descriptor.Kind + " objects", text);
      Assert.DoesNotContain("Columns:", text);
      Assert.DoesNotContain("defined by this custom resource", text);
      foreach (var column in descriptor.Columns) {
        var explained = text.Contains(column.Header + " is", StringComparison.Ordinal)
          || text.Contains(column.Header + " are", StringComparison.Ordinal);
        Assert.True(explained, descriptor.Id + " " + column.Header);
      }
    }
  }

  [Fact]
  public void Dashboards_have_no_shoulder_hint() {
    Assert.Equal("", ShoulderHints.Text(ResourceCatalog.OverviewId, null));
    Assert.Equal("", ShoulderHints.Text(ResourceCatalog.WorkloadsOverviewId, null));
  }
}
