using System.Text;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Tests;

public class ClusterWorkspaceTests {
  [Fact]
  public async Task Connect_adds_custom_resources_and_disconnect_drops_them() {
    var session = new FakeClusterSession { Crds = [WidgetCrd()] };
    var workspace = new ClusterWorkspace();
    var connected = await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    Assert.True(connected.IsSuccess);
    Assert.Same(session, workspace.Session);
    Assert.Contains(workspace.Navigator, item => item.Id == "crd:example.com/v1/widgets" && item.Title == "Widget");
    Assert.Equal("Widget", workspace.FindByGvk("example.com/v1", "Widget")!.Kind);
    Assert.Null(workspace.FindByGvk("example.com/v1", ""));

    var replacement = new FakeClusterSession { CrdError = "forbidden" };
    var again = await workspace.ConnectAsync(replacement, TestContext.Current.CancellationToken);
    Assert.True(again.IsSuccess);
    Assert.True(session.Disposed);
    Assert.DoesNotContain(workspace.Navigator, item => item.Id.StartsWith("crd:", StringComparison.Ordinal));

    workspace.Disconnect();
    Assert.True(replacement.Disposed);
    Assert.Null(workspace.Session);
  }

  [Fact]
  public async Task List_requires_a_session_and_rejects_unknown_ids() {
    var workspace = new ClusterWorkspace();
    var missing = await workspace.ListAsync("pods", "default", null, TestContext.Current.CancellationToken);
    Assert.False(missing.IsSuccess);
    Assert.Contains("not connected", missing.Messages[0], StringComparison.OrdinalIgnoreCase);

    var session = new FakeClusterSession();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var unknown = await workspace.ListAsync("not-a-kind", null, null, TestContext.Current.CancellationToken);
    Assert.False(unknown.IsSuccess);
    Assert.Contains("unknown resource", unknown.Messages[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task List_pods_attaches_metrics_and_keeps_the_resource_version() {
    var session = new FakeClusterSession();
    session.Set("pods", Pod("web", "apps", "Running"));
    session.PodMetrics["apps/web"] = new ResourceMetrics("web", "apps", "100m", "512Mi");
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync("pods", "apps", "web", TestContext.Current.CancellationToken);
    Assert.True(listed.IsSuccess, string.Join("; ", listed.Messages));
    var row = Assert.Single(listed.Value!);
    Assert.Equal("100m", row.Cells["CPU"]);
    Assert.Equal("512.0MiB", row.Cells["Memory"]);
    Assert.Equal("Running", row.Cells["Status"]);
    Assert.Equal("rv-1", workspace.LastResourceVersion);

    var filtered = await workspace.ListAsync("pods", "apps", "missing", TestContext.Current.CancellationToken);
    Assert.Empty(filtered.Value!);
  }

  [Fact]
  public async Task List_pods_keeps_rows_when_metrics_are_unavailable() {
    var session = new FakeClusterSession { PodMetricsFail = true };
    session.Set("pods", Pod("web", "apps", "Running"));
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync("pods", null, null, TestContext.Current.CancellationToken);
    Assert.True(listed.IsSuccess, string.Join("; ", listed.Messages));
    Assert.Equal("", Assert.Single(listed.Value!).Cells["CPU"]);
  }

  [Fact]
  public async Task List_propagates_an_api_failure() {
    var session = new FakeClusterSession();
    session.FailList("pods", "forbidden");
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync("pods", "apps", null, TestContext.Current.CancellationToken);
    Assert.False(listed.IsSuccess);
    Assert.Contains("forbidden", listed.Messages[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task List_deployments_sums_pod_metrics_for_the_owner() {
    var session = new FakeClusterSession();
    session.Set("deployments", Deployment("web", "apps"));
    session.Set(
      "pods",
      Pod("web-0", "apps", "Running", """{ "app": "web" }"""),
      Pod("done", "apps", "Succeeded", """{ "app": "web" }"""));
    session.PodMetrics["apps/web-0"] = new ResourceMetrics("web-0", "apps", "250m", "1Gi");
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync("deployments", "apps", null, TestContext.Current.CancellationToken);
    Assert.True(listed.IsSuccess, string.Join("; ", listed.Messages));
    var row = Assert.Single(listed.Value!);
    Assert.Equal("250m", row.Cells["CPU"]);
    Assert.Equal("1.0GiB", row.Cells["Memory"]);
  }

  [Fact]
  public async Task List_applications_collapses_manifests_and_caches_allocatable_cpu() {
    var session = new FakeClusterSession { CpuAllocatable = 4 };
    session.Set("deployments", Deployment("web", "apps", manifest: true));
    session.Set("pods", Pod("web-0", "apps", "Running", """{ "app": "web" }"""));
    session.PodMetrics["apps/web-0"] = new ResourceMetrics("web-0", "apps", "200m", "256Mi");
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync(ResourceCatalog.ApplicationsId, "apps", null, TestContext.Current.CancellationToken);
    Assert.True(listed.IsSuccess, string.Join("; ", listed.Messages));
    var row = Assert.Single(listed.Value!);
    Assert.Equal("web", row.Cell("Instance"));
    Assert.Equal("apps", row.Namespace);
    Assert.Equal(1, session.CpuCalls);

    var again = await workspace.ListAsync(ResourceCatalog.ApplicationsId, "apps", "no-such", TestContext.Current.CancellationToken);
    Assert.Empty(again.Value!);
    Assert.Equal(1, session.CpuCalls);
  }

  [Fact]
  public async Task List_applications_returns_the_first_workload_failure() {
    var session = new FakeClusterSession();
    session.FailList("deployments", "deployments forbidden");
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync(ResourceCatalog.ApplicationsId, null, null, TestContext.Current.CancellationToken);
    Assert.False(listed.IsSuccess);
    Assert.Contains("deployments forbidden", listed.Messages[0], StringComparison.Ordinal);
  }

  [Fact]
  public async Task List_helm_keeps_the_latest_revision_and_groups_charts() {
    var session = new FakeClusterSession {
      HelmDocuments = [
        Helm("vault", "security", 1, "superseded"),
        Helm("vault", "security", 3, "deployed"),
        Helm("vault", "apps", 2, "failed")
      ]
    };
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var releases = await workspace.ListAsync(ResourceCatalog.HelmReleasesId, null, null, TestContext.Current.CancellationToken);
    Assert.True(releases.IsSuccess, string.Join("; ", releases.Messages));
    Assert.Equal(2, releases.Value!.Count);
    Assert.Equal("3", releases.Value.Single(row => row.Namespace == "security").Cells["Revision"]);
    Assert.Equal("deployed", releases.Value.Single(row => row.Namespace == "security").Cells["Status"]);

    var charts = await workspace.ListAsync(ResourceCatalog.HelmChartsId, null, "vault", TestContext.Current.CancellationToken);
    var chart = Assert.Single(charts.Value!);
    Assert.Equal("vault", chart.Cells["Chart"]);
    Assert.Equal("2", chart.Cells["Releases"]);

    var history = await workspace.HelmHistoryAsync("vault", "security", TestContext.Current.CancellationToken);
    Assert.Equal(2, history.Value!.Count);
  }

  [Fact]
  public async Task Helm_history_reports_a_disconnected_session_and_list_errors() {
    var workspace = new ClusterWorkspace();
    var disconnected = await workspace.HelmHistoryAsync("vault", "security", TestContext.Current.CancellationToken);
    Assert.False(disconnected.IsSuccess);

    var session = new FakeClusterSession { HelmError = "secrets forbidden" };
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var failed = await workspace.ListAsync(ResourceCatalog.HelmReleasesId, null, null, TestContext.Current.CancellationToken);
    Assert.False(failed.IsSuccess);
    Assert.Contains("secrets forbidden", failed.Messages[0], StringComparison.Ordinal);
  }

  [Fact]
  public async Task List_dapr_sidecars_and_control_plane() {
    var session = new FakeClusterSession();
    session.Set(
      "pods",
      DaprPod("orders", "apps", """{ "dapr.io/enabled": "true" }""", false),
      DaprPod("plain", "apps", "{}", false),
      DaprPod("operator", "dapr", "{}", true),
      DaprPod("placement", "dapr", "{}", false));
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var sidecars = await workspace.ListAsync(ResourceCatalog.DaprSidecarsId, null, null, TestContext.Current.CancellationToken);
    Assert.True(sidecars.IsSuccess, string.Join("; ", sidecars.Messages));
    Assert.Equal(["operator", "orders"], sidecars.Value!.Select(row => row.Name).Order(StringComparer.Ordinal).ToArray());

    var plane = await workspace.ListAsync(ResourceCatalog.DaprControlPlaneId, null, "operator", TestContext.Current.CancellationToken);
    Assert.Equal("operator", Assert.Single(plane.Value!).Name);
    Assert.Equal("dapr", session.LastListNamespace);
  }

  [Fact]
  public async Task List_definitions_uses_the_crd_endpoint() {
    var session = new FakeClusterSession { Crds = [WidgetCrd()] };
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var listed = await workspace.ListAsync("customresourcedefinitions", null, "Widget", TestContext.Current.CancellationToken);
    Assert.Equal("Widget", Assert.Single(listed.Value!).Cells["Resource"]);
  }

  [Fact]
  public async Task Apply_strips_status_and_resolves_the_builtin_ref() {
    var workspace = new ClusterWorkspace();
    var disconnected = await workspace.ApplyDocumentAsync(Doc("""{ "kind": "ConfigMap" }"""), TestContext.Current.CancellationToken);
    Assert.False(disconnected.IsSuccess);

    var session = new FakeClusterSession();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var applied = await workspace.ApplyDocumentAsync(Doc("""
      {
        "apiVersion": "v1",
        "kind": "ConfigMap",
        "metadata": { "name": "app", "namespace": "default", "generation": 4, "resourceVersion": "9" },
        "data": { "key": "value" },
        "status": { "phase": "Ready" }
      }
      """), TestContext.Current.CancellationToken);

    Assert.True(applied.IsSuccess, string.Join("; ", applied.Messages));
    Assert.Null(session.Applied!["status"]);
    Assert.Null(session.Applied["metadata"]?["generation"]);
    Assert.Equal("ConfigMap", session.AppliedRef!.Kind);
    Assert.Equal("configmaps", session.AppliedRef.Plural);
  }

  [Fact]
  public async Task Related_pods_follow_the_owner_selector() {
    var session = new FakeClusterSession();
    session.Set(
      "pods",
      Pod("web-0", "apps", "Running", """{ "app": "web" }"""),
      Pod("other", "apps", "Running", """{ "app": "db" }"""));
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var owner = ResourceRow.From(Deployment("web", "apps"), ResourceCatalog.Find("deployments")!);

    var related = await workspace.RelatedPodsAsync(owner, TestContext.Current.CancellationToken);
    Assert.Equal("web-0", Assert.Single(related.Value!).Name);

    session.FailList("pods", "pods forbidden");
    var failed = await workspace.RelatedPodsAsync(owner, TestContext.Current.CancellationToken);
    Assert.False(failed.IsSuccess);
  }

  [Fact]
  public async Task Events_match_the_object_and_declared_workloads() {
    var session = new FakeClusterSession();
    session.Set("events", Event("web"), Event("worker"), Event("other"));
    var workspace = new ClusterWorkspace();
    var disconnected = await workspace.EventsForAsync(Row("web"), TestContext.Current.CancellationToken);
    Assert.False(disconnected.IsSuccess);

    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var direct = await workspace.EventsForAsync(Row("web"), TestContext.Current.CancellationToken);
    Assert.Equal("ev-web", Assert.Single(direct.Value!).Name);

    var app = ResourceRow.From(Doc("""
      {
        "metadata": { "name": "store", "namespace": "apps", "uid": "store" },
        "spec": { "workloads": [{ "kind": "Deployment", "name": "worker" }] }
      }
      """), ResourceCatalog.ApplicationsDescriptor);
    var declared = await workspace.EventsForAsync(app, TestContext.Current.CancellationToken);
    Assert.Equal("ev-worker", Assert.Single(declared.Value!).Name);
  }

  [Fact]
  public async Task Cluster_issues_and_drain_preview_use_the_shared_lists() {
    var workspace = new ClusterWorkspace();
    Assert.False((await workspace.GetClusterIssuesAsync(TestContext.Current.CancellationToken)).IsSuccess);
    Assert.False((await workspace.PreviewDrainAsync(["node-a"], TestContext.Current.CancellationToken)).IsSuccess);

    var session = new FakeClusterSession();
    session.Set("nodes", Doc("""
      {
        "metadata": { "name": "n1", "uid": "n1", "creationTimestamp": "2026-08-19T10:00:00Z" },
        "status": { "conditions": [
          { "type": "Ready", "status": "True" },
          { "type": "MemoryPressure", "status": "True", "message": "kubelet has memory pressure" }
        ] }
      }
      """));
    session.Set("pods", Pod("api-0", "apps", "Running", "{}", "ReplicaSet", "node-a"));
    session.FailList("events", "events forbidden");
    var workspaceConnected = new ClusterWorkspace();
    await workspaceConnected.ConnectAsync(session, TestContext.Current.CancellationToken);

    var issues = await workspaceConnected.GetClusterIssuesAsync(TestContext.Current.CancellationToken);
    Assert.True(issues.IsSuccess, string.Join("; ", issues.Messages));
    Assert.Contains(issues.Value!.Warnings, issue => issue.Message.Contains("memory pressure", StringComparison.OrdinalIgnoreCase));

    session.FailList("nodes", "nodes forbidden");
    var bothFailed = await workspaceConnected.GetClusterIssuesAsync(TestContext.Current.CancellationToken);
    Assert.False(bothFailed.IsSuccess);

    var drainSession = new FakeClusterSession();
    drainSession.Set("pods", Pod("api-0", "apps", "Running", "{}", "ReplicaSet", "node-a"));
    drainSession.Set("poddisruptionbudgets");
    var disconnectedDrain = new ClusterWorkspace();
    var preview = await disconnectedDrain.PreviewDrainAsync(["node-a"], TestContext.Current.CancellationToken);
    Assert.False(preview.IsSuccess);

    var connected = new ClusterWorkspace();
    await connected.ConnectAsync(drainSession, TestContext.Current.CancellationToken);
    preview = await connected.PreviewDrainAsync(["node-a", "node-a"], TestContext.Current.CancellationToken);
    Assert.True(preview.IsSuccess, string.Join("; ", preview.Messages));
    var plan = Assert.Single(preview.Value!.Nodes);
    Assert.Equal(DrainPlan.Evict, Assert.Single(plan.Pods).Action);
  }

  [Fact]
  public async Task Volume_files_list_read_and_write_through_the_mounted_pod() {
    var session = new FakeClusterSession {
      ExecText = "root\n0\n0\n",
      ExecBytes = new ExecBytesResult("conf\n"u8.ToArray(), """{"status":"Success"}""")
    };
    session.Set("pods", VolumePod("pg-0", "Running"));
    var workspace = new ClusterWorkspace();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);

    var mounts = await workspace.ListVolumeMountsAsync(Pvc(), TestContext.Current.CancellationToken);
    var mount = Assert.Single(mounts.Value!);
    Assert.Equal("/var/lib/postgresql/data/pgdata", mount.Root);

    var identity = await workspace.GetVolumeIdentityAsync(mount, TestContext.Current.CancellationToken);
    Assert.Equal("root (0:0)", identity.Value);

    var entries = await workspace.ListVolumeEntriesAsync(mount, "pgdata", TestContext.Current.CancellationToken);
    Assert.Equal("conf", Assert.Single(entries.Value!).Name);

    session.ExecBytes = new ExecBytesResult("listen = 1\n"u8.ToArray(), "");
    var file = await workspace.ReadVolumeFileAsync(mount, "pgdata/postgresql.conf", TestContext.Current.CancellationToken);
    Assert.Equal("listen = 1\n", Encoding.UTF8.GetString(file.Value!));

    var written = await workspace.WriteVolumeFileAsync(mount, "pgdata/postgresql.conf", "x"u8.ToArray(), TestContext.Current.CancellationToken);
    Assert.True(written.IsSuccess, string.Join("; ", written.Messages));
    Assert.Equal("x"u8.ToArray(), session.LastStdin);
  }

  [Fact]
  public async Task Volume_files_explain_missing_mounts_shells_and_escapes() {
    var workspace = new ClusterWorkspace();
    var disconnected = await workspace.ListVolumeMountsAsync(Pvc(), TestContext.Current.CancellationToken);
    Assert.False(disconnected.IsSuccess);

    var session = new FakeClusterSession();
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var unbound = await workspace.ListVolumeMountsAsync(Doc("""{ "kind": "PersistentVolume", "metadata": { "name": "pv" } }"""), TestContext.Current.CancellationToken);
    Assert.Contains("not bound", unbound.Messages[0], StringComparison.OrdinalIgnoreCase);

    session.Set("pods", VolumePod("pg-0", "Pending"));
    var pending = await workspace.ListVolumeMountsAsync(Pvc(), TestContext.Current.CancellationToken);
    Assert.Contains("not Running", pending.Messages[0], StringComparison.Ordinal);

    session.Set("pods");
    var none = await workspace.ListVolumeMountsAsync(Pvc(), TestContext.Current.CancellationToken);
    Assert.Contains("No running pod", none.Messages[0], StringComparison.Ordinal);

    session.Set("pods", VolumePod("pg-0", "Running"));
    var mounts = await workspace.ListVolumeMountsAsync(Pvc(), TestContext.Current.CancellationToken);
    var mount = mounts.Value![0];
    var commands = session.Commands.Count;
    var escape = await workspace.ReadVolumeFileAsync(mount, "../etc/passwd", TestContext.Current.CancellationToken);
    Assert.False(escape.IsSuccess);
    Assert.Equal(commands, session.Commands.Count);

    session.ExecBytes = new ExecBytesResult([], "cat: not found");
    var stderr = await workspace.ListVolumeEntriesAsync(mount, "", TestContext.Current.CancellationToken);
    Assert.False(stderr.IsSuccess);
    Assert.Contains("not found", stderr.Messages[0], StringComparison.Ordinal);

    session.ExecError = "executable file not found in $PATH";
    var identity = await workspace.GetVolumeIdentityAsync(mount, TestContext.Current.CancellationToken);
    Assert.Contains("no shell", identity.Messages[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Node_images_classify_cached_images_and_survive_a_pod_list_error() {
    var workspace = new ClusterWorkspace();
    var node = Doc("""
      {
        "metadata": { "name": "node-a" },
        "status": { "images": [{ "names": ["nginx:1.27"], "sizeBytes": 1000 }] }
      }
      """);
    Assert.False((await workspace.NodeImagesAsync(node, TestContext.Current.CancellationToken)).IsSuccess);

    var session = new FakeClusterSession();
    session.Set("pods", Pod("web", "default", "Running"));
    await workspace.ConnectAsync(session, TestContext.Current.CancellationToken);
    var report = await workspace.NodeImagesAsync(node, TestContext.Current.CancellationToken);
    Assert.True(report.IsSuccess, string.Join("; ", report.Messages));
    Assert.Equal("spec.nodeName=node-a", session.LastOptions!.FieldSelector);
    var image = Assert.Single(report.Value!.Images);
    Assert.Equal(NodeImages.Used, image.State);
    Assert.Equal("default/web", image.Pods);

    session.FailList("pods", "pods is forbidden");
    report = await workspace.NodeImagesAsync(node, TestContext.Current.CancellationToken);
    Assert.Equal("—", report.Value!.Images[0].State);
    Assert.Contains("pods is forbidden", report.Value.Caption, StringComparison.Ordinal);

    var nameless = await workspace.NodeImagesAsync(Doc("""{ "metadata": {} }"""), TestContext.Current.CancellationToken);
    Assert.False(nameless.IsSuccess);
  }

  private static ResourceRow Row(string name) =>
    ResourceRow.From(Doc($$"""
      { "metadata": { "name": "{{name}}", "namespace": "apps", "uid": "{{name}}" } }
      """), ResourceCatalog.Find("configmaps")!);

  private static JsonObject Pvc() =>
    Doc("""
      { "kind": "PersistentVolumeClaim", "metadata": { "name": "pg-data", "namespace": "postgresql" } }
      """);

  private static JsonObject VolumePod(string name, string phase) =>
    Doc($$"""
      {
        "metadata": { "name": "{{name}}", "namespace": "postgresql" },
        "spec": {
          "volumes": [{ "name": "data", "persistentVolumeClaim": { "claimName": "pg-data" } }],
          "containers": [{
            "name": "postgres",
            "volumeMounts": [{ "name": "data", "mountPath": "/var/lib/postgresql/data", "subPath": "pgdata" }]
          }]
        },
        "status": { "phase": "{{phase}}" }
      }
      """);

  private static JsonObject WidgetCrd() =>
    Doc("""
      {
        "metadata": { "name": "widgets.example.com" },
        "spec": {
          "group": "example.com",
          "scope": "Namespaced",
          "names": { "kind": "Widget", "plural": "widgets" },
          "versions": [{ "name": "v1", "storage": true, "served": true }]
        }
      }
      """);

  private static JsonObject Deployment(string name, string ns, bool manifest = false) {
    var labels = manifest
      ? """{ "app": "web", "app.kubernetes.io/name": "web", "app.kubernetes.io/instance": "web" }"""
      : """{ "app": "web" }""";

    return Doc($$"""
      {
        "apiVersion": "apps/v1",
        "kind": "Deployment",
        "metadata": { "name": "{{name}}", "namespace": "{{ns}}", "uid": "{{name}}", "labels": {{labels}} },
        "spec": {
          "replicas": 1,
          "selector": { "matchLabels": { "app": "web" } }
        },
        "status": { "replicas": 1, "readyReplicas": 1 }
      }
      """);
  }

  private static JsonObject Pod(
    string name,
    string ns,
    string phase,
    string labels = """{ "app": "web" }""",
    string? ownerKind = null,
    string? node = null) {
    var metadata = new JsonObject {
      ["name"] = name,
      ["namespace"] = ns,
      ["uid"] = name,
      ["labels"] = JsonNode.Parse(labels) as JsonObject
    };

    if (ownerKind is not null)
      metadata["ownerReferences"] = new JsonArray {
        new JsonObject { ["kind"] = ownerKind, ["name"] = "owner", ["controller"] = true }
      };

    var spec = new JsonObject {
      ["containers"] = new JsonArray {
        new JsonObject { ["name"] = "app", ["image"] = "nginx:1.27" }
      }
    };

    if (node is not null)
      spec["nodeName"] = node;

    return new JsonObject {
      ["metadata"] = metadata,
      ["spec"] = spec,
      ["status"] = JsonNode.Parse($$"""
        {
          "phase": "{{phase}}",
          "conditions": [{ "type": "Ready", "status": "True" }],
          "containerStatuses": [{ "name": "app", "ready": true, "restartCount": 0, "state": { "running": {} } }]
        }
        """)
    };
  }

  private static JsonObject DaprPod(string name, string ns, string annotations, bool sidecar) {
    var containers = sidecar
      ? """[{ "name": "daprd" }, { "name": "app" }]"""
      : """[{ "name": "app" }]""";

    return Doc($$"""
      {
        "metadata": { "name": "{{name}}", "namespace": "{{ns}}", "uid": "{{name}}", "annotations": {{annotations}} },
        "spec": { "containers": {{containers}} },
        "status": { "phase": "Running" }
      }
      """);
  }

  private static JsonObject Helm(string name, string ns, int revision, string status) =>
    Doc($$"""
      {
        "name": "{{name}}",
        "namespace": "{{ns}}",
        "version": {{revision}},
        "info": { "status": "{{status}}", "last_deployed": "2026-08-19T12:00:00Z" },
        "chart": { "metadata": { "name": "vault", "version": "1.2.3", "appVersion": "2.0.0" } }
      }
      """);

  private static JsonObject Event(string name) =>
    Doc($$"""
      {
        "metadata": { "name": "ev-{{name}}", "namespace": "apps", "uid": "ev-{{name}}" },
        "involvedObject": { "name": "{{name}}", "kind": "ConfigMap" },
        "type": "Warning",
        "reason": "Failed",
        "message": "boom"
      }
      """);

  private static JsonObject Doc(string json) =>
    JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("expected object");
}
