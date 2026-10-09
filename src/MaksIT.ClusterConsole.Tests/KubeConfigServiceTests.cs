using MaksIT.ClusterConsole.Client.KubeConfig;


namespace MaksIT.ClusterConsole.Tests;

public class KubeConfigServiceTests {
  [Fact]
  public void ListContexts_reads_fixture() {
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kubeconfig.yaml");
    var service = new KubeConfigService();
    var listed = service.ListContexts(path);

    Assert.True(listed.IsSuccess, string.Join("; ", listed.Messages));
    Assert.Equal(2, listed.Value!.Count);
    Assert.Contains(listed.Value, c => c.Name == "lab");
  }

  [Fact]
  public void GetCurrentContext_reads_fixture() {
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kubeconfig.yaml");
    var service = new KubeConfigService();
    var current = service.GetCurrentContext(path);

    Assert.True(current.IsSuccess);
    Assert.Equal("lab", current.Value);
  }

  [Fact]
  public void ResolvePath_missing_file_returns_null() {
    Assert.Null(KubeConfigService.ResolvePath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
  }

  [Fact]
  public void ListContexts_missing_file_returns_empty() {
    var service = new KubeConfigService();
    var listed = service.ListContexts(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    Assert.True(listed.IsSuccess);
    Assert.Empty(listed.Value!);
  }

  [Fact]
  public void UseContext_switches_current_context() {
    var path = CopyFixture();
    var service = new KubeConfigService();

    var switched = service.UseContext("other", path);
    Assert.True(switched.IsSuccess, string.Join("; ", switched.Messages));
    Assert.Equal("other", service.GetCurrentContext(path).Value);

    var details = service.ListContextDetails(path);
    Assert.True(details.IsSuccess);
    Assert.Contains(details.Value!, d => d.Name == "other" && d.IsCurrent);
    Assert.Contains(details.Value!, d => d.Name == "lab" && !d.IsCurrent);
  }

  [Fact]
  public void UseContext_unknown_returns_not_found() {
    var path = CopyFixture();
    var service = new KubeConfigService();
    var switched = service.UseContext("missing", path);

    Assert.False(switched.IsSuccess);
  }

  [Fact]
  public void UpsertConnection_creates_file_and_uses_context() {
    var path = Path.Combine(Path.GetTempPath(), "maksit-cluster-console-" + Guid.NewGuid().ToString("N"), "config");
    var service = new KubeConfigService();
    var added = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "k3s",
      Server = "https://127.0.0.1:6443",
      AuthKind = KubeAuthKind.Token,
      Token = "secret-token",
      InsecureSkipTlsVerify = true,
      UseAfterAdd = true
    }, path);

    Assert.True(added.IsSuccess, string.Join("; ", added.Messages));
    Assert.True(File.Exists(path));
    Assert.Equal("k3s", service.GetCurrentContext(path).Value);

    var listed = service.ListContextDetails(path);
    Assert.True(listed.IsSuccess);
    var item = Assert.Single(listed.Value!);
    Assert.Equal("k3s", item.Name);
    Assert.Equal("k3s", item.Cluster);
    Assert.Equal("k3s", item.User);
    Assert.Equal("https://127.0.0.1:6443", item.Server);
    Assert.True(item.SkipTlsVerify);
    Assert.Equal("Auth: token present", item.AuthSummary);
    Assert.True(item.IsCurrent);

    var built = service.Build("k3s", path);
    Assert.True(built.IsSuccess, string.Join("; ", built.Messages));
    Assert.Equal("secret-token", built.Value!.AccessToken);
    Assert.True(built.Value.DisableHttp2);
  }

  [Fact]
  public void UpsertConnection_k3sdata_embeds_certificates() {
    var path = CopyFixture();
    var service = new KubeConfigService();
    var ca = Convert.ToBase64String("ca-bytes"u8.ToArray());
    var cert = Convert.ToBase64String("cert-bytes"u8.ToArray());
    var key = Convert.ToBase64String("key-bytes"u8.ToArray());
    var added = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "edge",
      Server = "https://10.0.0.2:6443",
      AuthKind = KubeAuthKind.K3sData,
      CaData = ca,
      ClientCertData = cert,
      ClientKeyData = key,
      UseAfterAdd = false
    }, path);

    Assert.True(added.IsSuccess, string.Join("; ", added.Messages));
    Assert.Equal("lab", service.GetCurrentContext(path).Value);

    var details = service.ListContextDetails(path).Value!.Single(d => d.Name == "edge");
    Assert.Equal("CA data: present", details.CaSummary);
    Assert.Equal("Auth: client certificate + key", details.AuthSummary);
    Assert.False(details.IsCurrent);
  }

  [Fact]
  public void DeleteContext_cleanup_removes_unused_cluster_and_user() {
    var path = CopyFixture();
    var service = new KubeConfigService();
    service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "temp",
      Server = "https://example.test:6443",
      AuthKind = KubeAuthKind.Token,
      Token = "temp-token",
      UseAfterAdd = false
    }, path);

    var deleted = service.DeleteContext("temp", cleanupUnused: true, path);
    Assert.True(deleted.IsSuccess, string.Join("; ", deleted.Messages));

    var listed = service.ListContextDetails(path);
    Assert.DoesNotContain(listed.Value!, d => d.Name == "temp");
    Assert.Equal(2, listed.Value!.Count);
    Assert.Contains(listed.Value, d => d.Name == "lab");
    Assert.Contains(listed.Value, d => d.Name == "other");
  }

  [Fact]
  public void DeleteContext_without_cleanup_keeps_shared_cluster() {
    var path = CopyFixture();
    var service = new KubeConfigService();
    var deleted = service.DeleteContext("other", cleanupUnused: false, path);

    Assert.True(deleted.IsSuccess, string.Join("; ", deleted.Messages));
    var listed = service.ListContexts(path);
    Assert.Single(listed.Value!);
    Assert.Equal("lab", listed.Value![0].Cluster);
  }

  [Fact]
  public void UpsertConnection_rejects_missing_token() {
    var service = new KubeConfigService();
    var added = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "bad",
      Server = "https://127.0.0.1:6443",
      AuthKind = KubeAuthKind.Token
    }, CopyFixture());

    Assert.False(added.IsSuccess);
  }

  [Fact]
  public void UpsertConnection_reuses_existing_cluster_and_user_names() {
    var path = CopyFixture();
    var service = new KubeConfigService();
    var updated = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "lab",
      Server = "https://127.0.0.1:6443",
      AuthKind = KubeAuthKind.Token,
      Token = "rotated-token",
      InsecureSkipTlsVerify = true,
      UseAfterAdd = false
    }, path);

    Assert.True(updated.IsSuccess, string.Join("; ", updated.Messages));
    var details = service.ListContextDetails(path).Value!.Single(d => d.Name == "lab");
    Assert.Equal("lab", details.Cluster);
    Assert.Equal("admin", details.User);

    var config = k8s.KubernetesClientConfiguration.LoadKubeConfig(path);
    Assert.Single(config.Clusters!);
    Assert.Single(config.Users!);
  }

  [Fact]
  public void UseContext_updates_current_context_without_backup() {
    var path = CopyFixture();
    var service = new KubeConfigService();

    var switched = service.UseContext("other", path);
    Assert.True(switched.IsSuccess, string.Join("; ", switched.Messages));
    Assert.Equal("other", service.GetCurrentContext(path).Value);

    Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bak.*"));
  }

  [Fact]
  public void UpsertConnection_does_not_create_sibling_bak_files() {
    var path = CopyFixture();
    var service = new KubeConfigService();
    var added = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "edge",
      Server = "https://10.0.0.2:6443",
      AuthKind = KubeAuthKind.Token,
      Token = "edge-token",
      UseAfterAdd = false
    }, path);

    Assert.True(added.IsSuccess, string.Join("; ", added.Messages));
    Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bak.*"));
  }

  [Fact]
  public void UseContext_rejects_blank_missing_and_already_selected() {
    var service = new KubeConfigService();
    Assert.False(service.UseContext("  ", CopyFixture()).IsSuccess);

    var missing = service.UseContext("lab", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    Assert.False(missing.IsSuccess);
    Assert.Contains("not found", missing.Messages[0], StringComparison.OrdinalIgnoreCase);

    var path = CopyFixture();
    var current = service.UseContext("lab", path);
    Assert.True(current.IsSuccess, string.Join("; ", current.Messages));
    Assert.Contains("Already using", current.Messages[0], StringComparison.Ordinal);
  }

  [Fact]
  public void Delete_and_current_context_report_missing_files_and_names() {
    var service = new KubeConfigService();
    Assert.False(service.DeleteContext("", true, CopyFixture()).IsSuccess);

    var missingFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Assert.False(service.DeleteContext("lab", true, missingFile).IsSuccess);
    Assert.False(service.GetCurrentContext(missingFile).IsSuccess);
    Assert.False(service.DeleteContext("gone", false, CopyFixture()).IsSuccess);

    var path = Path.Combine(Path.GetTempPath(), "maksit-cluster-console-" + Guid.NewGuid().ToString("N") + ".yaml");
    File.WriteAllText(path, """
      apiVersion: v1
      kind: Config
      contexts: []
      """);
    var current = service.GetCurrentContext(path);
    Assert.False(current.IsSuccess);

    var broken = Path.Combine(Path.GetTempPath(), "maksit-cluster-console-" + Guid.NewGuid().ToString("N") + ".yaml");
    File.WriteAllText(broken, ":\n  - [");
    Assert.False(service.ListContexts(broken).IsSuccess);
    Assert.False(service.Build("missing", CopyFixture()).IsSuccess);
  }

  [Fact]
  public void UpsertConnection_validates_each_auth_kind() {
    var service = new KubeConfigService();
    var path = CopyFixture();
    Assert.False(service.UpsertConnection(new KubeConnectionRequest {
      ContextName = " ",
      Server = "https://127.0.0.1:6443"
    }, path).IsSuccess);
    Assert.False(service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "bad",
      Server = " "
    }, path).IsSuccess);
    Assert.False(service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "bad",
      Server = "https://127.0.0.1:6443",
      CaFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    }, path).IsSuccess);
    Assert.False(service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "bad",
      Server = "https://127.0.0.1:6443",
      AuthKind = KubeAuthKind.Cert
    }, path).IsSuccess);
    Assert.False(service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "bad",
      Server = "https://127.0.0.1:6443",
      AuthKind = KubeAuthKind.Basic,
      BasicUser = "admin"
    }, path).IsSuccess);

    var cert = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".crt");
    var key = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".key");
    File.WriteAllText(cert, "cert");
    File.WriteAllText(key, "key");
    var added = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "cert",
      ClusterName = "cert-cluster",
      UserName = "cert-user",
      Namespace = "ops",
      Server = "https://10.0.0.3:6443",
      AuthKind = KubeAuthKind.Cert,
      ClientCertFile = cert,
      ClientKeyFile = key,
      CaFile = cert,
      EmbedClusterCa = true,
      EmbedClientCerts = true,
      UseAfterAdd = false
    }, path);
    Assert.True(added.IsSuccess, string.Join("; ", added.Messages));
    var details = service.ListContextDetails(path).Value!.Single(item => item.Name == "cert");
    Assert.Equal("ops", details.Namespace);
    Assert.Equal("CA data: present", details.CaSummary);

    var basic = service.UpsertConnection(new KubeConnectionRequest {
      ContextName = "basic",
      Server = "https://10.0.0.4:6443",
      AuthKind = KubeAuthKind.Basic,
      BasicUser = "admin",
      BasicPassword = "secret",
      InsecureSkipTlsVerify = true,
      UseAfterAdd = false
    }, path);
    Assert.True(basic.IsSuccess, string.Join("; ", basic.Messages));
    Assert.Contains("admin", service.ListContextDetails(path).Value!.Single(item => item.Name == "basic").AuthSummary, StringComparison.Ordinal);

    Assert.False(KubeAuthKind.TryParse("nope", out _));
    Assert.True(KubeAuthKind.TryParse("TOKEN", out var kind));
    Assert.Equal(KubeAuthKind.Token, kind);
    Assert.Equal(path, KubeConfigService.ResolveWritablePath(path));
  }

  private static string CopyFixture() {
    var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kubeconfig.yaml");
    var path = Path.Combine(Path.GetTempPath(), "maksit-cluster-console-" + Guid.NewGuid().ToString("N") + ".yaml");
    File.Copy(source, path);

    return path;
  }
}
