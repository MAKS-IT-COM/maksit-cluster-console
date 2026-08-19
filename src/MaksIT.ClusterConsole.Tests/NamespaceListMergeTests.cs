using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class NamespaceListMergeTests {
  [Fact]
  public void Adds_pod_namespaces_that_are_missing_from_the_namespace_api() {
    var listed = new[] {
      NamespaceListMerge.Document("default", "Active"),
      NamespaceListMerge.Document("maksit-cicd", "Active")
    };
    var pods = new (string Namespace, DateTimeOffset? Created)[] {
      ("default", DateTimeOffset.UtcNow),
      ("maksit-cicd-build-060d725eb6fb43309dd9873485da858a", DateTimeOffset.Parse("2026-07-27T21:11:40Z")),
      ("maksit-cicd-build-060d725eb6fb43309dd9873485da858a", DateTimeOffset.Parse("2026-07-27T21:11:41Z"))
    };

    var merged = NamespaceListMerge.WithOrphansFromPods(listed, pods);
    var names = merged.Select(JsonPath.Name).ToList();
    Assert.Contains("default", names);
    Assert.Contains("maksit-cicd", names);
    Assert.Contains("maksit-cicd-build-060d725eb6fb43309dd9873485da858a", names);
    Assert.Equal(3, names.Count);

    var orphan = Assert.Single(merged, item => JsonPath.Name(item) == "maksit-cicd-build-060d725eb6fb43309dd9873485da858a");
    var row = ResourceRow.From(orphan, ResourceCatalog.Find("namespaces")!);
    Assert.Equal("Orphaned", row.Status);
    Assert.Equal("maksit-cicd-build-060d725eb6fb43309dd9873485da858a", row.Cells["Name"]);
  }
}
