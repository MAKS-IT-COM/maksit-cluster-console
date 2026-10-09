using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class JsonPathTests {
  [Fact]
  public void Volume_claim_and_service_addresses_follow_the_bound_object() {
    Assert.Equal("", JsonPath.VolumeClaim(null));
    Assert.Equal("pg-data", JsonPath.Read(Doc("""
      { "spec": { "claimRef": { "name": "pg-data" } } }
      """), "pv.claim"));
    Assert.Equal("postgresql/pg-data", JsonPath.VolumeClaim(Doc("""
      { "spec": { "claimRef": { "namespace": "postgresql", "name": "pg-data" } } }
      """)));

    var cluster = Doc("""{ "spec": { "type": "ClusterIP" } }""");
    Assert.Equal("Active", JsonPath.ServiceStatus(cluster));

    var pending = Doc("""{ "spec": { "type": "LoadBalancer" } }""");
    Assert.Equal("Pending", JsonPath.ServiceStatus(pending));

    var unreachable = Doc("""
      {
        "spec": { "type": "LoadBalancer", "loadBalancerIP": "172.16.0.11" },
        "metadata": { "annotations": { "lbipam.cilium.io/ips": "172.16.0.11, 172.16.0.12" } },
        "status": {
          "loadBalancer": { "ingress": [{ "ip": "172.16.0.11" }, { "hostname": "lb.example" }] },
          "conditions": [{ "type": "cilium.io/ipam-satisfied", "status": "False" }]
        }
      }
      """);
    Assert.Equal("Unreachable", JsonPath.ServiceStatus(unreachable));
    Assert.Equal("172.16.0.11,lb.example,172.16.0.12", JsonPath.ServiceExternalIp(unreachable));
    Assert.Equal("", JsonPath.ServiceStatus(null));
    Assert.Equal("", JsonPath.ServiceExternalIp(null));
  }

  [Fact]
  public void Age_rounds_down_and_keeps_unparsed_text() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    Assert.Equal("2d", JsonPath.Age(now.AddDays(-2).AddHours(-3), now));
    Assert.Equal("3h", JsonPath.Age(now.AddHours(-3), now));
    Assert.Equal("3h5m", JsonPath.Age(now.AddHours(-3).AddMinutes(-5), now));
    Assert.Equal("4m", JsonPath.Age(now.AddMinutes(-4), now));
    Assert.Equal("12s", JsonPath.Age(now.AddSeconds(-12), now));
    Assert.Equal("0s", JsonPath.Age(now.AddMinutes(5), now));
    Assert.Equal("not-a-time", JsonPath.Age("not-a-time"));
    Assert.Equal("", JsonPath.Age((string?)null));
  }

  [Fact]
  public void Storage_version_falls_back_when_no_version_is_marked_storage() {
    var served = Doc("""
      { "spec": { "versions": [{ "name": "v1alpha1", "served": true }, { "name": "v1beta1" }] } }
      """);
    Assert.Equal("v1alpha1", JsonPath.CrdStorageVersion(served));

    var first = Doc("""{ "spec": { "versions": [{ "name": "v1" }] } }""");
    Assert.Equal("v1", JsonPath.CrdStorageVersion(first));
    Assert.Equal("", JsonPath.CrdStorageVersion(null));
    Assert.Equal("0/0", JsonPath.PodReady(Doc("""{ "status": {} }""")));
    Assert.Equal("", JsonPath.Read(null, "metadata.name"));
    Assert.Equal("", JsonPath.Read(Doc("""{ "metadata": { "name": "web" } }"""), " "));
  }

  private static JsonObject Doc(string json) =>
    JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("expected object");
}
