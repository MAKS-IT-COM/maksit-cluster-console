using k8s.Models;


namespace MaksIT.ClusterConsole.Client.Cluster;

/// <summary>
/// A Job still counts as a run of its CronJob until that Job has succeeded or failed.
/// </summary>
internal static class CronJobRun {
  public static bool IsInProgress(V1Job job, string cronJobName) {
    if (!BelongsTo(job, cronJobName))
      return false;

    return !IsFinished(job);
  }

  private static bool BelongsTo(V1Job job, string cronJobName) {
    var owners = job.Metadata?.OwnerReferences;

    if (owners is null)
      return false;

    foreach (var owner in owners) {
      if (string.Equals(owner.Kind, "CronJob", StringComparison.Ordinal) && string.Equals(owner.Name, cronJobName, StringComparison.Ordinal))
        return true;
    }

    return false;
  }

  private static bool IsFinished(V1Job job) {
    var status = job.Status;

    if (status is null)
      return false;

    if (HasTerminalCondition(status))
      return true;

    if (status.Active is > 0)
      return false;

    return status.Succeeded is > 0;
  }

  private static bool HasTerminalCondition(V1JobStatus status) {
    if (status.Conditions is null)
      return false;

    foreach (var condition in status.Conditions) {
      if (!string.Equals(condition.Status, "True", StringComparison.OrdinalIgnoreCase))
        continue;

      if (condition.Type is "Complete" or "Failed")
        return true;
    }

    return false;
  }
}
