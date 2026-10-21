using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MaksIT.Results;



namespace MaksIT.ClusterConsole.Client.KubeConfig;

/// <summary>
/// Downloads provider CLIs into the app data folder after install.
/// Archives are checked with the publisher checksum. The AWS CLI zip is checked
/// with the AWS CLI public key. az and oci are installed with pip into a private
/// Python so the Flatpak runtime does not need a system Python.
/// </summary>
public static class CloudCliSetup {
  private static readonly HttpClient Http = CreateHttp();
  private static readonly SemaphoreSlim Gate = new(1, 1);

  public static async Task<Result<string>> InstallMissingAsync(
    string? presetId,
    Action<string>? report = null,
    CancellationToken cancellationToken = default) {
    if (!OperatingSystem.IsLinux())
      return Result<string>.BadRequest(null, "Provider CLIs are downloaded on Linux, including Flatpak.");

    var packages = CloudCliSandbox.PackagesFor(CloudCliSandbox.Missing(presetId));

    if (packages.Count == 0)
      return Result<string>.Ok("Already installed.");

    foreach (var package in packages) {
      var installed = await InstallAsync(package, report, cancellationToken).ConfigureAwait(false);

      if (!installed.IsSuccess)
        return installed;
    }

    CloudCliSandbox.ApplyPath();

    return Result<string>.Ok("Installed.");
  }

  public static async Task UpdateInstalledAsync(CancellationToken cancellationToken = default) {
    try {
      if (!OperatingSystem.IsLinux())
        return;

      foreach (var package in new[] { "python", "gcloud", "azure", "oci", "aws", "ibm", "kubelogin", "doctl" }) {
        if (CloudCliSandbox.ReadVersion(CloudCliSandbox.DefaultRoot, package) is null)
          continue;

        await InstallAsync(package, null, cancellationToken).ConfigureAwait(false);
      }

      CloudCliSandbox.ApplyPath();
    }
    catch (Exception) {
      // A failed update leaves the installed CLI in place.
    }
  }

  public static async Task<Result<string>> InstallAsync(
    string package,
    Action<string>? report = null,
    CancellationToken cancellationToken = default) {
    await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

    try {
      return await InstallCoreAsync(package, CloudCliSandbox.DefaultRoot, report, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      throw;
    }
    catch (Exception ex) {
      return Result<string>.ServiceUnavailable(null, package + " did not install. " + ex.Message);
    }
    finally {
      Gate.Release();
    }
  }

  private static async Task<Result<string>> InstallCoreAsync(
    string package,
    string root,
    Action<string>? report,
    CancellationToken cancellationToken) {
    if (!OperatingSystem.IsLinux())
      return Result<string>.BadRequest(null, "Provider CLIs are downloaded on Linux, including Flatpak.");

    if (CloudCliSandbox.LinuxToken() is null)
      return Result<string>.BadRequest(null, "This CPU has no portable build.");

    return package switch {
      "python" => await EnsurePythonAsync(root, report, cancellationToken).ConfigureAwait(false),
      "azure" => await InstallPipAsync(root, "azure", "azure-cli", "az", report, cancellationToken).ConfigureAwait(false),
      "oci" => await InstallPipAsync(root, "oci", "oci-cli", "oci", report, cancellationToken).ConfigureAwait(false),
      "gcloud" => await InstallGcloudAsync(root, report, cancellationToken).ConfigureAwait(false),
      "aws" => await InstallAwsAsync(root, report, cancellationToken).ConfigureAwait(false),
      "ibm" => await InstallIbmAsync(root, report, cancellationToken).ConfigureAwait(false),
      "kubelogin" => await InstallKubeloginAsync(root, report, cancellationToken).ConfigureAwait(false),
      "doctl" => await InstallDoctlAsync(root, report, cancellationToken).ConfigureAwait(false),
      _ => Result<string>.BadRequest(null, "Unknown CLI " + package + ".")
    };
  }

  private static async Task<Result<string>> EnsurePythonAsync(string root, Action<string>? report, CancellationToken cancellationToken) {
    var triple = CloudCliSandbox.LinuxToken() == "arm64"
      ? "aarch64-unknown-linux-gnu"
      : "x86_64-unknown-linux-gnu";
    report?.Invoke("Checking Python…");
    var tag = await CloudCliFiles.LatestGitHubTagAsync(Http, "astral-sh", "python-build-standalone", cancellationToken).ConfigureAwait(false);

    if (!tag.IsSuccess || tag.Value is null)
      return Result<string>.BadRequest(null, string.Join("; ", tag.Messages));

    var current = PythonPath(root);

    if (CloudCliSandbox.ReadVersion(root, "python") == tag.Value && current is not null)
      return Result<string>.Ok(current);

    report?.Invoke("Downloading Python…");
    var release = "https://github.com/astral-sh/python-build-standalone/releases/download/" + Uri.EscapeDataString(tag.Value) + "/";
    var sums = await CloudCliFiles.DownloadTextAsync(Http, release + "SHA256SUMS", cancellationToken).ConfigureAwait(false);

    if (!sums.IsSuccess || sums.Value is null)
      return sums;

    var name = Regex.Match(
      sums.Value,
      "cpython-3\\.12\\.[0-9.]+\\+" + Regex.Escape(tag.Value) + "-" + triple + "-install_only\\.tar\\.gz").Value;

    if (name.Length == 0)
      return Result<string>.BadRequest(null, "Python 3.12 was not in the release.");

    var expected = CloudCliFiles.HashFor(sums.Value, name);

    if (expected is null)
      return Result<string>.BadRequest(null, "Python checksum is missing.");

    var archive = Path.Combine(root, "python.download.tar.gz");
    var downloaded = await CloudCliFiles.DownloadAsync(Http, release + Uri.EscapeDataString(name), archive, cancellationToken).ConfigureAwait(false);

    if (!downloaded.IsSuccess)
      return downloaded;

    if (!await HashMatchesAsync(archive, expected, cancellationToken).ConfigureAwait(false))
      return Result<string>.BadRequest(null, "Python checksum did not match.");

    var home = Path.Combine(root, "python-home");
    ReplaceTree(archive, home);
    File.Delete(archive);
    var python = CloudCliFiles.FindNamed(home, "python3");

    if (python is null)
      return Result<string>.BadRequest(null, "The Python archive has no python3.");

    CloudCliSandbox.MarkExecutable(python);
    CloudCliSandbox.WriteVersion(root, "python", tag.Value);

    return Result<string>.Ok(python);
  }

  private static async Task<Result<string>> InstallPipAsync(
    string root,
    string packageId,
    string pipName,
    string command,
    Action<string>? report,
    CancellationToken cancellationToken) {
    var python = await EnsurePythonAsync(root, report, cancellationToken).ConfigureAwait(false);

    if (!python.IsSuccess || python.Value is null)
      return python;

    report?.Invoke("Checking " + command + "…");
    var meta = await CloudCliFiles.DownloadTextAsync(Http, "https://pypi.org/pypi/" + pipName + "/json", cancellationToken).ConfigureAwait(false);

    if (!meta.IsSuccess || meta.Value is null)
      return meta;

    using var json = JsonDocument.Parse(meta.Value);
    var version = json.RootElement.GetProperty("info").GetProperty("version").GetString();

    if (string.IsNullOrWhiteSpace(version))
      return Result<string>.BadRequest(null, "Could not read the " + pipName + " version.");

    var venv = Path.Combine(root, packageId, "venv");
    var tool = Path.Combine(venv, "bin", command);

    if (CloudCliSandbox.ReadVersion(root, packageId) == version && File.Exists(tool)) {
      CloudCliSandbox.LinkCommand(CloudCliSandbox.BinDirectory(root), command, tool);

      return Result<string>.Ok(version);
    }

    report?.Invoke("Downloading " + command + "…");

    if (!File.Exists(Path.Combine(venv, "bin", "python"))) {
      var created = await RunAsync(python.Value, ["-m", "venv", venv], null, cancellationToken).ConfigureAwait(false);

      if (!created.IsSuccess)
        return created;
    }

    var venvPython = Path.Combine(venv, "bin", "python");
    Dictionary<string, string> env = new() { ["PYTHONNOUSERSITE"] = "1" };
    var upgraded = await RunAsync(venvPython, ["-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip"], env, cancellationToken).ConfigureAwait(false);

    if (!upgraded.IsSuccess)
      return upgraded;

    var installed = await RunAsync(
      venvPython,
      ["-m", "pip", "install", "--disable-pip-version-check", pipName + "==" + version],
      env,
      cancellationToken).ConfigureAwait(false);

    if (!installed.IsSuccess)
      return installed;

    if (!File.Exists(tool))
      return Result<string>.BadRequest(null, command + " was not installed.");

    CloudCliSandbox.MarkExecutable(tool);
    CloudCliSandbox.LinkCommand(CloudCliSandbox.BinDirectory(root), command, tool);
    CloudCliSandbox.WriteVersion(root, packageId, version);

    return Result<string>.Ok(version);
  }

  private static async Task<Result<string>> InstallGcloudAsync(string root, Action<string>? report, CancellationToken cancellationToken) {
    var arch = CloudCliSandbox.LinuxToken() == "arm64" ? "linux-arm" : "linux-x86_64";
    var fileName = "google-cloud-cli-" + arch + ".tar.gz";
    report?.Invoke("Checking gcloud…");
    string? expected = null;

    foreach (var pageUrl in new[] {
      "https://docs.cloud.google.com/sdk/docs/downloads-versioned-archives",
      "https://cloud.google.com/sdk/docs/downloads-versioned-archives"
    }) {
      var page = await CloudCliFiles.DownloadTextAsync(Http, pageUrl, cancellationToken).ConfigureAwait(false);

      if (!page.IsSuccess || page.Value is null)
        continue;

      expected = CloudCliFiles.HashNear(page.Value, fileName);

      if (expected is not null)
        break;
    }

    if (expected is null)
      return Result<string>.BadRequest(null, "Could not read the gcloud checksum.");

    var sdk = FindInBin(Path.Combine(root, "gcloud"), "gcloud");
    var plugin = FindInBin(Path.Combine(root, "gcloud"), "gke-gcloud-auth-plugin");

    if (string.Equals(CloudCliSandbox.ReadVersion(root, "gcloud"), expected, StringComparison.OrdinalIgnoreCase)
        && sdk is not null
        && plugin is not null) {
      LinkGcloud(root, sdk, plugin);

      return Result<string>.Ok(expected);
    }

    var python = await EnsurePythonAsync(root, report, cancellationToken).ConfigureAwait(false);

    if (!python.IsSuccess || python.Value is null)
      return python;

    report?.Invoke("Downloading gcloud…");
    var archive = Path.Combine(root, fileName);
    var downloaded = await CloudCliFiles.DownloadAsync(
      Http,
      "https://dl.google.com/dl/cloudsdk/channels/rapid/downloads/" + fileName,
      archive,
      cancellationToken).ConfigureAwait(false);

    if (!downloaded.IsSuccess)
      return downloaded;

    if (!await HashMatchesAsync(archive, expected, cancellationToken).ConfigureAwait(false))
      return Result<string>.BadRequest(null, "gcloud checksum did not match.");

    var home = Path.Combine(root, "gcloud");
    ReplaceTree(archive, home);
    File.Delete(archive);
    sdk = FindInBin(home, "gcloud");

    if (sdk is null)
      return Result<string>.BadRequest(null, "The gcloud archive has no bin/gcloud.");

    CloudCliSandbox.MarkExecutable(sdk);
    LinkGcloud(root, sdk, null);
    report?.Invoke("Downloading gke-gcloud-auth-plugin…");
    var component = await RunAsync(
      sdk,
      ["--quiet", "components", "install", "gke-gcloud-auth-plugin"],
      GcloudEnvironment(root, python.Value),
      cancellationToken).ConfigureAwait(false);

    if (!component.IsSuccess)
      return component;

    plugin = FindInBin(home, "gke-gcloud-auth-plugin");

    if (plugin is null)
      return Result<string>.BadRequest(null, "gke-gcloud-auth-plugin was not installed.");

    CloudCliSandbox.MarkExecutable(plugin);
    LinkGcloud(root, sdk, plugin);
    CloudCliSandbox.WriteVersion(root, "gcloud", expected);

    return Result<string>.Ok(expected);
  }

  private static async Task<Result<string>> InstallAwsAsync(string root, Action<string>? report, CancellationToken cancellationToken) {
    var fileName = CloudCliSandbox.LinuxToken() == "arm64"
      ? "awscli-exe-linux-aarch64.zip"
      : "awscli-exe-linux-x86_64.zip";
    var url = "https://awscli.amazonaws.com/" + fileName;
    report?.Invoke("Checking aws…");
    var token = await CloudCliFiles.HeadTokenAsync(Http, url, cancellationToken).ConfigureAwait(false);

    if (token is not null
        && CloudCliSandbox.ReadVersion(root, "aws") == token
        && File.Exists(Path.Combine(CloudCliSandbox.BinDirectory(root), "aws")))
      return Result<string>.Ok(token);

    report?.Invoke("Downloading aws…");
    var archive = Path.Combine(root, fileName);
    var signaturePath = archive + ".sig";
    var zip = await CloudCliFiles.DownloadAsync(Http, url, archive, cancellationToken).ConfigureAwait(false);

    if (!zip.IsSuccess)
      return zip;

    var sig = await CloudCliFiles.DownloadAsync(Http, url + ".sig", signaturePath, cancellationToken).ConfigureAwait(false);

    if (!sig.IsSuccess)
      return sig;

    var signature = await File.ReadAllBytesAsync(signaturePath, cancellationToken).ConfigureAwait(false);

    if (!CloudCliFiles.SignatureMatches(archive, signature)) {
      File.Delete(archive);
      File.Delete(signaturePath);

      return Result<string>.BadRequest(null, "The AWS CLI signature did not match.");
    }

    var extract = Path.Combine(root, "aws-src");

    if (Directory.Exists(extract))
      Directory.Delete(extract, recursive: true);

    CloudCliFiles.ExtractAll(archive, extract);
    File.Delete(archive);
    File.Delete(signaturePath);
    var script = CloudCliFiles.FindNamed(extract, "install");

    if (script is null)
      return Result<string>.BadRequest(null, "The AWS CLI archive has no install script.");

    CloudCliSandbox.MarkExecutable(script);
    var bin = CloudCliSandbox.BinDirectory(root);
    Directory.CreateDirectory(bin);
    var shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
    var installDir = Path.Combine(root, "aws", "install");
    var installArgs = new List<string> { script, "--install-dir", installDir, "--bin-dir", bin };

    if (Directory.Exists(installDir))
      installArgs.Add("--update");

    var ran = await RunAsync(shell, installArgs, null, cancellationToken).ConfigureAwait(false);

    if (!ran.IsSuccess)
      return ran;

    if (token is null)
      token = await CloudCliFiles.HashFileAsync(Path.Combine(bin, "aws"), cancellationToken).ConfigureAwait(false);

    CloudCliSandbox.WriteVersion(root, "aws", token);

    return Result<string>.Ok(token);
  }

  private static async Task<Result<string>> InstallIbmAsync(string root, Action<string>? report, CancellationToken cancellationToken) {
    report?.Invoke("Checking ibmcloud…");
    var tag = await CloudCliFiles.LatestGitHubTagAsync(Http, "IBM-Cloud", "ibm-cloud-cli-release", cancellationToken).ConfigureAwait(false);

    if (!tag.IsSuccess || tag.Value is null)
      return Result<string>.BadRequest(null, string.Join("; ", tag.Messages));

    var version = tag.Value.StartsWith('v') ? tag.Value[1..] : tag.Value;
    var arch = CloudCliSandbox.LinuxToken() == "arm64" ? "linux_arm64" : "linux_amd64";
    var dest = Path.Combine(CloudCliSandbox.BinDirectory(root), "ibmcloud");

    if (CloudCliSandbox.ReadVersion(root, "ibm") == tag.Value && File.Exists(dest))
      return Result<string>.Ok(tag.Value);

    report?.Invoke("Downloading ibmcloud…");
    // IBM publishes no checksum. The file is accepted only from download.clis.cloud.ibm.com.
    var url = "https://download.clis.cloud.ibm.com/ibm-cloud-cli/" + version + "/binaries/IBM_Cloud_CLI_" + version + "_" + arch + ".tgz";
    var archive = Path.Combine(root, "ibmcloud.tgz");
    var downloaded = await CloudCliFiles.DownloadAsync(Http, url, archive, cancellationToken).ConfigureAwait(false);

    if (!downloaded.IsSuccess)
      return downloaded;

    Directory.CreateDirectory(CloudCliSandbox.BinDirectory(root));
    CloudCliFiles.ExtractNamed(archive, "ibmcloud", dest);
    File.Delete(archive);
    CloudCliSandbox.MarkExecutable(dest);
    CloudCliSandbox.ApplyPath(root);
    report?.Invoke("Downloading the IBM Kubernetes plugin…");
    var plugin = await RunAsync(
      dest,
      ["plugin", "install", "container-service"],
      IbmEnvironment(root),
      cancellationToken,
      confirm: true).ConfigureAwait(false);

    if (!plugin.IsSuccess
        && !string.Join("; ", plugin.Messages).Contains("already installed", StringComparison.OrdinalIgnoreCase))
      return plugin;

    CloudCliSandbox.WriteVersion(root, "ibm", tag.Value);

    return Result<string>.Ok(tag.Value);
  }

  private static Task<Result<string>> InstallKubeloginAsync(string root, Action<string>? report, CancellationToken cancellationToken) =>
    InstallGitHubBinaryAsync(
      root,
      "kubelogin",
      "Azure",
      "kubelogin",
      tag => "kubelogin-linux-" + CloudCliSandbox.LinuxToken() + ".zip",
      (tag, asset) => asset + ".sha256",
      report,
      cancellationToken);

  private static Task<Result<string>> InstallDoctlAsync(string root, Action<string>? report, CancellationToken cancellationToken) =>
    InstallGitHubBinaryAsync(
      root,
      "doctl",
      "digitalocean",
      "doctl",
      tag => "doctl-" + (tag.StartsWith('v') ? tag[1..] : tag) + "-linux-" + CloudCliSandbox.LinuxToken() + ".tar.gz",
      (tag, _) => "doctl-" + (tag.StartsWith('v') ? tag[1..] : tag) + "-checksums.sha256",
      report,
      cancellationToken);

  private static async Task<Result<string>> InstallGitHubBinaryAsync(
    string root,
    string command,
    string owner,
    string repo,
    Func<string, string> assetName,
    Func<string, string, string> checksumName,
    Action<string>? report,
    CancellationToken cancellationToken) {
    report?.Invoke("Checking " + command + "…");
    var tag = await CloudCliFiles.LatestGitHubTagAsync(Http, owner, repo, cancellationToken).ConfigureAwait(false);

    if (!tag.IsSuccess || tag.Value is null)
      return Result<string>.BadRequest(null, string.Join("; ", tag.Messages));

    var dest = Path.Combine(CloudCliSandbox.BinDirectory(root), command);

    if (CloudCliSandbox.ReadVersion(root, command) == tag.Value && File.Exists(dest))
      return Result<string>.Ok(tag.Value);

    var asset = assetName(tag.Value);
    var checksumAsset = checksumName(tag.Value, asset);
    var release = "https://github.com/" + owner + "/" + repo + "/releases/download/" + Uri.EscapeDataString(tag.Value) + "/";
    report?.Invoke("Downloading " + command + "…");
    var sums = await CloudCliFiles.DownloadTextAsync(Http, release + Uri.EscapeDataString(checksumAsset), cancellationToken).ConfigureAwait(false);

    if (!sums.IsSuccess || sums.Value is null)
      return sums;

    var expected = CloudCliFiles.HashFor(sums.Value, asset);

    if (expected is null)
      return Result<string>.BadRequest(null, "Checksum file has no entry for " + asset + ".");

    var archive = Path.Combine(root, asset);
    var downloaded = await CloudCliFiles.DownloadAsync(Http, release + Uri.EscapeDataString(asset), archive, cancellationToken).ConfigureAwait(false);

    if (!downloaded.IsSuccess)
      return downloaded;

    if (!await HashMatchesAsync(archive, expected, cancellationToken).ConfigureAwait(false))
      return Result<string>.BadRequest(null, command + " checksum did not match.");

    Directory.CreateDirectory(CloudCliSandbox.BinDirectory(root));
    CloudCliFiles.ExtractNamed(archive, command, dest);
    File.Delete(archive);
    CloudCliSandbox.MarkExecutable(dest);
    CloudCliSandbox.WriteVersion(root, command, tag.Value);

    return Result<string>.Ok(tag.Value);
  }

  private static void LinkGcloud(string root, string gcloud, string? plugin) {
    var bin = CloudCliSandbox.BinDirectory(root);
    var python = PythonPath(root) ?? "";
    WriteEnvCommand(bin, "gcloud", gcloud, python);
    var gsutil = Path.Combine(Path.GetDirectoryName(gcloud)!, "gsutil");

    if (File.Exists(gsutil))
      WriteEnvCommand(bin, "gsutil", gsutil, python);

    if (plugin is not null)
      CloudCliSandbox.LinkCommand(bin, "gke-gcloud-auth-plugin", plugin);
  }

  private static Dictionary<string, string> GcloudEnvironment(string root, string python) =>
    new() {
      ["CLOUDSDK_PYTHON"] = python,
      ["CLOUDSDK_CORE_DISABLE_PROMPTS"] = "1",
      ["PATH"] = CloudCliSandbox.BinDirectory(root) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
    };

  private static Dictionary<string, string> IbmEnvironment(string root) =>
    new() {
      ["IBMCLOUD_VERSION_CHECK"] = "false",
      ["PATH"] = CloudCliSandbox.BinDirectory(root) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
    };

  private static void WriteEnvCommand(string bin, string name, string target, string python) {
    Directory.CreateDirectory(bin);
    var body = new StringBuilder();
    body.Append("#!/bin/sh\n");

    if (python.Length > 0)
      body.Append("export CLOUDSDK_PYTHON='").Append(python.Replace("'", "'\\''")).Append("'\n");

    body.Append("export CLOUDSDK_CORE_DISABLE_PROMPTS=1\n");
    body.Append("exec '").Append(target.Replace("'", "'\\''")).Append("' \"$@\"\n");
    var path = Path.Combine(bin, name);
    File.WriteAllText(path, body.ToString());
    CloudCliSandbox.MarkExecutable(path);
  }

  private static void ReplaceTree(string archive, string home) {
    var staging = home + ".new";

    if (Directory.Exists(staging))
      Directory.Delete(staging, recursive: true);

    CloudCliFiles.ExtractAll(archive, staging);

    if (Directory.Exists(home))
      Directory.Delete(home, recursive: true);

    Directory.Move(staging, home);
  }

  private static string? PythonPath(string root) =>
    CloudCliFiles.FindNamed(Path.Combine(root, "python-home"), "python3");

  private static string? FindInBin(string root, string name) {
    if (!Directory.Exists(root))
      return null;

    return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
      .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.Ordinal))
      .OrderBy(path => path.Length)
      .FirstOrDefault();
  }

  private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken cancellationToken) {
    var actual = await CloudCliFiles.HashFileAsync(path, cancellationToken).ConfigureAwait(false);
    var matches = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    if (!matches)
      File.Delete(path);

    return matches;
  }

  private static async Task<Result<string>> RunAsync(
    string file,
    IReadOnlyList<string> args,
    IReadOnlyDictionary<string, string>? env,
    CancellationToken cancellationToken,
    bool confirm = false) {
    var start = new ProcessStartInfo {
      FileName = file,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = true
    };

    foreach (var arg in args)
      start.ArgumentList.Add(arg);

    if (env is not null) {
      foreach (var pair in env)
        start.Environment[pair.Key] = pair.Value;
    }

    using var process = new Process { StartInfo = start };

    try {
      if (!process.Start())
        return Result<string>.ServiceUnavailable(null, "Could not start " + Path.GetFileName(file) + ".");
    }
    catch (Exception ex) {
      return Result<string>.ServiceUnavailable(null, Path.GetFileName(file) + " did not start. " + ex.Message);
    }

    var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

    if (confirm)
      await process.StandardInput.WriteLineAsync("y").ConfigureAwait(false);

    process.StandardInput.Close();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(20));

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

      throw;
    }

    var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
    var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

    if (process.ExitCode != 0)
      return Result<string>.BadRequest(null, Path.GetFileName(file) + " failed. " + (stderr.Length > 0 ? stderr : stdout));

    return Result<string>.Ok(stdout);
  }

  private static HttpClient CreateHttp() {
    var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) {
      Timeout = TimeSpan.FromMinutes(20)
    };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MaksIT.ClusterConsole");

    return http;
  }
}
