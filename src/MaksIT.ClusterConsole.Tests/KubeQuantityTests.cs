using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Tests;

public class KubeQuantityTests {
  [Theory]
  [InlineData("100m", 0.1)]
  [InlineData("2500m", 2.5)]
  [InlineData("1", 1)]
  [InlineData("1000n", 0.000001)]
  [InlineData("4", 4)]
  public void ToCores_parses_cpu(string raw, double expected) {
    Assert.Equal(expected, KubeQuantity.ToCores(raw), 9);
  }

  [Theory]
  [InlineData("512Mi", 536870912)]
  [InlineData("1Gi", 1073741824)]
  [InlineData("1000Ki", 1024000)]
  [InlineData("100", 100)]
  public void ToBytes_parses_memory(string raw, long expected) {
    Assert.Equal(expected, KubeQuantity.ToBytes(raw));
  }

  [Fact]
  public void ClusterUsage_percent_uses_allocatable() {
    var usage = new ClusterUsage(
      "v1",
      "linux",
      2,
      new ResourceSlice(1, 4.47, 17.1, 4, 4, "cpu"),
      new ResourceSlice(512, 256, 2048, 1024, 1024, "memory"),
      new ResourceSlice(10, 0, 0, 110, 110, "pods"),
      [],
      [],
      true,
      null);
    Assert.Equal(25, usage.CpuPercent);
    Assert.Equal(50, usage.MemoryPercent);
    Assert.Equal(10d / 110 * 100, usage.PodPercent, 6);
    Assert.True(usage.Cpu.LimitsExceedCapacity);
    Assert.Equal("Usage: 1.00", usage.Cpu.UsageLine);
    Assert.Equal("Limits: 17.10", usage.Cpu.LimitsLine);
    Assert.Equal("Specified limits are higher than node capacity!", usage.Cpu.LimitsWarning);
  }

  [Fact]
  public void ClusterMetrics_sums_requests_limits_capacity() {
    var nodes = new[] {
      new k8s.Models.V1Node {
        Metadata = new k8s.Models.V1ObjectMeta { Name = "node" },
        Status = new k8s.Models.V1NodeStatus {
          Capacity = new Dictionary<string, k8s.Models.ResourceQuantity> {
            ["cpu"] = new("12"),
            ["memory"] = new("96Gi"),
            ["pods"] = new("330")
          },
          Allocatable = new Dictionary<string, k8s.Models.ResourceQuantity> {
            ["cpu"] = new("12"),
            ["memory"] = new("95Gi"),
            ["pods"] = new("330")
          }
        }
      }
    };
    var pods = new[] {
      new k8s.Models.V1Pod {
        Status = new k8s.Models.V1PodStatus { Phase = "Running" },
        Spec = new k8s.Models.V1PodSpec {
          NodeName = "node",
          Containers = [
            new k8s.Models.V1Container {
              Resources = new k8s.Models.V1ResourceRequirements {
                Requests = new Dictionary<string, k8s.Models.ResourceQuantity> {
                  ["cpu"] = new("500m"),
                  ["memory"] = new("1Gi")
                },
                Limits = new Dictionary<string, k8s.Models.ResourceQuantity> {
                  ["cpu"] = new("2"),
                  ["memory"] = new("4Gi")
                }
              }
            }
          ]
        }
      },
      new k8s.Models.V1Pod {
        Status = new k8s.Models.V1PodStatus { Phase = "Succeeded" },
        Spec = new k8s.Models.V1PodSpec {
          Containers = [
            new k8s.Models.V1Container {
              Resources = new k8s.Models.V1ResourceRequirements {
                Requests = new Dictionary<string, k8s.Models.ResourceQuantity> { ["cpu"] = new("8") },
                Limits = new Dictionary<string, k8s.Models.ResourceQuantity> { ["cpu"] = new("8") }
              }
            }
          ]
        }
      }
    };
    var metrics = new Dictionary<string, ResourceMetrics> {
      ["node"] = new("node", null, "1780m", "2Gi")
    };

    var (cpu, memory, podSlice, nodeUsages) = ClusterMetrics.From(nodes, pods, metrics);
    Assert.Equal(1.78, cpu.Used, 2);
    Assert.Equal(0.5, cpu.Requests, 2);
    Assert.Equal(2, cpu.Limits, 2);
    Assert.Equal(12, cpu.Allocatable);
    Assert.Equal(12, cpu.Capacity);
    Assert.False(cpu.LimitsExceedCapacity);
    Assert.Equal(2d * 1024 * 1024 * 1024, memory.Used);
    Assert.Equal(1d * 1024 * 1024 * 1024, memory.Requests);
    Assert.Equal(4d * 1024 * 1024 * 1024, memory.Limits);
    Assert.Equal(2, podSlice.Used);
    Assert.Equal(330, podSlice.Capacity);
    Assert.Single(nodeUsages);
    Assert.Equal("node", nodeUsages[0].Name);
    Assert.Equal(1.78, nodeUsages[0].Cpu.Used, 2);
    Assert.Equal(1, nodeUsages[0].Pods.Used);
  }
}
