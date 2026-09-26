using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class YamlApplyTests {
  [Fact]
  public void Bgp_service_yaml_round_trips_and_lists() {
    var original = JsonNode.Parse("""
      {
        "apiVersion": "v1",
        "kind": "Service",
        "metadata": {
          "name": "postgresql-postgres-bgp",
          "namespace": "postgresql",
          "resourceVersion": "42",
          "annotations": {
            "lbipam.cilium.io/ips": "172.16.0.11",
            "kubectl.kubernetes.io/last-applied-configuration": "{\"apiVersion\":\"v1\",\"kind\":\"Service\",\"metadata\":{\"name\":\"postgresql-postgres-bgp\"}}\n"
          }
        },
        "spec": {
          "type": "LoadBalancer",
          "clusterIP": "10.43.131.123",
          "clusterIPs": ["10.43.131.123"],
          "loadBalancerIP": "172.16.0.11",
          "externalTrafficPolicy": "Cluster",
          "ipFamilies": ["IPv4"],
          "ipFamilyPolicy": "SingleStack",
          "ports": [{ "name": "tcp-postgresql", "port": 5432, "protocol": "TCP", "targetPort": 5432 }],
          "selector": { "app": "postgres" }
        },
        "status": {
          "loadBalancer": { "ingress": [{ "ip": "172.16.0.11", "ipMode": "VIP" }] }
        }
      }
      """) as JsonObject;

    Assert.NotNull(original);
    var yaml = YamlFormatter.FromJson(ResourceDocument.PrepareForEdit(original));
    var parsed = YamlFormatter.ToJsonObject(yaml);
    Assert.NotNull(parsed);
    Assert.Equal("Service", parsed["kind"]?.GetValue<string>());
    Assert.Equal("postgresql-postgres-bgp", parsed["metadata"]?["name"]?.GetValue<string>());
    Assert.Equal("42", parsed["metadata"]?["resourceVersion"]?.GetValue<string>());
    Assert.Contains("postgresql-postgres-bgp", parsed["metadata"]?["annotations"]?["kubectl.kubernetes.io/last-applied-configuration"]?.GetValue<string>());
    var row = ResourceRow.From(parsed, ResourceCatalog.Find("services")!);
    Assert.Equal("172.16.0.11", row.Cells["External IP"]);
    Assert.Equal("5432", row.Cells["Ports"]);
  }
}
