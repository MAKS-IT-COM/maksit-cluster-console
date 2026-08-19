using k8s.Models;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Tests;

public class ContainerLimitsTests {
  [Fact]
  public void Groups_replica_pods_under_the_deployment_and_skips_unlimited_containers() {
    var replicaSets = new[] {
      new V1ReplicaSet {
        Metadata = new V1ObjectMeta {
          Name = "api-abc",
          NamespaceProperty = "apps",
          OwnerReferences = [
            new V1OwnerReference { Kind = "Deployment", Name = "api", Controller = true }
          ]
        }
      }
    };
    var pods = new[] {
      Pod("apps", "api-abc-1", "api-abc", "web", "500m", "2", "256Mi", "1Gi"),
      Pod("apps", "api-abc-2", "api-abc", "web", "500m", "2", "256Mi", "1Gi"),
      Pod("apps", "bare-1", null, "main", "100m", "", "64Mi", "")
    };

    var rows = ContainerLimits.From(pods, replicaSets);
    var api = Assert.Single(rows);
    Assert.Equal("Deployment", api.WorkloadKind);
    Assert.Equal("api", api.WorkloadName);
    Assert.Equal("apps", api.Namespace);
    Assert.Equal("web", api.Container);
    Assert.Equal(2, api.Pods);
    Assert.Equal("2", api.CpuLimit);
    Assert.Equal(4, api.CpuContribution);
  }

  private static V1Pod Pod(
    string ns,
    string name,
    string? replicaSet,
    string container,
    string cpuReq,
    string cpuLim,
    string memReq,
    string memLim) {
    var owners = replicaSet is null
      ? null
      : new List<V1OwnerReference> {
          new() { Kind = "ReplicaSet", Name = replicaSet, Controller = true }
        };
    var requests = new Dictionary<string, ResourceQuantity>();
    var limits = new Dictionary<string, ResourceQuantity>();
    if (cpuReq.Length > 0)
      requests["cpu"] = new ResourceQuantity(cpuReq);
    if (memReq.Length > 0)
      requests["memory"] = new ResourceQuantity(memReq);
    if (cpuLim.Length > 0)
      limits["cpu"] = new ResourceQuantity(cpuLim);
    if (memLim.Length > 0)
      limits["memory"] = new ResourceQuantity(memLim);

    return new V1Pod {
      Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = ns, OwnerReferences = owners },
      Status = new V1PodStatus { Phase = "Running" },
      Spec = new V1PodSpec {
        Containers = [
          new V1Container {
            Name = container,
            Resources = new V1ResourceRequirements {
              Requests = requests.Count == 0 ? null : requests,
              Limits = limits.Count == 0 ? null : limits
            }
          }
        ]
      }
    };
  }
}
