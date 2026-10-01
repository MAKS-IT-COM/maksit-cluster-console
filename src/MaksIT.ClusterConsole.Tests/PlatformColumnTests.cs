using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class PlatformColumnTests {
  [Fact]
  public void Longhorn_and_cnpg_columns_read_status_fields() {
    Assert.Equal(ResourceCatalog.Longhorn, ResourceCatalog.Find(ResourceCatalog.LonghornVolumesId)!.Section);
    Assert.Equal(ResourceCatalog.CloudNativePG, ResourceCatalog.Find(ResourceCatalog.CnpgClustersId)!.Section);
    Assert.False(ResourceCatalog.Find(ResourceCatalog.CnpgClustersId)!.Actions.CanDelete);
    Assert.False(ResourceCatalog.HelmReleasesDescriptor.Actions.CanApply);
    Assert.Contains("History", ResourceCatalog.HelmReleasesDescriptor.DetailTabs);

    var volume = JsonNode.Parse("""
      { "spec": { "size": "1073741824", "numberOfReplicas": 3 }, "status": { "state": "attached", "robustness": "healthy" } }
      """) as JsonObject;
    Assert.Equal("1.0GiB", JsonPath.Read(volume, "longhorn.size"));

    var node = JsonNode.Parse("""
      {
        "spec": { "allowScheduling": false },
        "status": {
          "diskStatus": {
            "disk1": {
              "storageAvailable": 1073741824,
              "conditions": [ { "type": "Ready", "status": "False" } ]
            }
          }
        }
      }
      """) as JsonObject;
    Assert.Equal("No", JsonPath.Read(node, "longhorn.scheduling"));
    Assert.Contains("not ready", JsonPath.Read(node, "longhorn.disks"));

    var cluster = JsonNode.Parse("""
      { "status": { "phase": "Cluster in healthy state", "readyInstances": 2, "instances": 3, "currentPrimary": "db-1" } }
      """) as JsonObject;
    Assert.Equal("2/3", JsonPath.Read(cluster, "cnpg.instances"));
    Assert.Equal("db-1", JsonPath.Read(cluster, "status.currentPrimary"));
  }
}
