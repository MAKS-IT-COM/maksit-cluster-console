using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class PodMetricsAggregateTests {
  [Fact]
  public void SumForOwner_aggregates_pods_owned_by_workload() {
    var deployment = JsonNode.Parse("""
      {
        "metadata": { "name": "web", "namespace": "apps", "uid": "1" },
        "spec": { "selector": { "matchLabels": { "app": "web" } } }
      }
      """) as JsonObject;
    var pods = new[] {
      Pod("web-1", "apps", """{ "app": "web" }""", "Running"),
      Pod("other-1", "apps", """{ "app": "other" }""", "Running")
    };
    var metrics = new Dictionary<string, ResourceMetrics> {
      ["apps/web-1"] = new("web-1", "apps", "100m", "256Mi"),
      ["apps/other-1"] = new("other-1", "apps", "500m", "1Gi")
    };

    Assert.NotNull(deployment);
    var usage = PodMetricsAggregate.SumForOwner(deployment, pods, metrics);
    Assert.Equal(0.1, usage.CpuCores, 9);
    Assert.Equal(268_435_456, usage.MemoryBytes);

    var display = PodMetricsAggregate.ToDisplayMetrics(usage, metricsAvailable: true);
    Assert.NotNull(display);
    Assert.Equal("100m", display!.Cpu);
    Assert.Equal("256Mi", display.Memory);
  }

  [Fact]
  public void DeploymentByReplicaSet_maps_controller_replica_sets() {
    var replicaSet = JsonNode.Parse("""
      {
        "metadata": {
          "name": "web-7d4f8b9c6d",
          "namespace": "apps",
          "ownerReferences": [
            { "kind": "Deployment", "name": "web", "controller": true }
          ]
        }
      }
      """) as JsonObject;

    Assert.NotNull(replicaSet);
    var map = PodMetricsAggregate.DeploymentByReplicaSet([replicaSet]);
    Assert.Equal("web", map["apps/web-7d4f8b9c6d"]);
  }

  [Fact]
  public void BelongsToApplication_matches_pods_owned_by_replica_set_deployment() {
    var app = ApplicationManifest.Collapse([
      Deployment("web", instance: "shop", nameLabel: "storefront", ready: 1, replicas: 1)
    ]).Single();
    var map = new Dictionary<string, string> { ["apps/web-abc"] = "web" };
    var pod = JsonNode.Parse("""
      {
        "metadata": {
          "name": "web-pod",
          "namespace": "apps",
          "ownerReferences": [{ "kind": "ReplicaSet", "name": "web-abc" }]
        },
        "status": { "phase": "Running" }
      }
      """) as JsonObject;
    var metrics = new Dictionary<string, ResourceMetrics> {
      ["apps/web-pod"] = new("web-pod", "apps", "50m", "128Mi")
    };

    Assert.NotNull(pod);
    Assert.True(ApplicationManifest.BelongsToApplication(app, pod, map));
    var usage = ApplicationManifest.SumUsage(app, [pod], metrics, map);
    Assert.Equal(0.05, usage.CpuCores, 9);
  }

  [Fact]
  public void MetricTips_show_task_manager_and_k8s_native_values() {
    var tips = ApplicationManifest.MetricTips(new ApplicationUsage(0.5, 536_870_912), metricsAvailable: true);
    Assert.Equal("500m", tips["CPU"]);
    Assert.Equal("512 MiB · 512 MB", tips["Memory"]);
  }

  private static JsonObject Pod(string name, string ns, string labelsJson, string phase) {
    var labels = JsonNode.Parse(labelsJson) as JsonObject;
    return new JsonObject {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = ns,
        ["labels"] = labels
      },
      ["status"] = new JsonObject { ["phase"] = phase }
    };
  }

  private static JsonObject Deployment(
    string name,
    string? instance = null,
    string? nameLabel = null,
    int ready = 0,
    int replicas = 1) {
    var labels = new JsonObject();
    if (instance is not null)
      labels[ApplicationManifest.InstanceKey] = instance;
    if (nameLabel is not null)
      labels[ApplicationManifest.NameKey] = nameLabel;

    return new JsonObject {
      ["kind"] = "Deployment",
      ["apiVersion"] = "apps/v1",
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = "apps",
        ["uid"] = name,
        ["labels"] = labels
      },
      ["spec"] = new JsonObject {
        ["replicas"] = replicas,
        ["selector"] = new JsonObject { ["matchLabels"] = new JsonObject { ["app"] = nameLabel ?? name } }
      },
      ["status"] = new JsonObject { ["readyReplicas"] = ready }
    };
  }
}
