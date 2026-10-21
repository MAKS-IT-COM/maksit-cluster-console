using k8s.Models;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Tests;

public class CronJobRunTests {
  [Fact]
  public void A_running_job_blocks_the_same_schedule() {
    var running = Job("system", new V1JobStatus { Active = 1, StartTime = DateTime.UtcNow });
    var starting = Job("system", status: null);
    var started = Job("system", new V1JobStatus { StartTime = DateTime.UtcNow });
    var finished = Job("system", new V1JobStatus { Succeeded = 1 });
    var failed = Job("system", new V1JobStatus {
      Conditions = [new V1JobCondition { Type = "Failed", Status = "True" }]
    });
    var other = Job("default", new V1JobStatus { Active = 1 });
    var stranger = new V1Job {
      Metadata = new V1ObjectMeta { Name = "manual" },
      Status = new V1JobStatus { Active = 1 }
    };

    Assert.True(CronJobRun.IsInProgress(running, "system"));
    Assert.True(CronJobRun.IsInProgress(starting, "system"));
    Assert.True(CronJobRun.IsInProgress(started, "system"));
    Assert.False(CronJobRun.IsInProgress(finished, "system"));
    Assert.False(CronJobRun.IsInProgress(failed, "system"));
    Assert.False(CronJobRun.IsInProgress(other, "system"));
    Assert.False(CronJobRun.IsInProgress(stranger, "system"));
  }

  [Fact]
  public void A_job_with_pods_still_active_is_not_finished() {
    var job = Job("system", new V1JobStatus { Active = 1, Succeeded = 1 });

    Assert.True(CronJobRun.IsInProgress(job, "system"));
  }

  private static V1Job Job(string owner, V1JobStatus? status) =>
    new() {
      Metadata = new V1ObjectMeta {
        Name = owner + "-manual",
        OwnerReferences = [
          new V1OwnerReference { Kind = "CronJob", Name = owner, ApiVersion = "batch/v1", Uid = "uid" }
        ]
      },
      Status = status
    };
}
