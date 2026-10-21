using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;



namespace MaksIT.ClusterConsole.Tests;

public class LonghornBackupTests {
  [Fact]
  public void Backup_and_job_columns_are_in_the_longhorn_section() {
    var backups = ResourceCatalog.Find(ResourceCatalog.LonghornBackupsId)!;
    var jobs = ResourceCatalog.Find(ResourceCatalog.LonghornBackupJobsId)!;

    Assert.Equal(ResourceCatalog.Longhorn, backups.Section);
    Assert.Equal("backupvolumes", backups.Plural);
    Assert.Equal("BackupVolume", backups.Kind);
    Assert.Equal(
      ["State", "Volume", "Last Backup", "Last Backup At", "Created", "Size", "Target"],
      backups.Columns.Select(column => column.Header));
    Assert.Equal(["State", "Name", "Task", "Schedule", "Last Successful Run", "Retain", "Groups"], jobs.Columns.Select(column => column.Header));
    Assert.True(backups.Actions.CanDelete);
    Assert.True(jobs.Actions.CanDelete);
  }

  [Fact]
  public void Schedule_editor_round_trips_presets_and_keeps_a_typed_range() {
    Assert.Equal("0 2 * * *", LonghornBackup.Daily.Text);
    Assert.Equal("0 * * * *", LonghornBackup.Hourly.Text);
    Assert.Equal("0 2 * * 0", LonghornBackup.Weekly.Text);
    Assert.Equal("Every day at 02:00", LonghornBackup.Describe(LonghornBackup.Daily.Text));
    Assert.Equal("Every hour", LonghornBackup.Describe(LonghornBackup.Hourly.Text));
    Assert.Equal("Every Sunday at 02:00", LonghornBackup.Describe(LonghornBackup.Weekly.Text));
    Assert.True(LonghornBackup.TryFields(LonghornBackup.Daily.Text, out var fields));
    Assert.Equal(LonghornBackup.Daily, fields);
    Assert.Equal("1-5 * * * *", LonghornBackup.Describe("1-5 * * * *"));
    Assert.True(LonghornBackup.IsFiveField("1-5 * * * *"));
    Assert.False(LonghornBackup.IsFiveField("* * * *"));
    Assert.False(LonghornBackup.TryFields("1-5 * * * *", out _));
  }

  [Fact]
  public void Job_document_is_a_backup_and_the_label_patch_swaps_names() {
    var document = LonghornBackup.JobDocument("nightly", "longhorn-system", "0 2 * * *", 7, "backup", [], 1);
    var spec = document["spec"]!.AsObject();

    Assert.Equal("backup", spec["task"]!.GetValue<string>());
    Assert.Equal("nightly", spec["name"]!.GetValue<string>());
    Assert.Equal("0 2 * * *", spec["cron"]!.GetValue<string>());
    Assert.Equal(7, spec["retain"]!.GetValue<int>());
    Assert.False(spec.ContainsKey("groups"));
    Assert.True(LonghornBackup.IsBackupJob(document));
    Assert.False(LonghornBackup.IsBackupJob(LonghornBackup.JobDocument("snap", "longhorn-system", "0 2 * * *", 7, "snapshot", [], 1)));
    Assert.Equal("backup-force-create", LonghornBackup.StoredTask("backup", true));
    Assert.Equal("Backup (force)", LonghornBackup.Column(
      LonghornBackup.JobDocument("forced", "longhorn-system", "0 2 * * *", 7, "backup-force-create", [], 1),
      "longhorn.backup.task"));

    var system = LonghornBackup.JobDocument(
      "cluster",
      "longhorn-system",
      "0 2 * * 0",
      1,
      "system-backup",
      [],
      1,
      parameters: new Dictionary<string, string> { ["volume-backup-policy"] = "if-not-present" });

    Assert.Equal("system-backup", system["spec"]!["task"]!.GetValue<string>());
    Assert.Equal("if-not-present", system["spec"]!["parameters"]!["volume-backup-policy"]!.GetValue<string>());
    Assert.Equal(6, LonghornBackup.Tasks.Count);
    Assert.All(LonghornBackup.Tasks, task => Assert.True(task.Hint.Length > 40));
    Assert.False(LonghornBackup.IsDnsLabel("Nightly"));
    Assert.True(LonghornBackup.IsDnsLabel("nightly"));

    var patch = LonghornBackup.LabelPatch("daily", "nightly");

    Assert.Contains("\"recurring-job.longhorn.io/nightly\":\"enabled\"", patch, StringComparison.Ordinal);
    Assert.Contains("\"recurring-job.longhorn.io/daily\":null", patch, StringComparison.Ordinal);

    var volume = new JsonObject {
      ["metadata"] = new JsonObject {
        ["labels"] = new JsonObject {
          ["recurring-job.longhorn.io/daily"] = "enabled"
        }
      }
    };

    Assert.True(LonghornBackup.VolumeHasJob(volume, "daily"));
    Assert.False(LonghornBackup.VolumeHasJob(volume, "nightly"));
  }

  [Fact]
  public void Last_successful_run_uses_the_newest_finished_job() {
    var cron = JsonNode.Parse("""
      {
        "metadata": { "name": "system", "namespace": "longhorn-system" },
        "status": { "lastSuccessfulTime": "2026-10-13T22:02:38Z" }
      }
      """)!.AsObject();
    var finished = JsonNode.Parse("""
      {
        "metadata": {
          "name": "system-manual-1",
          "namespace": "longhorn-system",
          "ownerReferences": [{ "kind": "CronJob", "name": "system" }]
        },
        "status": { "succeeded": 1, "completionTime": "2026-10-21T13:50:00Z" }
      }
      """)!.AsObject();
    var running = JsonNode.Parse("""
      {
        "metadata": {
          "name": "system-manual-2",
          "namespace": "longhorn-system",
          "ownerReferences": [{ "kind": "CronJob", "name": "system" }]
        },
        "status": { "startTime": "2026-10-21T13:55:00Z" }
      }
      """)!.AsObject();

    var failed = JsonNode.Parse("""
      {
        "metadata": {
          "name": "system-manual-0",
          "namespace": "longhorn-system",
          "ownerReferences": [{ "kind": "CronJob", "name": "system" }]
        },
        "status": {
          "failed": 1,
          "startTime": "2026-10-20T01:00:00Z",
          "conditions": [{ "type": "Failed", "status": "True", "message": "backup target is unavailable" }]
        }
      }
      """)!.AsObject();

    var runs = LonghornBackup.Runs([cron], [failed, finished, running]);
    var system = runs[LonghornBackup.RunKey("longhorn-system", "system")];

    Assert.Equal("Running", system.State);
    Assert.Equal("A run is in progress.", system.Detail);
    Assert.Equal("2026-10-21 13:50Z", system.LastSuccess);

    var second = JsonNode.Parse("""
      {
        "metadata": {
          "name": "system-manual-3",
          "namespace": "longhorn-system",
          "ownerReferences": [{ "kind": "CronJob", "name": "system" }]
        },
        "status": { "active": 1, "startTime": "2026-10-21T13:56:00Z" }
      }
      """)!.AsObject();
    var both = LonghornBackup.Runs([cron], [running, second]);
    Assert.Equal("2 runs are in progress.", both[LonghornBackup.RunKey("longhorn-system", "system")].Detail);

    var recovered = LonghornBackup.Runs([cron], [failed, finished]);
    Assert.Equal("Idle", recovered[LonghornBackup.RunKey("longhorn-system", "system")].State);

    var broken = LonghornBackup.Runs([], [failed]);
    var failure = broken[LonghornBackup.RunKey("longhorn-system", "system")];
    Assert.Equal("Failed", failure.State);
    Assert.Equal("backup target is unavailable", failure.Detail);
  }

  [Fact]
  public void Backup_row_reads_state_volume_and_created_time() {
    var backup = JsonNode.Parse("""
      {
        "metadata": { "name": "pvc-1-hash", "namespace": "longhorn-system" },
        "spec": { "volumeName": "pvc-1", "backupTargetName": "default" },
        "status": {
          "lastBackupName": "system-backup-1",
          "lastBackupAt": "2026-10-13T22:02:38Z",
          "createdAt": "2026-09-05T13:50:43Z",
          "size": "1073741824",
          "messages": {}
        }
      }
      """)!.AsObject();

    Assert.Equal("Ready", LonghornBackup.Column(backup, "longhorn.backup.state"));
    Assert.Equal("pvc-1", LonghornBackup.Column(backup, "longhorn.backup.volume"));
    Assert.Equal("system-backup-1", LonghornBackup.Column(backup, "longhorn.backup.last"));
    Assert.Equal("2026-10-13 22:02Z", LonghornBackup.Column(backup, "longhorn.backup.lastAt"));
    Assert.Equal("2026-09-05 13:50Z", LonghornBackup.Column(backup, "longhorn.backup.created"));
    Assert.Equal("default", LonghornBackup.Column(backup, "longhorn.backup.target"));
    Assert.False(string.IsNullOrEmpty(LonghornBackup.Column(backup, "longhorn.backup.size")));
  }
}
