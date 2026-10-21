using System.Runtime.InteropServices;



namespace MaksIT.ClusterConsole.Client.KubeConfig;

/// <summary>
/// Provider CLIs downloaded into the app data folder. The directory is prepended
/// to this process PATH so a Flatpak sandbox can start them.
/// </summary>
public static class CloudCliSandbox {
  public static string DefaultRoot =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "MaksIT",
      "Cluster Console",
      "cloud-cli");

  public static string BinDirectory(string? root = null) =>
    Path.Combine(root ?? DefaultRoot, "bin");

  public static IReadOnlyList<string> CommandsFor(string? presetId) =>
    presetId switch {
      "google" => ["gcloud", "gke-gcloud-auth-plugin"],
      "azure" => ["az", "kubelogin"],
      "amazon" => ["aws"],
      "oracle" => ["oci"],
      "digitalocean" => ["doctl"],
      "ibm" => ["ibmcloud"],
      _ => []
    };

  public static IReadOnlyList<string> PackagesFor(IEnumerable<string> missingCommands) {
    var packages = new List<string>();

    foreach (var command in missingCommands) {
      var package = command switch {
        "gcloud" or "gke-gcloud-auth-plugin" => "gcloud",
        "az" => "azure",
        "oci" => "oci",
        "kubelogin" => "kubelogin",
        "doctl" => "doctl",
        "aws" => "aws",
        "ibmcloud" => "ibm",
        _ => ""
      };

      if (package.Length > 0 && !packages.Contains(package))
        packages.Add(package);
    }

    return packages;
  }

  public static IReadOnlyList<string> Missing(string? presetId) {
    ApplyPath();

    return MissingFrom(presetId, Environment.GetEnvironmentVariable("PATH"));
  }

  public static IReadOnlyList<string> MissingFrom(string? presetId, string? path) =>
    CommandsFor(presetId).Where(command => !CommandExists(path, command)).ToList();

  public static void ApplyPath(string? root = null) {
    var bin = BinDirectory(root);

    if (!Directory.Exists(bin))
      return;

    var next = Prepend(Environment.GetEnvironmentVariable("PATH"), [bin]);
    Environment.SetEnvironmentVariable("PATH", next);
  }

  public static string Prepend(string? current, IEnumerable<string> directories) {
    var parts = (current ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
    var missing = directories.Where(dir => !parts.Any(part => PathEquals(part, dir))).ToList();

    if (missing.Count == 0)
      return string.Join(Path.PathSeparator, parts);

    return string.Join(Path.PathSeparator, missing.Concat(parts));
  }

  public static string? ReadVersion(string root, string packageId) {
    var path = Path.Combine(root, packageId, "version");

    if (!File.Exists(path))
      return null;

    var text = File.ReadAllText(path).Trim();

    return text.Length == 0 ? null : text;
  }

  public static void WriteVersion(string root, string packageId, string version) {
    var dir = Path.Combine(root, packageId);
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "version"), version);
  }

  internal static string? LinuxToken() =>
    RuntimeInformation.OSArchitecture switch {
      Architecture.X64 => "amd64",
      Architecture.Arm64 => "arm64",
      _ => null
    };

  internal static void MarkExecutable(string path) {
    if (OperatingSystem.IsWindows())
      return;

    File.SetUnixFileMode(
      path,
      UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
      | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
      | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
  }

  internal static void LinkCommand(string binDirectory, string name, string target) {
    Directory.CreateDirectory(binDirectory);
    var link = Path.Combine(binDirectory, name);

    if (File.Exists(link))
      File.Delete(link);

    try {
      File.CreateSymbolicLink(link, target);
    }
    catch (Exception) {
      var script = "#!/bin/sh\nexec '" + target.Replace("'", "'\\''") + "' \"$@\"\n";
      File.WriteAllText(link, script);
      MarkExecutable(link);
    }
  }

  private static bool CommandExists(string? path, string name) {
    if (string.IsNullOrWhiteSpace(path))
      return false;

    foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
      if (File.Exists(Path.Combine(dir.Trim(), name)))
        return true;
    }

    return false;
  }

  private static bool PathEquals(string left, string right) =>
    string.Equals(
      left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
      right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
