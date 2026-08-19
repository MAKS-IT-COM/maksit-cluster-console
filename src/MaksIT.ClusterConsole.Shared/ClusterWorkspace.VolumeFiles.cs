using System.Text;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public sealed partial class ClusterWorkspace {
  public async Task<Result<IReadOnlyList<VolumeMountTarget>>> ListVolumeMountsAsync(
    JsonObject document,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<IReadOnlyList<VolumeMountTarget>>.ServiceUnavailable(null, "not connected");

    if (!VolumeClaim.TryGet(document, out var ns, out var pvcName))
      return Result<IReadOnlyList<VolumeMountTarget>>.NotFound(
        null,
        "This volume is not bound to a PVC. Attach a claim first.");

    var pods = ResourceCatalog.Find("pods")!;
    var listed = await _session.ListAsync(pods.ToRef(), ns, cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<VolumeMountTarget>>(null, false, listed.Messages, listed.StatusCode);

    var mounts = VolumeMounts.Find(listed.Value ?? [], pvcName);
    var running = mounts.Where(m => m.IsRunning).ToList();
    if (running.Count > 0)
      return Result<IReadOnlyList<VolumeMountTarget>>.Ok(running);

    if (mounts.Count > 0)
      return Result<IReadOnlyList<VolumeMountTarget>>.NotFound(
        null,
        "A pod mounts this PVC but is not Running.");

    return Result<IReadOnlyList<VolumeMountTarget>>.NotFound(
      null,
      "No running pod is mounting this PVC. Attach a workload first.");
  }

  public async Task<Result<string>> GetVolumeIdentityAsync(
    VolumeMountTarget mount,
    CancellationToken cancellationToken = default) {
    if (_session is null)
      return Result<string>.ServiceUnavailable(null, "not connected");

    var result = await _session.ExecAsync(
      mount.PodName,
      mount.Namespace,
      mount.Container,
      VolumeFilesCommands.Identity(),
      cancellationToken).ConfigureAwait(false);
    if (!result.IsSuccess)
      return MapShellResult(result);

    var lines = (result.Value ?? "")
      .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (lines.Length == 0)
      return Result<string>.Ok("container user");

    var user = lines[0];
    var uid = lines.Length > 1 ? lines[1] : "";
    var gid = lines.Length > 2 ? lines[2] : "";
    var identity = string.IsNullOrEmpty(uid) ? user : $"{user} ({uid}:{gid})";
    return Result<string>.Ok(identity);
  }

  public async Task<Result<IReadOnlyList<VolumeEntry>>> ListVolumeEntriesAsync(
    VolumeMountTarget mount,
    string relativeDirectory,
    CancellationToken cancellationToken = default) {
    var path = VolumePath.Resolve(mount.Root, relativeDirectory);
    if (!path.IsSuccess || path.Value is null)
      return new Result<IReadOnlyList<VolumeEntry>>(null, false, path.Messages, path.StatusCode);

    var exec = await ExecVolumeAsync(mount, VolumeFilesCommands.List(path.Value), null, cancellationToken)
      .ConfigureAwait(false);
    if (!exec.IsSuccess || exec.Value is null)
      return new Result<IReadOnlyList<VolumeEntry>>(null, false, exec.Messages, exec.StatusCode);

    if (HasExecError(exec.Value) && exec.Value.Stdout.Length == 0)
      return Result<IReadOnlyList<VolumeEntry>>.UnprocessableEntity(null, ExecError(exec.Value));

    var text = Encoding.UTF8.GetString(exec.Value.Stdout);
    return Result<IReadOnlyList<VolumeEntry>>.Ok(VolumeListing.Parse(text));
  }

  public async Task<Result<byte[]>> ReadVolumeFileAsync(
    VolumeMountTarget mount,
    string relativePath,
    CancellationToken cancellationToken = default) {
    var path = VolumePath.Resolve(mount.Root, relativePath);
    if (!path.IsSuccess || path.Value is null)
      return new Result<byte[]>(null, false, path.Messages, path.StatusCode);

    var exec = await ExecVolumeAsync(mount, VolumeFilesCommands.Read(path.Value), null, cancellationToken)
      .ConfigureAwait(false);
    if (!exec.IsSuccess || exec.Value is null)
      return new Result<byte[]>(null, false, exec.Messages, exec.StatusCode);

    if (HasExecError(exec.Value) && exec.Value.Stdout.Length == 0)
      return Result<byte[]>.UnprocessableEntity(null, ExecError(exec.Value));

    return Result<byte[]>.Ok(exec.Value.Stdout);
  }

  public async Task<Result> WriteVolumeFileAsync(
    VolumeMountTarget mount,
    string relativePath,
    byte[] data,
    CancellationToken cancellationToken = default) {
    var path = VolumePath.Resolve(mount.Root, relativePath);
    if (!path.IsSuccess || path.Value is null)
      return path.ToResult();

    var exec = await ExecVolumeAsync(mount, VolumeFilesCommands.Write(path.Value), data, cancellationToken)
      .ConfigureAwait(false);
    if (!exec.IsSuccess)
      return exec.ToResult();

    if (exec.Value is not null && HasExecError(exec.Value))
      return Result.UnprocessableEntity(ExecError(exec.Value));

    return Result.Ok();
  }

  private async Task<Result<ExecBytesResult>> ExecVolumeAsync(
    VolumeMountTarget mount,
    IReadOnlyList<string> command,
    byte[]? stdin,
    CancellationToken cancellationToken) {
    if (_session is null)
      return Result<ExecBytesResult>.ServiceUnavailable(null, "not connected");

    var result = await _session.ExecBytesAsync(
      mount.PodName,
      mount.Namespace,
      mount.Container,
      command,
      stdin,
      cancellationToken).ConfigureAwait(false);
    if (!result.IsSuccess)
      return MapShellResult(result);

    return result;
  }

  private static Result<T> MapShellResult<T>(Result<T> result) {
    var message = string.Join("; ", result.Messages);
    if (message.Contains("executable file not found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase))
      return new Result<T>(
        default,
        false,
        ["This container has no shell; cannot browse the volume."],
        result.StatusCode);

    return result;
  }

  private static bool HasExecError(ExecBytesResult result) {
    if (string.IsNullOrWhiteSpace(result.Stderr) || IsKubeSuccessStatus(result.Stderr))
      return false;

    return result.Stdout.Length == 0;
  }

  private static bool IsKubeSuccessStatus(string text) =>
    text.Contains("\"status\":\"Success\"", StringComparison.OrdinalIgnoreCase)
    || text.Contains("\"status\": \"Success\"", StringComparison.OrdinalIgnoreCase);

  private static string ExecError(ExecBytesResult result) {
    if (result.Stderr.Contains("executable file not found", StringComparison.OrdinalIgnoreCase))
      return "This container has no shell; cannot browse the volume.";

    return result.Stderr;
  }
}
