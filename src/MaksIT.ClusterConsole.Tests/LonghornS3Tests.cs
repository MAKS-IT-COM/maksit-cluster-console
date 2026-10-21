using System.Text;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.S3;


namespace MaksIT.ClusterConsole.Tests;

public class LonghornS3Tests {
  [Fact]
  public void Backup_url_round_trips_and_rejects_other_schemes() {
    Assert.True(S3Layout.TryParseUrl("s3://backup@us-east-1/longhorn", out var bucket, out var region, out var prefix));
    Assert.Equal("backup", bucket);
    Assert.Equal("us-east-1", region);
    Assert.Equal("longhorn", prefix);
    Assert.Equal("s3://backup@us-east-1/longhorn", S3Layout.FormatUrl(bucket, region, prefix));
    Assert.True(S3Layout.TryParseUrl("s3://backup@us-east-1/", out _, out _, out var root));
    Assert.Equal("", root);
    Assert.True(S3Layout.TryParseUrl("s3://backup@minio", out _, out var custom, out _));
    Assert.Equal("minio", custom);
    Assert.False(S3Layout.TryParseUrl("nfs://server/share", out _, out _, out _));
    Assert.False(S3Layout.TryParseUrl("s3://backup/no-region", out _, out _, out _));
  }

  [Fact]
  public void Path_style_follows_the_endpoint_and_the_virtual_host_flag() {
    Assert.True(S3Layout.PathStyle("http://s3.example:9000", ""));
    Assert.False(S3Layout.PathStyle("http://s3.example:9000", "true"));
    Assert.False(S3Layout.PathStyle("", ""));
    Assert.True(S3Layout.PathStyle("", "false"));
    Assert.Equal("http://s3.example:9000", S3Layout.FirstEndpoint(" http://s3.example:9000/, http://other "));
  }

  [Fact]
  public void Keys_navigate_a_folder_and_reject_a_copy_into_itself() {
    Assert.Equal("a/b/", S3Layout.NormalizePrefix("/a/b"));
    Assert.Equal("a/b/", S3Layout.Parent("a/b/c"));
    Assert.Equal("", S3Layout.Parent("a"));
    Assert.Equal("a/b/child", S3Layout.Child("a/b", "child"));
    Assert.Equal("a/b/renamed", S3Layout.Sibling("a/b/file.txt", "renamed", false));
    Assert.Equal("a/b/renamed/", S3Layout.Sibling("a/b/folder/", "renamed", true));
    Assert.Equal("notes-copy.txt", S3Layout.CopyName("notes.txt", false));
    Assert.Equal("folder-copy", S3Layout.CopyName("folder", true));
    Assert.True(S3Layout.ContainsPrefix("backups/vol/", "backups/vol/child/"));
    Assert.False(S3Layout.ContainsPrefix("backups/vol/", "backups/other/"));
    Assert.Equal("a/b/file.txt", S3Layout.UploadKey("a/b", "file.txt"));
    Assert.True(S3Layout.PartSize(64L * 1024 * 1024 * 10000 + 1) > 64L * 1024 * 1024);
  }

  [Fact]
  public void Bucket_list_keeps_the_backup_bucket_and_sorts_names() {
    var listed = S3Layout.Buckets([
      new S3Bucket("media", null),
      new S3Bucket("media", null),
      new S3Bucket("", null),
      new S3Bucket("logs", null)
    ], "backup");

    Assert.Equal(["backup", "logs", "media"], listed.Select(bucket => bucket.Name));

    var present = S3Layout.Buckets([
      new S3Bucket("backup", null),
      new S3Bucket("logs", null)
    ], "backup");

    Assert.Equal(["backup", "logs"], present.Select(bucket => bucket.Name));
  }

  [Fact]
  public void Backup_buckets_are_marked_and_other_buckets_stay_open() {
    var marked = S3Layout.MarkBackups([
      new S3Bucket("backup", null),
      new S3Bucket("media", null),
      new S3Bucket("offsite", null)
    ], ["backup", "offsite", ""]);

    Assert.Equal(["backup", "media", "offsite"], marked.Select(bucket => bucket.Name));
    Assert.True(marked[0].Backup);
    Assert.False(marked[1].Backup);
    Assert.True(marked[2].Backup);
    Assert.Equal("backup · Longhorn", marked[0].Caption);
    Assert.Equal("media", marked[1].Caption);
  }

  [Fact]
  public void Listing_keeps_folders_first_and_skips_the_current_prefix() {
    var entries = S3Layout.List("backups/", ["backups/vol/", "backups/vol/"], [
      new S3ObjectInfo("backups/", 0, null),
      new S3ObjectInfo("backups/vol/", 0, null),
      new S3ObjectInfo("backups/readme.txt", 12, null),
      new S3ObjectInfo("backups/nested/skip.txt", 4, null)
    ]);

    Assert.Equal(["vol", "readme.txt"], entries.Select(entry => entry.Name));
    Assert.True(entries[0].IsDirectory);
    Assert.Equal("12B", entries[1].SizeText);
  }

  [Fact]
  public void Secret_document_round_trips_the_longhorn_keys() {
    var target = Sample();
    var secret = LonghornS3.SecretDocument(target);
    var backup = LonghornS3.TargetDocument(target);

    Assert.Equal("longhorn-backup-target", secret["metadata"]?["name"]?.ToString());
    Assert.Equal("access", LonghornS3.Decode(secret, "AWS_ACCESS_KEY_ID"));
    Assert.Equal("secret", LonghornS3.Decode(secret, "AWS_SECRET_ACCESS_KEY"));
    Assert.Equal("http://s3.example:9000", LonghornS3.Decode(secret, "AWS_ENDPOINTS"));
    Assert.Equal("false", LonghornS3.Decode(secret, "VIRTUAL_HOSTED_STYLE"));
    Assert.Equal("s3://backup@us-east-1/longhorn", backup["spec"]?["backupTargetURL"]?.ToString());
    Assert.Equal("longhorn-backup-target-offsite", LonghornS3.SecretNameFor("offsite"));

    var read = LonghornS3.Read(backup, secret);

    Assert.NotNull(read);
    Assert.Equal("default", read.Name);
    Assert.Equal("backup", read.Bucket);
    Assert.Equal("longhorn", read.Prefix);
    Assert.True(read.PathStyle);
    Assert.Equal("access", read.AccessKey);
    Assert.Equal("secret", read.SecretKey);
  }

  [Fact]
  public async Task Load_reads_s3_targets_and_skips_other_urls() {
    var session = new FakeClusterSession();
    session.SetRef(LonghornS3.TargetRef, Backup("default", "s3://backup@us-east-1/longhorn", "creds"), Backup("nfs", "nfs://server/share", ""));
    session.Gotten = Secret("creds");

    var loaded = await LonghornS3.LoadAsync(session, TestContext.Current.CancellationToken);

    Assert.True(loaded.IsSuccess);
    var target = Assert.Single(loaded.Value!.Targets);
    Assert.Equal("default", target.Name);
    Assert.Equal("access", target.AccessKey);
    Assert.Equal(1, loaded.Value.OtherTargets);
  }

  [Fact]
  public async Task Save_writes_the_secret_and_then_the_backup_target() {
    var session = new FakeClusterSession();
    var saved = await LonghornS3.SaveAsync(session, Sample(), TestContext.Current.CancellationToken);

    Assert.True(saved.IsSuccess);
    Assert.Equal(2, session.Applies);
    Assert.Equal("BackupTarget", session.Applied?["kind"]?.ToString());
    Assert.Equal(LonghornS3.TargetRef, session.AppliedRef);
  }

  [Fact]
  public async Task Delete_removes_the_backup_target() {
    var session = new FakeClusterSession();
    var removed = await LonghornS3.DeleteAsync(session, Sample(), TestContext.Current.CancellationToken);

    Assert.True(removed.IsSuccess);
    Assert.Equal(1, session.Deletes);
  }

  private static S3Target Sample() =>
    new(
      "default",
      "longhorn-system",
      "backup",
      "us-east-1",
      "longhorn",
      "http://s3.example:9000",
      "access",
      "secret",
      true,
      "",
      "longhorn-backup-target",
      LonghornS3.DefaultPoll);

  private static JsonObject Backup(string name, string url, string secret) =>
    new() {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = "longhorn-system"
      },
      ["spec"] = new JsonObject {
        ["backupTargetURL"] = url,
        ["credentialSecret"] = secret,
        ["pollInterval"] = "5m0s"
      }
    };

  private static JsonObject Secret(string name) =>
    new() {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = "longhorn-system"
      },
      ["data"] = new JsonObject {
        ["AWS_ACCESS_KEY_ID"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("access")),
        ["AWS_SECRET_ACCESS_KEY"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("secret")),
        ["AWS_ENDPOINTS"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("http://s3.example:9000")),
        ["VIRTUAL_HOSTED_STYLE"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("false"))
      }
    };
}
