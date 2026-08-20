using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ResourceCatalogTests {
  [Fact]
  public void BuiltIns_cover_lens_sections() {
    var sections = ResourceCatalog.BuiltIns.Select(d => d.Section).Distinct().ToHashSet();
    Assert.Contains(ResourceCatalog.Workloads, sections);
    Assert.Contains(ResourceCatalog.Config, sections);
    Assert.Contains(ResourceCatalog.Network, sections);
    Assert.Contains(ResourceCatalog.Storage, sections);
    Assert.Contains(ResourceCatalog.AccessControl, sections);
    Assert.Contains(ResourceCatalog.Dapr, sections);
    Assert.Contains(ResourceCatalog.CustomResources, sections);
    Assert.NotNull(ResourceCatalog.Find("pods"));
    Assert.NotNull(ResourceCatalog.Find("deployments"));
    Assert.NotNull(ResourceCatalog.Find("components"));
    Assert.NotNull(ResourceCatalog.Find("customresourcedefinitions"));
  }

  [Fact]
  public void Applications_are_a_navigator_view_not_a_builtin_gvr() {
    Assert.Contains(ResourceCatalog.Applications, ResourceCatalog.Sections);
    Assert.DoesNotContain(ResourceCatalog.BuiltIns, d => d.Id == ResourceCatalog.ApplicationsId);
    Assert.Same(ResourceCatalog.ApplicationsDescriptor, ResourceCatalog.Find(ResourceCatalog.ApplicationsId));
    Assert.Null(ResourceCatalog.FindByGvk("v1", "Application"));

    var workspace = new ClusterWorkspace();
    var item = Assert.Single(workspace.Navigator, n => n.Id == ResourceCatalog.ApplicationsId);
    Assert.True(item.IsSpecial);
    Assert.Same(ResourceCatalog.ApplicationsDescriptor, item.Descriptor);
    Assert.Contains("Instance", item.Descriptor!.Columns.Select(c => c.Header));
    Assert.DoesNotContain("Kind", item.Descriptor.Columns.Select(c => c.Header));
    Assert.Contains("Pods", item.Descriptor.DetailTabs);

    var sections = ResourceCatalog.Sections.ToList();
    Assert.True(sections.IndexOf(ResourceCatalog.Nodes) < sections.IndexOf(ResourceCatalog.Applications));
    Assert.True(sections.IndexOf(ResourceCatalog.Applications) < sections.IndexOf(ResourceCatalog.Workloads));
  }

  [Fact]
  public void Port_forwarding_is_a_navigator_table_not_a_builtin_gvr() {
    Assert.Same(ResourceCatalog.PortForwardingDescriptor, ResourceCatalog.Find(ResourceCatalog.PortForwardingId));
    Assert.DoesNotContain(ResourceCatalog.BuiltIns, d => d.Id == ResourceCatalog.PortForwardingId);
    Assert.Null(ResourceCatalog.FindByGvk("v1", "PortForward"));
    Assert.Contains("Pod", ResourceCatalog.PortForwardingDescriptor.Columns.Select(c => c.Header));
    Assert.Contains("Local", ResourceCatalog.PortForwardingDescriptor.Columns.Select(c => c.Header));
    Assert.Contains("Remote", ResourceCatalog.PortForwardingDescriptor.Columns.Select(c => c.Header));
    Assert.False(ResourceCatalog.PortForwardingDescriptor.Actions.CanApply);
    Assert.False(ResourceCatalog.PortForwardingDescriptor.Actions.CanDelete);

    var workspace = new ClusterWorkspace();
    var item = Assert.Single(workspace.Navigator, n => n.Id == ResourceCatalog.PortForwardingId);
    Assert.True(item.IsSpecial);
    Assert.Same(ResourceCatalog.PortForwardingDescriptor, item.Descriptor);
    Assert.Equal(ResourceCatalog.Network, item.Section);
  }

  [Fact]
  public void PortForwardRow_maps_handle_into_table_cells() {
    using var handle = new PortForwardHandle("web-1", "apps", 8080, 18080, Stream.Null);
    var row = PortForwardRow.From(handle, "pf-1");
    Assert.Equal("pf-1", row.Uid);
    Assert.Equal("localhost:18080", row.Name);
    Assert.Equal("apps", row.Namespace);
    Assert.Equal("web-1", row.Cells["Pod"]);
    Assert.Equal("18080", row.Cells["Local"]);
    Assert.Equal("8080", row.Cells["Remote"]);
    Assert.Equal("Active", row.Cells["Status"]);
    Assert.True(PortForwardRow.TryLocalPort(row, out var localPort));
    Assert.Equal(18080, localPort);
    Assert.True(PortForwardRow.TryLocalUrl(row, out var url));
    Assert.Equal("http://127.0.0.1:18080/", url);
    Assert.Equal("http://127.0.0.1:18080/", PortForwardRow.LocalUrl(18080));
    Assert.Equal(
      "Port-forward started: http://127.0.0.1:18080 → apps/web-1:8080.",
      PortForwardRow.StartedMessage(handle));
    using var rebound = new PortForwardHandle("web-1", "apps", 8080, 18081, Stream.Null);
    Assert.Equal(
      "Port-forward rebound: localhost:18080 → http://127.0.0.1:18081 → apps/web-1:8080.",
      PortForwardRow.ReboundMessage(18080, rebound));
    Assert.Equal("Port-forward failed: bind failed", PortForwardRow.FailedMessage(["bind failed"]));
  }

  [Fact]
  public void PortForwardRow_shows_requested_service_port_not_mapped_container_port() {
    using var handle = new PortForwardHandle(
      "longhorn-ui-1",
      "longhorn-system",
      8000,
      80,
      Stream.Null,
      requestedPort: 80);
    var row = PortForwardRow.From(handle, "pf-80");
    Assert.Equal("localhost:80", row.Name);
    Assert.Equal("80", row.Cells["Local"]);
    Assert.Equal("80", row.Cells["Remote"]);
    Assert.Equal(
      "Port-forward started: http://127.0.0.1:80 → longhorn-system/longhorn-ui-1:80.",
      PortForwardRow.StartedMessage(handle));
  }

  [Fact]
  public void Every_section_has_a_distinct_icon_path() {
    var paths = ResourceCatalog.Sections.Select(NavigatorIcons.Path).ToList();
    Assert.All(paths, path => Assert.StartsWith("M", path));
    Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());
    Assert.Equal("M5,5 H19 V19 H5 Z", NavigatorIcons.Path("unknown"));
  }

  [Fact]
  public void JsonPath_reads_pod_name_and_ready() {
    var pod = JsonNode.Parse("""
      {
        "metadata": { "name": "web", "namespace": "default", "uid": "1", "creationTimestamp": "2020-01-01T00:00:00Z" },
        "status": {
          "phase": "Running",
          "containerStatuses": [
            { "ready": true, "restartCount": 2 },
            { "ready": false, "restartCount": 1 }
          ]
        }
      }
      """) as JsonObject;

    Assert.NotNull(pod);
    Assert.Equal("web", JsonPath.Name(pod));
    Assert.Equal("1/2", JsonPath.PodReady(pod));
    Assert.Equal("3", JsonPath.PodRestarts(pod));
    var row = ResourceRow.From(pod, ResourceCatalog.Find("pods")!);
    Assert.Equal("web", row.Name);
    Assert.Equal("Running", row.Cells["Status"]);
  }

  [Fact]
  public void JsonPath_lists_pod_containers_and_native_sidecars() {
    var pod = JsonNode.Parse("""
      {
        "spec": {
          "initContainers": [
            { "name": "proxy", "image": "envoy:v1", "restartPolicy": "Always" },
            { "name": "init-config", "image": "busybox:1" }
          ],
          "containers": [
            { "name": "frontend", "image": "quay.io/cilium/hubble-ui:v0.13.1" },
            { "name": "backend", "image": "quay.io/cilium/hubble-ui-backend:v0.13.1" }
          ]
        },
        "status": {
          "containerStatuses": [
            { "name": "frontend", "ready": true, "restartCount": 0, "state": { "running": {} } },
            { "name": "backend", "ready": true, "restartCount": 1, "state": { "running": {} } }
          ],
          "initContainerStatuses": [
            { "name": "proxy", "ready": true, "restartCount": 0, "state": { "running": {} } },
            { "name": "init-config", "ready": true, "restartCount": 0, "state": { "terminated": { "reason": "Completed" } } }
          ]
        }
      }
      """) as JsonObject;

    Assert.NotNull(pod);
    var containers = JsonPath.ListPodContainers(pod);
    Assert.Equal(["proxy", "init-config", "frontend", "backend"], containers.Select(c => c.Name).ToArray());
    Assert.Equal("Sidecar", containers[0].Kind);
    Assert.Equal("Init", containers[1].Kind);
    Assert.Equal("Container", containers[2].Kind);
    Assert.Equal("quay.io/cilium/hubble-ui:v0.13.1", containers[2].Image);
    Assert.Equal("Running", containers[3].State);
    Assert.Equal(1, containers[3].Restarts);
  }

  [Fact]
  public void Workload_details_include_logs_and_terminal() {
    var tabs = ResourceCatalog.Find("deployments")!.DetailTabs;
    Assert.Contains("Pods", tabs);
    Assert.Contains("Logs", tabs);
    Assert.Contains("Terminal", tabs);
  }

  [Fact]
  public void YamlFormatter_round_trips_object() {
    var json = JsonNode.Parse("""{"kind":"Pod","metadata":{"name":"x"}}""")!;
    var yaml = YamlFormatter.FromJson(json);
    Assert.Contains("kind:", yaml);
    Assert.Contains("name:", yaml);
  }

  [Fact]
  public void FindByGvk_resolves_configmap_and_ingressclass_plurals() {
    var cm = ResourceCatalog.FindByGvk("v1", "ConfigMap");
    Assert.NotNull(cm);
    Assert.Equal("configmaps", cm.Plural);

    var ingressClass = ResourceCatalog.FindByGvk("networking.k8s.io/v1", "IngressClass");
    Assert.NotNull(ingressClass);
    Assert.Equal("ingressclasses", ingressClass.Plural);
  }

  [Fact]
  public void ResourceDocument_decodes_secret_data_and_writes_stringData() {
    var secret = JsonNode.Parse("""
      {
        "apiVersion": "v1",
        "kind": "Secret",
        "metadata": { "name": "app", "namespace": "default" },
        "data": { "password": "c2VjcmV0" }
      }
      """) as JsonObject;

    Assert.NotNull(secret);
    var entries = ResourceDocument.ReadDataEntries(secret);
    Assert.Single(entries);
    Assert.Equal("password", entries[0].Key);
    Assert.Equal("secret", entries[0].Value);
    Assert.False(entries[0].IsBinary);

    var edited = ResourceDocument.Clone(secret);
    ResourceDocument.WriteDataEntries(edited, [new ResourceDataEntry("password", "changed", false)]);
    Assert.Equal("changed", edited["stringData"]?["password"]?.GetValue<string>());
    Assert.Null(edited["data"]);
  }

  [Fact]
  public void ResourceDocument_prepares_apply_body_without_status() {
    var doc = JsonNode.Parse("""
      {
        "kind": "ConfigMap",
        "metadata": { "name": "x", "managedFields": [ { "manager": "kubectl" } ], "resourceVersion": "1" },
        "data": { "a": "b" },
        "status": { "phase": "Active" }
      }
      """) as JsonObject;

    Assert.NotNull(doc);
    var prepared = ResourceDocument.PrepareForApply(doc);
    Assert.Null(prepared["status"]);
    Assert.Null(prepared["metadata"]?["managedFields"]);
    Assert.Equal("1", prepared["metadata"]?["resourceVersion"]?.GetValue<string>());
    Assert.Equal("b", prepared["data"]?["a"]?.GetValue<string>());
  }

  [Fact]
  public void FromCustomResourceDefinition_groups_by_api_group_and_uses_storage_version() {
    var cilium = JsonNode.Parse("""
      {
        "spec": {
          "group": "cilium.io",
          "scope": "Namespaced",
          "names": { "kind": "CiliumEndpoint", "plural": "ciliumendpoints" },
          "versions": [
            { "name": "v2alpha1", "served": true, "storage": false },
            { "name": "v2", "served": true, "storage": true }
          ]
        }
      }
      """) as JsonObject;
    var dapr = JsonNode.Parse("""
      {
        "spec": {
          "group": "dapr.io",
          "scope": "Namespaced",
          "names": { "kind": "Component", "plural": "components" },
          "versions": [ { "name": "v1alpha1", "served": true, "storage": true } ]
        }
      }
      """) as JsonObject;
    var longhorn = JsonNode.Parse("""
      {
        "spec": {
          "group": "longhorn.io",
          "scope": "Cluster",
          "names": { "kind": "Setting", "plural": "settings" },
          "versions": [ { "name": "v1beta2", "served": true, "storage": true } ]
        }
      }
      """) as JsonObject;

    Assert.NotNull(cilium);
    Assert.NotNull(dapr);
    Assert.NotNull(longhorn);
    var descriptors = new[] {
      ResourceCatalog.FromCustomResourceDefinition(cilium)!,
      ResourceCatalog.FromCustomResourceDefinition(dapr)!,
      ResourceCatalog.FromCustomResourceDefinition(longhorn)!
    };

    Assert.Equal("v2", descriptors[0].Version);
    Assert.True(descriptors[0].Namespaced);
    Assert.False(descriptors[2].Namespaced);
    Assert.Equal("crd:cilium.io/v2/ciliumendpoints", descriptors[0].Id);

    var groups = ResourceCatalog.GroupCustomResources([
      ResourceCatalog.Find("customresourcedefinitions")!,
      ..descriptors
    ]);
    Assert.Equal(["cilium.io", "dapr.io", "longhorn.io"], groups.Select(g => g.Group).ToArray());
    Assert.Equal("CiliumEndpoint", groups[0].Kinds[0].Title);
    Assert.DoesNotContain(groups.SelectMany(g => g.Kinds), d => d.Kind == "CustomResourceDefinition");
  }

  [Fact]
  public void Definitions_row_maps_resource_group_version_scope() {
    var crd = JsonNode.Parse("""
      {
        "metadata": { "name": "ciliumendpoints.cilium.io", "uid": "1", "creationTimestamp": "2020-01-01T00:00:00Z" },
        "spec": {
          "group": "cilium.io",
          "scope": "Namespaced",
          "names": { "kind": "CiliumEndpoint", "plural": "ciliumendpoints" },
          "versions": [
            { "name": "v2alpha1", "served": true, "storage": false },
            { "name": "v2", "served": true, "storage": true }
          ]
        }
      }
      """) as JsonObject;

    Assert.NotNull(crd);
    var row = ResourceRow.From(crd, ResourceCatalog.Find("customresourcedefinitions")!);
    Assert.Equal("CiliumEndpoint", row.Cells["Resource"]);
    Assert.Equal("cilium.io", row.Cells["Group"]);
    Assert.Equal("v2", row.Cells["Version"]);
    Assert.Equal("Namespaced", row.Cells["Scope"]);
    Assert.False(string.IsNullOrWhiteSpace(row.Cells["Age"]));
    Assert.True(ResourceCatalog.Find("customresourcedefinitions")!.Actions.CanDelete);
    Assert.True(ResourceCatalog.Find("customresourcedefinitions")!.Actions.CanApply);
  }

  [Fact]
  public void Persistent_volume_row_includes_bound_claim() {
    var bound = JsonNode.Parse("""
      {
        "metadata": { "name": "pvc-1", "uid": "1", "creationTimestamp": "2020-01-01T00:00:00Z" },
        "spec": {
          "capacity": { "storage": "10Gi" },
          "accessModes": ["ReadWriteOnce"],
          "persistentVolumeReclaimPolicy": "Delete",
          "claimRef": { "namespace": "postgresql", "name": "pg-data" }
        },
        "status": { "phase": "Bound" }
      }
      """) as JsonObject;
    var available = JsonNode.Parse("""
      {
        "metadata": { "name": "unused", "uid": "2", "creationTimestamp": "2020-01-01T00:00:00Z" },
        "spec": {
          "capacity": { "storage": "1Gi" },
          "accessModes": ["ReadWriteOnce"],
          "persistentVolumeReclaimPolicy": "Retain"
        },
        "status": { "phase": "Available" }
      }
      """) as JsonObject;

    Assert.NotNull(bound);
    Assert.NotNull(available);
    var descriptor = ResourceCatalog.Find("persistentvolumes")!;
    Assert.Contains("Claim", descriptor.Columns.Select(c => c.Header));

    var boundRow = ResourceRow.From(bound, descriptor);
    Assert.Equal("postgresql/pg-data", boundRow.Cells["Claim"]);
    Assert.Equal("Bound", boundRow.Cells["Status"]);

    var availableRow = ResourceRow.From(available, descriptor);
    Assert.Equal("", availableRow.Cells["Claim"]);
  }

  [Fact]
  public void Service_row_includes_load_balancer_and_cluster_ips() {
    var service = JsonNode.Parse("""
      {
        "metadata": {
          "name": "postgresql-postgres-bgp",
          "namespace": "postgresql",
          "annotations": { "lbipam.cilium.io/ips": "172.16.0.11" }
        },
        "spec": {
          "type": "LoadBalancer",
          "clusterIP": "10.43.131.123",
          "loadBalancerIP": "172.16.0.11",
          "ports": [{ "port": 5432 }]
        },
        "status": {
          "loadBalancer": { "ingress": [{ "ip": "172.16.0.11", "ipMode": "VIP" }] }
        }
      }
      """) as JsonObject;

    Assert.NotNull(service);
    var row = ResourceRow.From(service, ResourceCatalog.Find("services")!);
    Assert.Equal("LoadBalancer", row.Cells["Type"]);
    Assert.Equal("10.43.131.123", row.Cells["Cluster IP"]);
    Assert.Equal("172.16.0.11", row.Cells["External IP"]);
    Assert.Equal("5432", row.Cells["Ports"]);
  }
}
