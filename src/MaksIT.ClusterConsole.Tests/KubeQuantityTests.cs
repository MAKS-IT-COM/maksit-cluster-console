using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Tests;

public class KubeQuantityTests {
  [Theory]
  [InlineData("100m", 0.1)]
  [InlineData("2500m", 2.5)]
  [InlineData("1", 1)]
  [InlineData("1000n", 0.000001)]
  [InlineData("4", 4)]
  [InlineData("2u", 0.000002)]
  [InlineData("2k", 2000)]
  [InlineData("2K", 2000)]
  [InlineData("nope", 0)]
  [InlineData("-", 0)]
  [InlineData(null, 0)]
  public void ToCores_parses_cpu(string? raw, double expected) {
    Assert.Equal(expected, KubeQuantity.ToCores(raw), 9);
  }

  [Theory]
  [InlineData("512Mi", 536870912)]
  [InlineData("1Gi", 1073741824)]
  [InlineData("1000Ki", 1024000)]
  [InlineData("1KiB", 1024)]
  [InlineData("256.0MiB", 268435456)]
  [InlineData("1Ti", 1099511627776)]
  [InlineData("1M", 1000000)]
  [InlineData("1G", 1000000000)]
  [InlineData("1000m", 1)]
  [InlineData("100", 100)]
  [InlineData("nope", 0)]
  public void ToBytes_parses_memory(string raw, long expected) {
    Assert.Equal(expected, KubeQuantity.ToBytes(raw));
  }

  [Theory]
  [InlineData(0.25, "250m")]
  [InlineData(2.5, "2.5")]
  public void FormatCores_uses_millicores_below_one(double cores, string expected) =>
    Assert.Equal(expected, KubeQuantity.FormatCores(cores));

  [Fact]
  public void FormatBytes_uses_binary_units() {
    Assert.Equal("1 GiB", KubeQuantity.FormatBytes(1024L * 1024 * 1024));
    Assert.Equal("1 MiB", KubeQuantity.FormatBytes(1024 * 1024));
    Assert.Equal("100 B", KubeQuantity.FormatBytes(100));
    Assert.Equal("1.0GiB", KubeQuantity.FormatBytesCompact(1024L * 1024 * 1024));
    Assert.Equal("100B", KubeQuantity.FormatBytesCompact(100));
  }

  [Theory]
  [InlineData(0, "0")]
  [InlineData(100, "100")]
  [InlineData(1536, "1.5Ki")]
  [InlineData(2L * 1024 * 1024 * 1024, "2Gi")]
  public void FormatMemoryQuantity_picks_the_largest_exact_unit(long bytes, string expected) =>
    Assert.Equal(expected, KubeQuantity.FormatMemoryQuantity(bytes));

  [Theory]
  [InlineData(536_870_912, "512Mi")]
  [InlineData(805_306_368, "768Mi")]
  public void FormatMemoryQuantity_round_trips_for_metrics(long bytes, string expected) {
    Assert.Equal(expected, KubeQuantity.FormatMemoryQuantity(bytes));
    Assert.Equal(bytes, KubeQuantity.ToBytes(KubeQuantity.FormatMemoryQuantity(bytes)));
  }

  [Theory]
  [InlineData(536_870_912, "512 MB")]
  [InlineData(1_048_576, "1 MB")]
  [InlineData(512_000, "<1 MB")]
  public void FormatMegabytes_uses_task_manager_style(long bytes, string expected) =>
    Assert.Equal(expected, KubeQuantity.FormatMegabytes(bytes));

  [Theory]
  [InlineData(0.001, 4, "<0.1%")]
  [InlineData(0.0001, 4, "<0.1%")]
  [InlineData(0, 4, "0%")]
  public void FormatCpuPercent_uses_task_manager_style(double used, double allocatable, string expected) =>
    Assert.Equal(expected, ApplicationManifest.FormatCpuPercent(used, allocatable, metricsAvailable: true));

  [Theory]
  [InlineData(536_870_912, "512.0MiB")]
  [InlineData(1_048_576, "1.0MiB")]
  [InlineData(512_000, "<1Mi")]
  public void FormatMemoryUsage_uses_k8s_compact_units(long bytes, string expected) =>
    Assert.Equal(expected, ApplicationManifest.FormatMemoryUsage(bytes, metricsAvailable: true));

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

    var empty = ResourceSlice.Empty("pods");
    Assert.Equal(1, empty.Scale);
    Assert.Equal("0", empty.Caption.Split('/')[0].Trim());
    Assert.Equal("", empty.LimitsWarning);
    var zero = new ClusterUsage("v1", "linux", 0, empty, empty, empty, [], [], false, "metrics down");
    Assert.Equal(0, zero.CpuPercent);
    Assert.Equal("metrics down", zero.MetricsMessage);

    var memory = new ResourceSlice(1024d * 1024 * 1024, 0, 0, 2d * 1024 * 1024 * 1024, 2d * 1024 * 1024 * 1024, "memory");
    Assert.Equal("1.0GiB / 2.0GiB", memory.Caption);
    var closed = 0;
    var handle = new PortForwardHandle("web", "apps", 8080, 18080, new MemoryStream(), () => closed++, 80);
    Assert.Equal(80, handle.RequestedPort);
    handle.Retarget("web-2", "apps", 9090);
    Assert.Equal("web-2", handle.PodName);
    Assert.Equal(9090, handle.ContainerPort);
    handle.Dispose();
    Assert.Equal(1, closed);
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
