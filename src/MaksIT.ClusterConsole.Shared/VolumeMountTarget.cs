using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public sealed record VolumeMountTarget(
  string PodName,
  string Namespace,
  string Container,
  string VolumeName,
  string MountPath,
  string? SubPath,
  string Phase) {
  public bool IsRunning =>
    string.Equals(Phase, "Running", StringComparison.OrdinalIgnoreCase);

  public string Root {
    get {
      var mount = string.IsNullOrWhiteSpace(MountPath) ? "/" : MountPath;
      if (string.IsNullOrWhiteSpace(SubPath))
        return mount.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";

      return $"{mount.TrimEnd('/')}/{SubPath.Trim('/')}";
    }
  }

  public string Key =>
    $"{Namespace}/{PodName}/{Container}/{Root}";

  public string Caption =>
    $"{PodName}/{Container}  {Root}";
}

public static class VolumeMounts {
  public static IReadOnlyList<VolumeMountTarget> Find(IEnumerable<JsonObject> pods, string pvcName) {
    var matches = new List<VolumeMountTarget>();
    foreach (var pod in pods)
      matches.AddRange(FromPod(pod, pvcName));

    return matches
      .OrderByDescending(m => m.IsRunning)
      .ThenBy(m => m.Namespace, StringComparer.OrdinalIgnoreCase)
      .ThenBy(m => m.PodName, StringComparer.OrdinalIgnoreCase)
      .ThenBy(m => m.Container, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public static IReadOnlyList<VolumeMountTarget> FromPod(JsonObject pod, string pvcName) {
    if (string.IsNullOrWhiteSpace(pvcName))
      return [];

    var spec = pod["spec"] as JsonObject;
    var volumes = spec?["volumes"] as JsonArray;
    if (volumes is null)
      return [];

    var volumeNames = volumes
      .OfType<JsonObject>()
      .Where(volume => string.Equals(
        volume["persistentVolumeClaim"]?["claimName"]?.GetValue<string>(),
        pvcName,
        StringComparison.Ordinal))
      .Select(volume => volume["name"]?.GetValue<string>())
      .Where(name => !string.IsNullOrEmpty(name))
      .Select(name => name!)
      .ToHashSet(StringComparer.Ordinal);

    if (volumeNames.Count == 0)
      return [];

    var podName = JsonPath.Name(pod);
    var ns = JsonPath.Namespace(pod) ?? "";
    var phase = pod["status"]?["phase"]?.GetValue<string>() ?? "";
    var items = new List<VolumeMountTarget>();

    foreach (var container in (spec?["containers"] as JsonArray)?.OfType<JsonObject>() ?? []) {
      var containerName = container["name"]?.GetValue<string>();
      if (string.IsNullOrEmpty(containerName))
        continue;

      foreach (var mount in (container["volumeMounts"] as JsonArray)?.OfType<JsonObject>() ?? []) {
        var volumeName = mount["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(volumeName) || !volumeNames.Contains(volumeName))
          continue;

        var mountPath = mount["mountPath"]?.GetValue<string>();
        if (string.IsNullOrEmpty(mountPath))
          continue;

        items.Add(new VolumeMountTarget(
          podName,
          ns,
          containerName,
          volumeName,
          mountPath,
          mount["subPath"]?.GetValue<string>(),
          phase));
      }
    }

    return items;
  }
}
