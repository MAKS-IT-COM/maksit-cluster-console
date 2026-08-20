using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ServicePortForwardTests {
  [Fact]
  public void ResourceOwnership_matches_service_selector_not_matchLabels() {
    var service = Service("""{ "app": "web" }""");
    var matching = Pod("web-1", """{ "app": "web", "pod-template-hash": "abc" }""");
    var other = Pod("db-1", """{ "app": "db" }""");

    Assert.True(ResourceOwnership.Owns(matching, service));
    Assert.False(ResourceOwnership.Owns(other, service));
    Assert.NotNull(ResourceOwnership.SelectorLabels(service));
    Assert.Equal("web", ResourceOwnership.SelectorLabels(service)!["app"]?.GetValue<string>());
  }

  [Fact]
  public void ResourceOwnership_still_matches_workload_matchLabels() {
    var deployment = JsonNode.Parse("""
      {
        "kind": "Deployment",
        "metadata": { "name": "web" },
        "spec": { "selector": { "matchLabels": { "app": "web" } } }
      }
      """) as JsonObject;
    var pod = Pod("web-1", """{ "app": "web" }""");

    Assert.NotNull(deployment);
    Assert.True(ResourceOwnership.Owns(pod, deployment));
  }

  [Fact]
  public void ResourceOwnership_service_selector_does_not_match_same_helm_instance() {
    var service = JsonNode.Parse("""
      {
        "kind": "Service",
        "metadata": {
          "name": "longhorn-frontend",
          "namespace": "longhorn-system",
          "labels": {
            "app": "longhorn-ui",
            "app.kubernetes.io/instance": "longhorn",
            "app.kubernetes.io/name": "longhorn"
          }
        },
        "spec": {
          "selector": { "app": "longhorn-ui" },
          "ports": [{ "name": "http", "port": 80, "targetPort": "http" }]
        }
      }
      """) as JsonObject;
    var ui = Pod(
      "longhorn-ui-1",
      """{ "app": "longhorn-ui", "app.kubernetes.io/instance": "longhorn", "app.kubernetes.io/name": "longhorn" }""");
    var csi = Pod(
      "csi-attacher-1",
      """{ "app": "csi-attacher", "app.kubernetes.io/instance": "longhorn", "app.kubernetes.io/name": "longhorn" }""");

    Assert.NotNull(service);
    Assert.True(ResourceOwnership.Owns(ui, service));
    Assert.False(ResourceOwnership.Owns(csi, service));
  }

  [Fact]
  public void Resolve_skips_pods_that_lack_the_named_targetPort() {
    var service = JsonNode.Parse("""
      {
        "kind": "Service",
        "metadata": { "name": "longhorn-frontend", "namespace": "longhorn-system" },
        "spec": {
          "selector": { "app": "longhorn-ui" },
          "ports": [{ "name": "http", "port": 80, "targetPort": "http" }]
        }
      }
      """) as JsonObject;
    var csi = JsonNode.Parse("""
      {
        "kind": "Pod",
        "metadata": { "name": "csi-attacher-1", "namespace": "longhorn-system", "labels": { "app": "csi-attacher" } },
        "spec": { "containers": [{ "name": "attacher", "ports": [{ "containerPort": 8443 }] }] },
        "status": { "phase": "Running", "conditions": [{ "type": "Ready", "status": "True" }] }
      }
      """) as JsonObject;
    var ui = JsonNode.Parse("""
      {
        "kind": "Pod",
        "metadata": { "name": "longhorn-ui-1", "namespace": "longhorn-system", "labels": { "app": "longhorn-ui" } },
        "spec": { "containers": [{ "name": "longhorn-ui", "ports": [{ "name": "http", "containerPort": 8000 }] }] },
        "status": { "phase": "Running", "conditions": [{ "type": "Ready", "status": "True" }] }
      }
      """) as JsonObject;

    Assert.NotNull(service);
    Assert.NotNull(csi);
    Assert.NotNull(ui);
    var pods = ResourceCatalog.Find("pods")!;
    var resolved = ServicePortForward.Resolve(
      service,
      [ResourceRow.From(csi, pods), ResourceRow.From(ui, pods)],
      null,
      80);

    Assert.True(resolved.IsSuccess);
    Assert.Equal("longhorn-ui-1", resolved.Value!.PodName);
    Assert.Equal(8000, resolved.Value.ContainerPort);
    Assert.Equal(80, resolved.Value.RequestedPort);
  }

  [Fact]
  public void MapPort_translates_service_port_to_numeric_targetPort() {
    var service = Service(
      """{ "app": "web" }""",
      """[{ "port": 80, "targetPort": 8080 }]""");
    var mapped = ServicePortForward.MapPort(service, Pod("web-1", """{ "app": "web" }"""), 80);

    Assert.True(mapped.IsSuccess);
    Assert.Equal(8080, mapped.Value);
  }

  [Fact]
  public void MapPort_resolves_named_targetPort_on_the_pod() {
    var service = Service(
      """{ "app": "web" }""",
      """[{ "port": 80, "targetPort": "http" }]""");
    var pod = JsonNode.Parse("""
      {
        "kind": "Pod",
        "metadata": { "name": "web-1", "namespace": "apps", "labels": { "app": "web" } },
        "spec": {
          "containers": [
            { "name": "app", "ports": [{ "name": "http", "containerPort": 9090 }] }
          ]
        },
        "status": { "phase": "Running", "conditions": [{ "type": "Ready", "status": "True" }] }
      }
      """) as JsonObject;

    Assert.NotNull(pod);
    var mapped = ServicePortForward.MapPort(service, pod, 80);
    Assert.True(mapped.IsSuccess);
    Assert.Equal(9090, mapped.Value);
  }

  [Fact]
  public void MapPort_keeps_unmatched_remote_port_as_container_port() {
    var service = Service(
      """{ "app": "web" }""",
      """[{ "port": 80, "targetPort": 8080 }]""");
    var mapped = ServicePortForward.MapPort(service, Pod("web-1", """{ "app": "web" }"""), 8080);

    Assert.True(mapped.IsSuccess);
    Assert.Equal(8080, mapped.Value);
  }

  [Fact]
  public void Resolve_picks_a_running_pod_and_maps_the_service_port() {
    var service = Service(
      """{ "app": "web" }""",
      """[{ "port": 5432, "targetPort": 5432 }]""");
    var pending = ResourceRow.From(Pod("web-0", """{ "app": "web" }""", "Pending"), ResourceCatalog.Find("pods")!);
    var running = ResourceRow.From(
      Pod("web-1", """{ "app": "web" }""", "Running", ready: true),
      ResourceCatalog.Find("pods")!);

    var resolved = ServicePortForward.Resolve(service, [pending, running], null, 5432);

    Assert.True(resolved.IsSuccess);
    Assert.Equal("web-1", resolved.Value!.PodName);
    Assert.Equal("apps", resolved.Value.Namespace);
    Assert.Equal(5432, resolved.Value.ContainerPort);
  }

  [Fact]
  public void Resolve_rejects_a_service_without_selector() {
    var service = JsonNode.Parse("""
      {
        "kind": "Service",
        "metadata": { "name": "external", "namespace": "apps" },
        "spec": { "type": "ExternalName", "externalName": "db.example.com", "ports": [{ "port": 5432 }] }
      }
      """) as JsonObject;

    Assert.NotNull(service);
    var resolved = ServicePortForward.Resolve(service, [], null, 5432);
    Assert.False(resolved.IsSuccess);
    Assert.Contains("selector", string.Join(' ', resolved.Messages), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void PickPod_skips_a_preferred_pod_that_is_not_running() {
    var pods = ResourceCatalog.Find("pods")!;
    var preferred = ResourceRow.From(Pod("web-old", """{ "app": "web" }""", "Pending"), pods);
    var running = ResourceRow.From(Pod("web-new", """{ "app": "web" }""", "Running", ready: true), pods);

    var picked = ServicePortForward.PickPod([preferred, running], preferred);

    Assert.NotNull(picked);
    Assert.Equal("web-new", picked.Name);
  }

  [Fact]
  public void PickRunning_matches_stable_labels_when_the_replica_name_changes() {
    var pods = ResourceCatalog.Find("pods")!;
    var gone = ResourceRow.From(
      Pod("longhorn-ui-5d6f5b4c44-4p6n5", """{ "app": "longhorn-ui", "pod-template-hash": "5d6f5b4c44" }""", "Pending"),
      pods);
    var next = ResourceRow.From(
      Pod("longhorn-ui-aaaa-bbbb", """{ "app": "longhorn-ui", "pod-template-hash": "aaaa" }""", "Running", ready: true),
      pods);

    var picked = ServicePortForward.PickRunning(
      [gone, next],
      "longhorn-ui-5d6f5b4c44-4p6n5",
      new Dictionary<string, string> { ["app"] = "longhorn-ui" });

    Assert.NotNull(picked);
    Assert.Equal("longhorn-ui-aaaa-bbbb", picked.Name);
  }

  [Fact]
  public void StableLabels_strips_pod_template_hash() {
    var pod = Pod(
      "longhorn-ui-5d6f5b4c44-4p6n5",
      """{ "app": "longhorn-ui", "pod-template-hash": "5d6f5b4c44" }""");

    var labels = ServicePortForward.StableLabels(pod);

    Assert.NotNull(labels);
    Assert.Equal("longhorn-ui", labels["app"]);
    Assert.False(labels.ContainsKey("pod-template-hash"));
  }

  [Fact]
  public void StableLabels_uses_a_service_selector() {
    var service = Service("""{ "app": "longhorn-ui" }""");

    var labels = ServicePortForward.StableLabels(service);

    Assert.NotNull(labels);
    Assert.Equal("longhorn-ui", labels["app"]);
  }

  [Fact]
  public void Services_catalog_can_port_forward_and_lists_related_pods() {
    var descriptor = ResourceCatalog.Find("services")!;
    Assert.True(descriptor.Actions.CanPortForward);
    Assert.Contains("Pods", descriptor.DetailTabs);
    Assert.Equal(5432, ServicePortForward.DefaultPort(Service(
      """{ "app": "web" }""",
      """[{ "port": 5432, "targetPort": 5432 }]""")));
  }

  private static JsonObject Service(string selectorJson, string? portsJson = null) {
    var ports = portsJson ?? """[{ "port": 80, "targetPort": 80 }]""";
    var parsed = JsonNode.Parse($$"""
      {
        "kind": "Service",
        "metadata": { "name": "web", "namespace": "apps" },
        "spec": {
          "selector": {{selectorJson}},
          "ports": {{ports}}
        }
      }
      """) as JsonObject;
    Assert.NotNull(parsed);
    return parsed;
  }

  private static JsonObject Pod(string name, string labelsJson, string phase = "Running", bool ready = false) {
    var readyStatus = ready ? "True" : "False";
    var parsed = JsonNode.Parse($$"""
      {
        "kind": "Pod",
        "metadata": { "name": "{{name}}", "namespace": "apps", "labels": {{labelsJson}} },
        "status": {
          "phase": "{{phase}}",
          "conditions": [{ "type": "Ready", "status": "{{readyStatus}}" }]
        }
      }
      """) as JsonObject;
    Assert.NotNull(parsed);
    return parsed;
  }
}
