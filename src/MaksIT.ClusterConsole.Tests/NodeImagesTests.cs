using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class NodeImagesTests {
  [Fact]
  public void Short_pod_image_marks_docker_hub_cache_used_and_leaves_other_tags_unused() {
    var node = Node(
      Image("docker.io/library/nginx:1.27", "docker.io/library/nginx@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 20_000_000),
      Image("docker.io/library/nginx:1.26", 5_000_000),
      Image("ghcr.io/org/unused:1", 1_000_000));
    var pods = new[] {
      Pod("default", "web", "nginx:1.27", "docker.io/library/nginx@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
    };

    var report = NodeImages.Classify(node, pods, true);

    Assert.Equal(["docker.io/library/nginx:1.26", "ghcr.io/org/unused:1", "docker.io/library/nginx:1.27"], report.Images.Select(i => i.Image).ToArray());
    Assert.Equal(NodeImages.Unused, report.Images[0].State);
    Assert.Equal(NodeImages.Unused, report.Images[1].State);
    Assert.Equal(NodeImages.Used, report.Images[2].State);
    Assert.Equal("default/web", report.Images[2].Pods);
    Assert.Contains("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", report.Images[2].OtherNames);
    Assert.Equal("1 used · 19.1MiB · 2 unused · 5.7MiB", report.Caption);
  }

  [Fact]
  public void Image_id_digest_marks_the_cache_entry_used() {
    var node = Node(Image("docker.io/library/busybox:1.36", "docker.io/library/busybox@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 2_000_000));
    var pods = new[] {
      Pod("kube-system", "pause", "busybox:latest", "containerd://sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")
    };

    var report = NodeImages.Classify(node, pods, true);

    Assert.Equal(NodeImages.Used, report.Images[0].State);
    Assert.Equal("kube-system/pause", report.Images[0].Pods);
  }

  [Fact]
  public void Init_container_image_counts_as_used() {
    var node = Node(Image("docker.io/library/busybox:1.36", 1000));
    var pod = JsonNode.Parse("""
      {
        "metadata": { "name": "job", "namespace": "batch" },
        "spec": { "initContainers": [{ "name": "init", "image": "busybox:1.36" }], "containers": [{ "name": "app", "image": "app:1" }] }
      }
      """) as JsonObject;

    var report = NodeImages.Classify(node, [pod!], true);

    Assert.Equal(NodeImages.Used, report.Images[0].State);
    Assert.Equal("batch/job", report.Images[0].Pods);
  }

  [Fact]
  public void Untagged_pod_image_matches_latest() {
    var node = Node(
      Image("docker.io/library/redis:latest", 10),
      Image("docker.io/library/redis:7", 10));
    var pods = new[] { Pod("default", "cache", "redis", null) };

    var report = NodeImages.Classify(node, pods, true);

    Assert.Equal(NodeImages.Used, report.Images.Single(i => i.Image.EndsWith(":latest", StringComparison.Ordinal)).State);
    Assert.Equal(NodeImages.Unused, report.Images.Single(i => i.Image.EndsWith(":7", StringComparison.Ordinal)).State);
  }

  [Fact]
  public void Missing_pod_list_does_not_call_images_unused() {
    var node = Node(Image("nginx:1.27", 10));

    var report = NodeImages.Classify(node, [], false, "pods is forbidden");

    Assert.Equal("—", report.Images[0].State);
    Assert.Equal("", report.Images[0].Pods);
    Assert.Contains("pods is forbidden", report.Caption);
  }

  [Fact]
  public void Fifty_images_notes_the_kubelet_cap() {
    var images = Enumerable.Range(0, NodeImages.StatusLimit)
      .Select(i => Image($"example/app:{i}", 1000 - i))
      .ToArray();

    var report = NodeImages.Classify(Node(images), [], true);

    Assert.Equal(NodeImages.StatusLimit, report.Images.Count);
    Assert.Contains("at most 50", report.Caption);
    Assert.All(report.Images, image => Assert.Equal(NodeImages.Unused, image.State));
  }

  [Fact]
  public void Empty_node_has_no_cached_images() {
    var report = NodeImages.Classify(JsonNode.Parse("""{"kind":"Node"}""") as JsonObject, [], true);

    Assert.Empty(report.Images);
    Assert.Equal("No cached images reported on this node.", report.Caption);
  }

  [Fact]
  public void Node_field_selector_escapes_special_characters() {
    Assert.Equal("spec.nodeName=k3ssrv0001", NodeImages.PodsOnNodeSelector("k3ssrv0001"));
    Assert.Equal(@"spec.nodeName=a\=b\,c", NodeImages.PodsOnNodeSelector("a=b,c"));
  }

  [Fact]
  public void Nodes_detail_includes_images() {
    Assert.Contains("Images", ResourceCatalog.Find("nodes")!.DetailTabs);
  }

  private static JsonObject Node(params JsonObject[] images) =>
    new() {
      ["kind"] = "Node",
      ["status"] = new JsonObject {
        ["images"] = new JsonArray(images.Select(image => (JsonNode)image).ToArray())
      }
    };

  private static JsonObject Image(string name, long size) =>
    Image(name, null, size);

  private static JsonObject Image(string name, string? digestName, long size) {
    var names = new JsonArray(name);
    if (digestName is not null)
      names.Add(digestName);
    return new JsonObject {
      ["names"] = names,
      ["sizeBytes"] = size
    };
  }

  private static JsonObject Pod(string ns, string name, string image, string? imageId) {
    var status = new JsonObject {
      ["name"] = "app",
      ["image"] = image
    };
    if (imageId is not null)
      status["imageID"] = imageId;

    return new JsonObject {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = ns
      },
      ["spec"] = new JsonObject {
        ["nodeName"] = "node-a",
        ["containers"] = new JsonArray(new JsonObject {
          ["name"] = "app",
          ["image"] = image
        })
      },
      ["status"] = new JsonObject {
        ["containerStatuses"] = new JsonArray(status)
      }
    };
  }
}
