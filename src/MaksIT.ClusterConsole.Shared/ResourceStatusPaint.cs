namespace MaksIT.ClusterConsole.Shared;

public enum ResourceStatusTone {
  Neutral,
  Healthy,
  Warning,
  Error,
  Info
}

public static class ResourceStatusPaint {
  public static ResourceStatusTone Tone(string? status) {
    if (string.IsNullOrWhiteSpace(status))
      return ResourceStatusTone.Neutral;

    var value = status.Trim();
    if (Matches(value, "CrashLoopBackOff", "ImagePullBackOff", "ErrImagePull", "Failed", "Error",
          "Evicted", "OOMKilled", "CreateContainerError", "Lost"))
      return ResourceStatusTone.Error;

    if (Contains(value, "fail", "error", "backoff", "unhealthy", "denied"))
      return ResourceStatusTone.Error;

    if (Matches(value, "Pending", "ContainerCreating", "PodInitializing", "Terminating",
          "Released", "Progressing", "Waiting", "Unknown", "Stopped", "NotReady",
          "Orphaned", "Missing"))
      return ResourceStatusTone.Warning;

    if (Contains(value, "pending", "terminat", "init", "progress", "wait"))
      return ResourceStatusTone.Warning;

    if (Matches(value, "Resolved"))
      return ResourceStatusTone.Info;

    if (Matches(value, "Succeeded", "Completed", "Bound", "Available"))
      return ResourceStatusTone.Info;

    if (Matches(value, "Running", "Ready", "Active", "deployed", "True"))
      return ResourceStatusTone.Healthy;

    if (Contains(value, "running", "ready", "active", "deployed", "succeed", "complete", "bound"))
      return ResourceStatusTone.Healthy;

    return ResourceStatusTone.Neutral;
  }

  private static bool Matches(string value, params string[] tokens) {
    foreach (var token in tokens) {
      if (value.Equals(token, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }

  private static bool Contains(string value, params string[] tokens) {
    foreach (var token in tokens) {
      if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }
}
