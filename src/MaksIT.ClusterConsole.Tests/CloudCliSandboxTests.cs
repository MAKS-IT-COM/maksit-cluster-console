using System.Formats.Tar;
using System.IO.Compression;
using MaksIT.ClusterConsole.Client.KubeConfig;



namespace MaksIT.ClusterConsole.Tests;

public class CloudCliSandboxTests {
  [Fact]
  public void Checksum_file_matches_the_named_asset() {
    var text = """
      c9aca9dfb307e7070e2a4ca3944c1e4df5c2ebb6c311b2fa3f0dde5369a51c02  doctl-2.0.0-linux-amd64.tar.gz
      1111111111111111111111111111111111111111111111111111111111111111 *doctl-2.0.0-linux-arm64.tar.gz
      """;

    Assert.Equal(
      "c9aca9dfb307e7070e2a4ca3944c1e4df5c2ebb6c311b2fa3f0dde5369a51c02",
      CloudCliFiles.HashFor(text, "doctl-2.0.0-linux-amd64.tar.gz"));
    Assert.Equal(
      "1111111111111111111111111111111111111111111111111111111111111111",
      CloudCliFiles.HashFor(text, "doctl-2.0.0-linux-arm64.tar.gz"));
    Assert.Equal(
      "934a2a9618e018cb9fbeb14f3e4c28416fdae815df7706df91500e41d234eb66",
      CloudCliFiles.HashFor("934a2a9618e018cb9fbeb14f3e4c28416fdae815df7706df91500e41d234eb66\n", "kubelogin-linux-amd64.zip"));
    Assert.Null(CloudCliFiles.HashFor("not-a-hash  doctl\n", "doctl"));
  }

  [Fact]
  public void Gcloud_page_hash_is_read_beside_the_file_name() {
    var hash = "351ed4a2812c5525c1cee00334257a42c020fcaffa27d3067c1eec0a618dd83f";
    var page = "<tr><td>" + hash + "</td><td>google-cloud-cli-linux-x86_64.tar.gz</td></tr>";

    Assert.Equal(hash, CloudCliFiles.HashNear(page, "google-cloud-cli-linux-x86_64.tar.gz"));
    Assert.Null(CloudCliFiles.HashNear(page, "google-cloud-cli-linux-arm.tar.gz"));
  }

  [Fact]
  public void Path_prepend_is_idempotent_and_commands_map_to_one_package() {
    var bin = Path.Combine("cloud", "bin");
    var once = CloudCliSandbox.Prepend(Path.Combine("usr", "bin"), [bin]);
    var twice = CloudCliSandbox.Prepend(once, [bin]);

    Assert.Equal(once, twice);
    Assert.StartsWith(bin, once);
    Assert.Equal(["gcloud", "gke-gcloud-auth-plugin"], CloudCliSandbox.CommandsFor("google"));
    Assert.Equal(["gcloud"], CloudCliSandbox.PackagesFor(CloudCliSandbox.CommandsFor("google")));
    Assert.Equal(["azure", "kubelogin"], CloudCliSandbox.PackagesFor(["az", "kubelogin"]));
    Assert.Equal(["gcloud", "gke-gcloud-auth-plugin"], CloudCliSandbox.MissingFrom("google", Path.Combine("usr", "bin")));
    Assert.Empty(CloudCliSandbox.MissingFrom("custom", once));
  }

  [Fact]
  public void Archive_extract_finds_the_cli_and_rejects_a_parent_path() {
    var root = Path.Combine(Path.GetTempPath(), "maksit-cloud-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try {
      var zipPath = Path.Combine(root, "kubelogin.zip");
      using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
        using var entry = zip.CreateEntry("bin/linux_amd64/kubelogin").Open();
        entry.Write("login"u8);
      }

      var login = Path.Combine(root, "kubelogin");
      CloudCliFiles.ExtractNamed(zipPath, "kubelogin", login);
      Assert.Equal("login", File.ReadAllText(login));

      var tarPath = Path.Combine(root, "doctl.tar.gz");
      var payload = Path.Combine(root, "payload");
      File.WriteAllText(payload, "doctl");
      using (var output = File.Create(tarPath)) {
        using var gzip = new GZipStream(output, CompressionMode.Compress);
        using var tar = new TarWriter(gzip);
        tar.WriteEntry(payload, "doctl");
      }

      var extracted = Path.Combine(root, "out");
      CloudCliFiles.ExtractAll(tarPath, extracted);
      Assert.Equal("doctl", File.ReadAllText(Path.Combine(extracted, "doctl")));
      Assert.False(CloudCliFiles.SafeName("../doctl"));
      Assert.False(CloudCliFiles.SafeName("/doctl"));
      Assert.True(CloudCliFiles.SafeName("bin/doctl"));
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Aws_publisher_key_is_the_documented_key() {
    Assert.Equal("A6310ACC4672475C", CloudCliFiles.MasterKeyId());
  }

  [Fact]
  public async Task Install_is_refused_off_linux() {
    if (OperatingSystem.IsLinux())
      return;

    var result = await CloudCliSetup.InstallAsync("doctl", cancellationToken: TestContext.Current.CancellationToken);

    Assert.False(result.IsSuccess);
  }

  [Fact]
  public void Version_file_round_trips() {
    var root = Path.Combine(Path.GetTempPath(), "maksit-cloud-" + Guid.NewGuid().ToString("N"));

    try {
      CloudCliSandbox.WriteVersion(root, "doctl", "v2.0.0");
      Assert.Equal("v2.0.0", CloudCliSandbox.ReadVersion(root, "doctl"));
      Assert.Null(CloudCliSandbox.ReadVersion(root, "aws"));
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }
}
