using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Tests;

public class LonghornVolumeTests {
  [Fact]
  public void Columns_match_the_longhorn_volume_list() {
    var columns = ResourceCatalog.Find(ResourceCatalog.LonghornVolumesId)!.Columns.Select(column => column.Header).ToArray();

    Assert.Equal(
      ["State", "Name", "Size", "Actual Size", "Created", "Data Engine", "PV/PVC", "Namespace", "Volume", "Node", "Schedule", "Last Backup At"],
      columns);
    Assert.True(ResourceCatalog.Find(ResourceCatalog.LonghornVolumesId)!.Actions.CanVolumeAttach);
    Assert.True(ResourceCatalog.Find(ResourceCatalog.LonghornVolumesId)!.Actions.CanVolumeDetach);

    var volume = Volume("""
      {
        "metadata": { "name": "pvc-new", "namespace": "longhorn-system", "creationTimestamp": "2026-10-01T12:00:00Z" },
        "spec": {
          "size": "1073741824",
          "dataEngine": "v2",
          "recurringJobs": [ { "name": "backup" } ]
        },
        "status": {
          "state": "detached",
          "robustness": "healthy",
          "actualSize": "1048576",
          "lastBackupAt": "2026-09-01T00:00:00Z",
          "kubernetesStatus": {
            "pvName": "pvc-new",
            "pvStatus": "Bound",
            "namespace": "apps",
            "pvcName": "data",
            "workloadsStatus": [ { "podName": "db-0" } ]
          }
        }
      }
      """);

    Assert.Equal("detached", JsonPath.Read(volume, "longhorn.state"));
    Assert.Equal("1.0GiB", JsonPath.Read(volume, "longhorn.size"));
    Assert.Equal("1.0MiB", JsonPath.Read(volume, "longhorn.actualSize"));
    Assert.Equal("2026-10-01 12:00Z", JsonPath.Read(volume, "longhorn.created"));
    Assert.Equal("v2", JsonPath.Read(volume, "longhorn.dataEngine"));
    Assert.Equal("Bound", JsonPath.Read(volume, "longhorn.pv"));
    Assert.Equal("apps", JsonPath.Read(volume, "longhorn.workloadNamespace"));
    Assert.Equal("data", JsonPath.Read(volume, "longhorn.volumeName"));
    Assert.Equal("", JsonPath.Read(volume, "longhorn.node"));
    Assert.Contains("db-0", LonghornVolume.Tips(volume)["Node"], StringComparison.Ordinal);
    Assert.Equal("backup", JsonPath.Read(volume, "longhorn.schedule"));
    Assert.Equal("2026-09-01 00:00Z", JsonPath.Read(volume, "longhorn.lastBackupAt"));
    Assert.Equal("attached · degraded", JsonPath.Read(Volume("""
      { "status": { "state": "attached", "robustness": "degraded", "currentNodeID": "node-a" } }
      """), "longhorn.state"));
    Assert.Equal("node-a", JsonPath.Read(Volume("""
      { "status": { "state": "attached", "currentNodeID": "node-a" } }
      """), "longhorn.node"));
  }

  [Fact]
  public void Degraded_attached_volume_blocks_the_new_detached_one() {
    var now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
    var old = Volume("""
      {
        "metadata": { "name": "pvc-old", "namespace": "longhorn-system", "uid": "old", "creationTimestamp": "2026-01-01T00:00:00Z" },
        "status": {
          "state": "attached",
          "robustness": "degraded",
          "kubernetesStatus": { "namespace": "apps", "pvcName": "data" }
        }
      }
      """);
    var fresh = Volume("""
      {
        "metadata": { "name": "pvc-new", "namespace": "longhorn-system", "uid": "new", "creationTimestamp": "2026-10-20T00:00:00Z" },
        "status": {
          "state": "detached",
          "robustness": "healthy",
          "kubernetesStatus": { "namespace": "apps", "pvcName": "data" }
        }
      }
      """);

    var issues = LonghornVolume.Issues([old, fresh], now);

    Assert.Single(issues);
    Assert.Contains("Detach pvc-old", issues[0].Message, StringComparison.Ordinal);
    Assert.Contains("attach pvc-new", issues[0].Message, StringComparison.Ordinal);
    Assert.Contains("Do not delete pvc-new", issues[0].Message, StringComparison.Ordinal);
    Assert.Equal("Warning", issues[0].Severity);
  }

  [Fact]
  public void Backup_restore_pairs_a_degraded_volume_with_the_new_detached_one() {
    var now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
    var old = Volume("""
      {
        "metadata": { "name": "pvc-old", "namespace": "longhorn-system", "uid": "old", "creationTimestamp": "2026-01-01T00:00:00Z" },
        "status": { "state": "attached", "robustness": "degraded" }
      }
      """);
    var fresh = Volume("""
      {
        "metadata": { "name": "pvc-new", "namespace": "longhorn-system", "uid": "new", "creationTimestamp": "2026-10-20T00:00:00Z" },
        "spec": { "fromBackup": "s3://bucket@us-east-1/?backup=backup-1&volume=pvc-old" },
        "status": { "state": "detached", "robustness": "healthy" }
      }
      """);
    var replica = Volume("""
      {
        "metadata": { "name": "replica-old", "namespace": "longhorn-system" },
        "spec": { "volumeName": "pvc-old", "failedAt": "2026-10-19T00:00:00Z" },
        "status": { "currentState": "stopped" }
      }
      """);

    var issues = LonghornVolume.Issues([old, fresh], now, [replica]);

    Assert.Single(issues);
    Assert.Contains("Detach pvc-old", issues[0].Message, StringComparison.Ordinal);
    Assert.Contains("attach pvc-new", issues[0].Message, StringComparison.Ordinal);
    Assert.Contains("Do not delete pvc-new", issues[0].Message, StringComparison.Ordinal);
    Assert.Contains("replica-old is failed", issues[0].Message, StringComparison.Ordinal);
    Assert.Contains("force", issues[0].Message, StringComparison.Ordinal);
  }

  [Fact]
  public void An_unrelated_detached_volume_is_not_treated_as_the_new_one() {
    var now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
    var old = Volume("""
      {
        "metadata": { "name": "pvc-old", "namespace": "longhorn-system", "uid": "old", "creationTimestamp": "2026-01-01T00:00:00Z" },
        "status": { "state": "attached", "robustness": "degraded" }
      }
      """);
    var other = Volume("""
      {
        "metadata": { "name": "pvc-other", "namespace": "longhorn-system", "uid": "other", "creationTimestamp": "2026-10-20T00:00:00Z" },
        "status": { "state": "detached", "robustness": "healthy" }
      }
      """);

    var issues = LonghornVolume.Issues([old, other], now);

    Assert.Single(issues);
    Assert.DoesNotContain(issues, issue => issue.Message.Contains("blocking", StringComparison.Ordinal));
  }

  [Fact]
  public void Actual_size_schedule_and_pv_tip_follow_the_longhorn_ui() {
    var volume = Volume("""
      {
        "metadata": {
          "name": "pvc-new",
          "labels": { "recurring-job.longhorn.io/daily": "enabled", "recurring-job.longhorn.io/off": "disabled" }
        },
        "controllers": [ { "actualSize": "2097152" } ],
        "status": {
          "actualSize": "1048576",
          "kubernetesStatus": {
            "pvName": "pvc-new",
            "pvStatus": "Bound",
            "namespace": "apps",
            "pvcName": "data"
          }
        }
      }
      """);

    Assert.Equal("2.0MiB", JsonPath.Read(volume, "longhorn.actualSize"));
    Assert.Equal("daily", JsonPath.Read(volume, "longhorn.schedule"));
    var tips = LonghornVolume.Tips(volume);
    Assert.Contains("PV: pvc-new", tips["PV/PVC"], StringComparison.Ordinal);
    Assert.Contains("PVC: data", tips["PV/PVC"], StringComparison.Ordinal);
    Assert.Contains("Namespace: apps", tips["PV/PVC"], StringComparison.Ordinal);
  }

  [Fact]
  public void Ready_nodes_are_the_ones_longhorn_marks_ready() {
    var ready = Volume("""
      { "metadata": { "name": "node-a" }, "status": { "conditions": [ { "type": "Ready", "status": "True" } ] } }
      """);
    var down = Volume("""
      { "metadata": { "name": "node-b" }, "status": { "conditions": [ { "type": "Ready", "status": "False" } ] } }
      """);

    Assert.Equal(["node-a"], LonghornVolume.ReadyNodes([down, ready]));
  }

  [Fact]
  public void Attachment_ticket_keeps_other_tickets_and_names_the_node() {
    var merge = LonghornAttachment.TicketMerge("node-a", false);
    Assert.Contains("\"nodeID\":\"node-a\"", merge, StringComparison.Ordinal);
    Assert.Contains("longhorn-ui", merge, StringComparison.Ordinal);
    Assert.Contains("\"disableFrontend\":\"false\"", merge, StringComparison.Ordinal);

    var clear = LonghornAttachment.ClearTickets(["longhorn-ui", "csi-attacher"]);
    Assert.Contains("\"longhorn-ui\":null", clear, StringComparison.Ordinal);
    Assert.Contains("\"csi-attacher\":null", clear, StringComparison.Ordinal);
    Assert.Equal("{\"spec\":{\"nodeID\":\"\"}}", LonghornAttachment.NodePatch(""));
  }

  private static JsonObject Volume(string json) =>
    JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("expected object");
}
