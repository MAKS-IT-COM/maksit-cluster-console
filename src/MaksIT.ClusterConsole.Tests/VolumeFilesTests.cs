using System.Text;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class VolumeFilesTests {
  [Fact]
  public void VolumeClaim_reads_pvc_and_bound_pv() {
    var pvc = JsonNode.Parse("""
      {
        "kind": "PersistentVolumeClaim",
        "metadata": { "name": "pg-data", "namespace": "postgresql" }
      }
      """) as JsonObject;
    var pv = JsonNode.Parse("""
      {
        "kind": "PersistentVolume",
        "metadata": { "name": "pvc-1" },
        "spec": { "claimRef": { "namespace": "postgresql", "name": "pg-data" } }
      }
      """) as JsonObject;
    var unbound = JsonNode.Parse("""
      {
        "kind": "PersistentVolume",
        "metadata": { "name": "unused" },
        "spec": {}
      }
      """) as JsonObject;

    Assert.True(VolumeClaim.TryGet(pvc, out var pvcNs, out var pvcName));
    Assert.Equal("postgresql", pvcNs);
    Assert.Equal("pg-data", pvcName);

    Assert.True(VolumeClaim.TryGet(pv, out var pvNs, out var pvName));
    Assert.Equal("postgresql", pvNs);
    Assert.Equal("pg-data", pvName);

    Assert.False(VolumeClaim.TryGet(unbound, out _, out _));

    var pvcListItem = JsonNode.Parse("""
      {
        "metadata": { "name": "pg-data", "namespace": "postgresql" },
        "spec": { "volumeName": "pvc-1", "accessModes": ["ReadWriteOnce"] }
      }
      """) as JsonObject;
    Assert.True(VolumeClaim.TryGet(pvcListItem, out var listNs, out var listName));
    Assert.Equal("postgresql", listNs);
    Assert.Equal("pg-data", listName);
  }

  [Fact]
  public void VolumeMounts_find_running_pod_and_subPath() {
    var pod = JsonNode.Parse("""
      {
        "metadata": { "name": "pg-0", "namespace": "postgresql" },
        "spec": {
          "volumes": [
            { "name": "data", "persistentVolumeClaim": { "claimName": "pg-data" } },
            { "name": "tmp", "emptyDir": {} }
          ],
          "containers": [
            {
              "name": "postgres",
              "volumeMounts": [
                { "name": "data", "mountPath": "/var/lib/postgresql/data", "subPath": "pgdata" },
                { "name": "tmp", "mountPath": "/tmp" }
              ]
            }
          ]
        },
        "status": { "phase": "Running" }
      }
      """) as JsonObject;

    Assert.NotNull(pod);
    var mounts = VolumeMounts.FromPod(pod, "pg-data");
    var mount = Assert.Single(mounts);
    Assert.Equal("pg-0", mount.PodName);
    Assert.Equal("postgres", mount.Container);
    Assert.Equal("/var/lib/postgresql/data/pgdata", mount.Root);
    Assert.True(mount.IsRunning);
    Assert.Empty(VolumeMounts.FromPod(pod, "other"));
  }

  [Fact]
  public void VolumePath_stays_under_mount_and_rejects_dotdot() {
    var ok = VolumePath.Resolve("/data", "pgdata/postgresql.conf");
    Assert.True(ok.IsSuccess);
    Assert.Equal("/data/pgdata/postgresql.conf", ok.Value);

    var root = VolumePath.Resolve("/data", "");
    Assert.True(root.IsSuccess);
    Assert.Equal("/data", root.Value);

    var escape = VolumePath.Resolve("/data", "../etc/passwd");
    Assert.False(escape.IsSuccess);

    var absolute = VolumePath.Resolve("/data", "/etc/passwd");
    Assert.True(absolute.IsSuccess);
    Assert.Equal("/data/etc/passwd", absolute.Value);

    Assert.Equal("a/b", VolumePath.CombineRelative("a", "b"));
    Assert.Equal("a", VolumePath.ParentRelative("a/b"));
    Assert.Equal("", VolumePath.ParentRelative("a"));
  }

  [Fact]
  public void VolumeListing_parses_tsv_and_skips_dot_entries() {
    var entries = VolumeListing.Parse("""
      d	0	pgdata
      f	1024	postgresql.conf
      f	0	.
      d	0	..
      f	12	lost+found
      """);

    Assert.Equal(["pgdata", "lost+found", "postgresql.conf"], entries.Select(e => e.Name).ToArray());
    Assert.True(entries[0].IsDirectory);
    Assert.Equal("1 KB", entries[2].SizeText);
  }

  [Fact]
  public void VolumeListing_parses_ls_one_per_line() {
    var entries = VolumeListing.Parse("""
      pgdata/
      postgresql.conf
      .
      ..
      lost+found
      """);

    Assert.Equal(["pgdata", "lost+found", "postgresql.conf"], entries.Select(e => e.Name).ToArray());
    Assert.True(entries[0].IsDirectory);
    Assert.False(entries[2].IsDirectory);
  }

  [Fact]
  public void VolumeText_rejects_nul_and_oversize() {
    Assert.True(VolumeText.IsText(Encoding.UTF8.GetBytes("listen_addresses = '*'\n")));
    Assert.True(VolumeText.CanEdit([]));
    Assert.False(VolumeText.IsText([(byte)'a', 0, (byte)'b']));
    Assert.False(VolumeText.CanEdit(new byte[VolumeText.MaxEditBytes + 1]));
  }
}
