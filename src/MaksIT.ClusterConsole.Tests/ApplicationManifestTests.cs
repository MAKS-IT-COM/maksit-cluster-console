using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ApplicationManifestTests {
  [Fact]
  public void HasManifest_requires_recommended_name_or_instance_label() {
    var labeled = Deployment("web", instance: "shop", nameLabel: "storefront");
    var unlabeled = Deployment("raw");
    Assert.True(ApplicationManifest.HasManifest(labeled));
    Assert.False(ApplicationManifest.HasManifest(unlabeled));
  }

  [Fact]
  public void Cells_keep_metadata_name_and_show_instance_from_labels() {
    var item = Deployment(
      "storefront-api",
      instance: "shop",
      nameLabel: "storefront",
      managedBy: "Helm",
      version: "1.4.2",
      ready: 2,
      replicas: 2);

    Assert.Equal("storefront-api", JsonPath.Name(item));
    var cells = ApplicationManifest.Cells(item);
    Assert.Equal("shop", cells["Instance"]);
    Assert.Equal("apps", cells["Namespace"]);
    Assert.Equal("Helm", cells["Managed by"]);
    Assert.Equal("1.4.2", cells["Version"]);
    Assert.Equal("2/2", cells["Ready"]);
    Assert.Equal("-", cells["CPU"]);
    Assert.Equal("-", cells["Memory"]);
    Assert.Equal("Running", cells["Status"]);
  }

  [Fact]
  public void Cells_show_cpu_percent_and_memory_megabytes_when_metrics_available() {
    var item = Deployment("web", instance: "shop", nameLabel: "storefront", ready: 1, replicas: 1);
    var cells = ApplicationManifest.Cells(
      item,
      new ApplicationUsage(0.5, 536_870_912),
      clusterCpuAllocatable: 4,
      metricsAvailable: true);
    Assert.Equal("12.5%", cells["CPU"]);
    Assert.Equal("512.0MiB", cells["Memory"]);
  }

  [Fact]
  public void SumUsage_aggregates_matching_pods_across_workloads() {
    var app = ApplicationManifest.Collapse([
      Deployment("hubble-ui", instance: "cilium", nameLabel: "hubble-ui", ready: 1, replicas: 1),
      Deployment("hubble-relay", instance: "cilium", nameLabel: "hubble-relay", ready: 1, replicas: 1)
    ]).Single();

    var pods = new[] {
      Pod("hubble-ui-1", "apps", instance: "cilium", nameLabel: "hubble-ui", phase: "Running"),
      Pod("hubble-relay-1", "apps", instance: "cilium", nameLabel: "hubble-relay", phase: "Running"),
      Pod("other-1", "apps", instance: "other", nameLabel: "other", phase: "Running")
    };
    var metrics = new Dictionary<string, ResourceMetrics> {
      ["apps/hubble-ui-1"] = new("hubble-ui-1", "apps", "100m", "256Mi"),
      ["apps/hubble-relay-1"] = new("hubble-relay-1", "apps", "250m", "512Mi")
    };

    var usage = ApplicationManifest.SumUsage(app, pods, metrics);
    Assert.Equal(0.35, usage.CpuCores, 9);
    Assert.Equal(805_306_368, usage.MemoryBytes);
    Assert.Equal("768.0MiB", ApplicationManifest.FormatMemoryUsage(usage.MemoryBytes, metricsAvailable: true));
  }

  [Fact]
  public void BelongsToApplication_matches_instance_labels_or_workload_owner() {
    var app = ApplicationManifest.Collapse([
      Deployment("hubble-ui", instance: "cilium", nameLabel: "hubble-ui", ready: 1, replicas: 1)
    ]).Single();
    var byLabel = Pod("hubble-ui-1", "apps", instance: "cilium", nameLabel: "hubble-ui", phase: "Running");
    var byOwner = JsonNode.Parse("""
      {
        "metadata": {
          "name": "hubble-ui-abc",
          "namespace": "apps",
          "ownerReferences": [{ "name": "hubble-ui", "kind": "DaemonSet" }]
        },
        "status": { "phase": "Running" }
      }
      """) as JsonObject;
    var other = Pod("other-1", "apps", instance: "other", nameLabel: "other", phase: "Running");

    Assert.NotNull(byOwner);
    Assert.True(ApplicationManifest.BelongsToApplication(app, byLabel));
    Assert.True(ApplicationManifest.BelongsToApplication(app, byOwner));
    Assert.False(ApplicationManifest.BelongsToApplication(app, other));
  }

  [Fact]
  public void Labels_on_pod_template_count_as_application_manifest() {
    var item = JsonNode.Parse("""
      {
        "kind": "Deployment",
        "apiVersion": "apps/v1",
        "metadata": { "name": "hubble-ui", "namespace": "kube-system" },
        "spec": {
          "replicas": 1,
          "template": {
            "metadata": {
              "labels": {
                "app.kubernetes.io/name": "hubble-ui",
                "app.kubernetes.io/instance": "cilium"
              }
            }
          }
        },
        "status": { "readyReplicas": 1 }
      }
      """) as JsonObject;

    Assert.NotNull(item);
    Assert.True(ApplicationManifest.HasManifest(item));
    Assert.Equal("cilium", ApplicationManifest.Cells(item)["Instance"]);
  }

  [Fact]
  public void SameInstance_matches_pods_in_the_same_namespace() {
    var deploy = Deployment("web", instance: "shop", nameLabel: "storefront");
    var pod = JsonNode.Parse("""
      {
        "metadata": {
          "name": "web-abc",
          "namespace": "apps",
          "labels": {
            "app.kubernetes.io/name": "storefront",
            "app.kubernetes.io/instance": "shop"
          }
        }
      }
      """) as JsonObject;
    var otherNs = JsonNode.Parse("""
      {
        "metadata": {
          "name": "web-abc",
          "namespace": "other",
          "labels": { "app.kubernetes.io/instance": "shop" }
        }
      }
      """) as JsonObject;

    Assert.NotNull(pod);
    Assert.NotNull(otherNs);
    Assert.True(ApplicationManifest.SameInstance(deploy, pod));
    Assert.False(ApplicationManifest.SameInstance(deploy, otherNs));
  }

  [Fact]
  public void Collapse_groups_helm_components_by_instance_and_namespace() {
    var ui = Deployment("hubble-ui", instance: "cilium", nameLabel: "hubble-ui", ready: 1, replicas: 1);
    var relay = Deployment("hubble-relay", instance: "cilium", nameLabel: "hubble-relay", ready: 1, replicas: 1);
    var operatorDeploy = Deployment("cilium-operator", instance: "cilium", nameLabel: "operator", ready: 0, replicas: 1);
    var other = Deployment("hubble-ui", instance: "cilium", nameLabel: "hubble-ui", ready: 1, replicas: 1);
    other["metadata"]!["namespace"] = "other";

    var first = Deployment("platform", instance: "first", nameLabel: "platform", version: "1", ready: 1, replicas: 1);
    var second = Deployment("platform", instance: "second", nameLabel: "platform", version: "2", ready: 1, replicas: 1);

    var collapsed = ApplicationManifest.Collapse([ui, relay, operatorDeploy, other, first, second, Deployment("raw")]);
    Assert.Equal(4, collapsed.Count);

    var cilium = Assert.Single(collapsed, d => JsonPath.Namespace(d) == "apps" && JsonPath.Name(d) == "cilium");
    Assert.Equal("Application", cilium["kind"]?.GetValue<string>());
    Assert.Equal("2/3", ApplicationManifest.Cells(cilium)["Ready"]);
    Assert.Equal("Progressing", ApplicationManifest.Cells(cilium)["Status"]);
    Assert.Equal(["cilium-operator", "hubble-relay", "hubble-ui"], ApplicationManifest.WorkloadNames(cilium));

    Assert.Contains(collapsed, d => JsonPath.Name(d) == "first");
    Assert.Contains(collapsed, d => JsonPath.Name(d) == "second");
    Assert.Contains(collapsed, d => JsonPath.Namespace(d) == "other" && JsonPath.Name(d) == "cilium");
  }

  [Fact]
  public void WorkloadNames_returns_empty_when_document_is_null() =>
    Assert.Empty(ApplicationManifest.WorkloadNames(null));

  [Fact]
  public void Workloads_tolerate_json_null_kind_and_name() {
    var item = JsonNode.Parse("""
      {
        "spec": {
          "workloads": [
            { "kind": null, "name": "hubble-ui" },
            { "kind": "Deployment", "name": null }
          ]
        }
      }
      """) as JsonObject;

    Assert.NotNull(item);
    Assert.Equal([("Workload", "hubble-ui"), ("Deployment", "")], ApplicationManifest.Workloads(item));
    Assert.Equal(["hubble-ui"], ApplicationManifest.WorkloadNames(item));
  }

  private static JsonObject Deployment(
    string name,
    string? instance = null,
    string? nameLabel = null,
    string? managedBy = null,
    string? version = null,
    int ready = 0,
    int replicas = 1) {
    var labels = new JsonObject();
    if (instance is not null)
      labels[ApplicationManifest.InstanceKey] = instance;
    if (nameLabel is not null)
      labels[ApplicationManifest.NameKey] = nameLabel;
    if (managedBy is not null)
      labels[ApplicationManifest.ManagedByKey] = managedBy;
    if (version is not null)
      labels[ApplicationManifest.VersionKey] = version;

    return new JsonObject {
      ["kind"] = "Deployment",
      ["apiVersion"] = "apps/v1",
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = "apps",
        ["uid"] = name,
        ["creationTimestamp"] = "2020-01-01T00:00:00Z",
        ["labels"] = labels
      },
      ["spec"] = new JsonObject { ["replicas"] = replicas },
      ["status"] = new JsonObject { ["readyReplicas"] = ready }
    };
  }

  private static JsonObject Pod(
    string name,
    string ns,
    string instance,
    string nameLabel,
    string phase) {
    return new JsonObject {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = ns,
        ["labels"] = new JsonObject {
          [ApplicationManifest.InstanceKey] = instance,
          [ApplicationManifest.NameKey] = nameLabel
        }
      },
      ["status"] = new JsonObject { ["phase"] = phase }
    };
  }
}
