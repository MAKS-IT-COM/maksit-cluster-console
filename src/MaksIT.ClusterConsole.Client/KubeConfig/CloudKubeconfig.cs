using System.Diagnostics;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client.KubeConfig;

public sealed record CloudFetchRequest(
  string PresetId,
  string Cluster,
  string? Location,
  string? Project,
  string KubeconfigPath);

public sealed record CloudCliCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>
/// Official CLI commands that write a full kubeconfig context: API server, CA, and exec login.
/// Provider CLIs, including kubelogin and doctl, are downloaded into the app data folder.
/// A host install still cannot be started from that sandbox.
/// </summary>
public static class CloudKubeconfig {
  public static Result<CloudCliCommand> Build(CloudFetchRequest request) {
    var preset = CloudExecPreset.Find(request.PresetId);

    if (preset is null || !preset.CanFetch)
      return Result<CloudCliCommand>.BadRequest(null, "This provider has no kubeconfig command.");

    if (string.IsNullOrWhiteSpace(request.Cluster))
      return Result<CloudCliCommand>.BadRequest(null, preset.ClusterLabel + " is required.");

    if (preset.LocationLabel.Length > 0 && string.IsNullOrWhiteSpace(request.Location))
      return Result<CloudCliCommand>.BadRequest(null, preset.LocationLabel + " is required.");

    if (preset.ProjectLabel.Length > 0 && string.IsNullOrWhiteSpace(request.Project))
      return Result<CloudCliCommand>.BadRequest(null, preset.ProjectLabel + " is required.");

    if (string.IsNullOrWhiteSpace(request.KubeconfigPath))
      return Result<CloudCliCommand>.BadRequest(null, "Kubeconfig path is required.");

    var cluster = request.Cluster.Trim();
    var location = request.Location?.Trim() ?? "";
    var project = request.Project?.Trim() ?? "";
    var path = request.KubeconfigPath;

    CloudCliCommand command = preset.Id switch {
      "google" => new(
        "gcloud",
        ["container", "clusters", "get-credentials", cluster, "--location", location, "--project", project]),
      "azure" => new(
        "az",
        ["aks", "get-credentials", "--resource-group", project, "--name", cluster, "--file", path]),
      "amazon" => new(
        "aws",
        ["eks", "update-kubeconfig", "--name", cluster, "--region", location, "--kubeconfig", path]),
      "oracle" => new(
        "oci",
        ["ce", "cluster", "create-kubeconfig", "--cluster-id", cluster, "--file", path, "--region", location, "--token-version", "2.0.0"]),
      "digitalocean" => new(
        "doctl",
        ["kubernetes", "cluster", "kubeconfig", "save", cluster]),
      "ibm" => new(
        "ibmcloud",
        ["ks", "cluster", "config", "--cluster", cluster]),
      _ => null!
    };

    if (command is null)
      return Result<CloudCliCommand>.BadRequest(null, "This provider has no kubeconfig command.");

    return Result<CloudCliCommand>.Ok(command);
  }

  public static async Task<Result<string>> RunAsync(
    CloudCliCommand command,
    string kubeconfigPath,
    CancellationToken cancellationToken = default) {
    CloudCliSandbox.ApplyPath();
    var directory = Path.GetDirectoryName(kubeconfigPath);

    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    var fileName = ResolveExecutable(command.FileName);
    var start = new ProcessStartInfo {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    };
    start.Environment["KUBECONFIG"] = kubeconfigPath;

    if (OperatingSystem.IsWindows()
        && (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))) {
      start.FileName = "cmd.exe";
      start.ArgumentList.Add("/d");
      start.ArgumentList.Add("/s");
      start.ArgumentList.Add("/c");
      start.ArgumentList.Add(fileName);
    }
    else {
      start.FileName = fileName;
    }

    foreach (var argument in command.Arguments)
      start.ArgumentList.Add(argument);

    using var process = new Process { StartInfo = start };

    try {
      if (!process.Start())
        return Result<string>.ServiceUnavailable(null, "Could not start " + command.FileName + ".");
    }
    catch (Exception ex) {
      return Result<string>.ServiceUnavailable(null, command.FileName + " did not start. " + ex.Message);
    }

    var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(3));

    try {
      await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      try {
        process.Kill(entireProcessTree: true);
      }
      catch (Exception) {
        // The process may already have exited.
      }

      try {
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
      }
      catch (Exception) {
        // The pipes close when the process is killed.
      }

      return Result<string>.RequestTimeout(null, command.FileName + " did not finish within 3 minutes.");
    }

    var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
    var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

    if (process.ExitCode != 0) {
      var detail = stderr.Length > 0 ? stderr : stdout;

      return Result<string>.BadRequest(null, command.FileName + " failed. " + detail);
    }

    return Result<string>.Ok(stdout.Length > 0 ? stdout : "Context written.");
  }

  private static string ResolveExecutable(string fileName) {
    if (Path.IsPathRooted(fileName) && File.Exists(fileName))
      return fileName;

    var path = Environment.GetEnvironmentVariable("PATH");

    if (string.IsNullOrWhiteSpace(path))
      return fileName;

    var extensions = OperatingSystem.IsWindows()
      ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
      : new[] { "" };

    foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
      var plain = Path.Combine(dir.Trim(), fileName);

      if (File.Exists(plain))
        return plain;

      foreach (var extension in extensions) {
        var candidate = plain + extension;

        if (File.Exists(candidate))
          return candidate;
      }
    }

    return fileName;
  }
}
