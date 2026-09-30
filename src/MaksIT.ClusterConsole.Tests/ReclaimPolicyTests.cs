using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Tests;

public class ReclaimPolicyTests {
  [Fact]
  public void Replacement_sets_retain_and_keeps_class_settings() {
    var original = JsonNode.Parse("""
      {
        "apiVersion": "storage.k8s.io/v1",
        "kind": "StorageClass",
        "metadata": {
          "name": "longhorn",
          "namespace": "kube-system",
          "uid": "abc",
          "resourceVersion": "9",
          "generation": 3,
          "creationTimestamp": "2020-01-01T00:00:00Z",
          "managedFields": [{ "manager": "kubectl" }],
          "finalizers": ["protect"],
          "labels": { "team": "storage" },
          "annotations": {
            "storageclass.kubernetes.io/is-default-class": "true",
            "kubectl.kubernetes.io/last-applied-configuration": "{\"reclaimPolicy\":\"Delete\"}",
            "example.com/owner": "platform"
          }
        },
        "provisioner": "driver.longhorn.io",
        "reclaimPolicy": "Delete",
        "volumeBindingMode": "WaitForFirstConsumer",
        "allowVolumeExpansion": true,
        "parameters": { "numberOfReplicas": "2" },
        "mountOptions": ["discard"],
        "status": { "phase": "ignored" }
      }
      """) as JsonObject;

    Assert.NotNull(original);
    var replacement = ReclaimPolicy.Replacement(original, ReclaimPolicy.Retain);

    Assert.Equal("Delete", ReclaimPolicy.ClassPolicy(original));
    Assert.Equal("9", original["metadata"]?["resourceVersion"]?.GetValue<string>());
    Assert.Equal("Retain", replacement["reclaimPolicy"]?.GetValue<string>());
    Assert.Equal("driver.longhorn.io", replacement["provisioner"]?.GetValue<string>());
    Assert.Equal("WaitForFirstConsumer", replacement["volumeBindingMode"]?.GetValue<string>());
    Assert.True(replacement["allowVolumeExpansion"]?.GetValue<bool>());
    Assert.Equal("2", replacement["parameters"]?["numberOfReplicas"]?.GetValue<string>());
    Assert.Equal("discard", replacement["mountOptions"]?[0]?.GetValue<string>());
    Assert.Equal("storage", replacement["metadata"]?["labels"]?["team"]?.GetValue<string>());
    Assert.Equal("true", replacement["metadata"]?["annotations"]?["storageclass.kubernetes.io/is-default-class"]?.GetValue<string>());
    Assert.Equal("platform", replacement["metadata"]?["annotations"]?["example.com/owner"]?.GetValue<string>());
    Assert.Null(replacement["metadata"]?["annotations"]?["kubectl.kubernetes.io/last-applied-configuration"]);
    Assert.Null(replacement["metadata"]?["resourceVersion"]);
    Assert.Null(replacement["metadata"]?["uid"]);
    Assert.Null(replacement["metadata"]?["generation"]);
    Assert.Null(replacement["metadata"]?["managedFields"]);
    Assert.Null(replacement["metadata"]?["finalizers"]);
    Assert.Null(replacement["metadata"]?["namespace"]);
    Assert.Null(replacement["status"]);
    Assert.True(ReclaimPolicy.IsDefaultClass(original));
    Assert.True(ReclaimPolicy.ClassUsesDelete(original));
  }

  [Fact]
  public void Missing_class_policy_is_delete() {
    var storageClass = JsonNode.Parse("""
      { "metadata": { "name": "local-path" }, "provisioner": "rancher.io/local-path" }
      """) as JsonObject;

    Assert.NotNull(storageClass);
    Assert.Equal("Delete", ReclaimPolicy.ClassPolicy(storageClass));
    Assert.True(ReclaimPolicy.ClassUsesDelete(storageClass));
    Assert.Equal("Retain", ReclaimPolicy.Replacement(storageClass, ReclaimPolicy.Retain)["reclaimPolicy"]?.GetValue<string>());
    Assert.Equal("Delete", ReclaimPolicy.Replacement(storageClass, "delete")["reclaimPolicy"]?.GetValue<string>());
    Assert.Equal("storage.k8s.io/v1", ReclaimPolicy.Replacement(storageClass)["apiVersion"]?.GetValue<string>());
    Assert.Equal("StorageClass", ReclaimPolicy.Replacement(storageClass)["kind"]?.GetValue<string>());
  }

  [Fact]
  public void Preview_marks_volumes_that_differ_from_the_target() {
    var storageClass = JsonNode.Parse("""
      {
        "metadata": { "name": "longhorn" },
        "reclaimPolicy": "Delete"
      }
      """) as JsonObject;
    var delete = Volume("pvc-a", "longhorn", "Delete", "Bound", "apps", "data");
    var retain = Volume("pvc-b", "longhorn", "Retain", "Bound", "apps", "logs");
    var other = Volume("pvc-c", "local-path", "Delete", "Bound", "apps", "other");
    var annotation = JsonNode.Parse("""
      {
        "metadata": {
          "name": "pvc-old",
          "annotations": { "volume.beta.kubernetes.io/storage-class": "longhorn" }
        },
        "spec": { "persistentVolumeReclaimPolicy": "Delete" },
        "status": { "phase": "Released" }
      }
      """) as JsonObject;
    var unset = Volume("pvc-static", "longhorn", null, "Available", null, null);

    Assert.NotNull(storageClass);
    Assert.NotNull(annotation);
    var preview = ReclaimPolicy.PreviewClass(storageClass, [delete, retain, other, annotation, unset], ReclaimPolicy.Retain);

    Assert.Equal(["pvc-a", "pvc-b", "pvc-old", "pvc-static"], preview.Volumes.Select(volume => volume.Name).ToArray());
    Assert.Equal("apps/data", preview.Volumes[0].Claim);
    Assert.Equal("Bound", preview.Volumes[0].Phase);
    Assert.Equal(["pvc-a", "pvc-old"], preview.Volumes.Where(volume => volume.WillChange).Select(volume => volume.Name).ToArray());
    Assert.True(preview.ClassNeedsChange);

    var toDelete = ReclaimPolicy.PreviewClass(storageClass, [delete, retain, other, annotation, unset], ReclaimPolicy.Delete);
    Assert.False(toDelete.ClassNeedsChange);
    Assert.Equal(["pvc-b", "pvc-static"], toDelete.Volumes.Where(volume => volume.WillChange).Select(volume => volume.Name).ToArray());
  }

  [Fact]
  public void Preview_volumes_marks_retain_and_missing() {
    var retain = Volume("keep", "longhorn", "Retain", "Bound", "apps", "data");
    var recycle = Volume("old", "longhorn", "Recycle", "Bound", null, null);
    var preview = ReclaimPolicy.PreviewVolumes([retain, recycle], ["keep", "old", "gone"], ReclaimPolicy.Retain);

    Assert.Equal(3, preview.Volumes.Count);
    Assert.Equal("Not found", preview.Volumes.Single(volume => volume.Name == "gone").Detail);
    Assert.False(preview.Volumes.Single(volume => volume.Name == "keep").WillChange);
    Assert.Contains("(already Retain)", preview.Volumes.Single(volume => volume.Name == "keep").Detail);
    Assert.True(preview.Volumes.Single(volume => volume.Name == "old").WillChange);
    Assert.Equal(["old"], ReclaimPolicy.VolumesToUpdate([retain, recycle], ["keep", "old", "gone"], ReclaimPolicy.Retain).Select(ReclaimPolicy.Name).ToArray());
  }

  [Fact]
  public void Outcome_summary_names_what_changed() {
    var patched = new StorageReclaimOutcome(2, ReclaimPolicy.Retain, true, false, null, []);
    Assert.Equal("Set Retain on 2 volumes. Storage class recreated with Retain.", patched.Summary);

    var already = new StorageReclaimOutcome(0, ReclaimPolicy.Delete, false, true, null, []);
    Assert.Equal("Storage class already uses Delete.", already.Summary);

    var failed = new StorageReclaimOutcome(1, ReclaimPolicy.Retain, false, false, null, ["pvc-a: forbidden"]);
    Assert.Equal("Set Retain on 1 volume. pvc-a: forbidden", failed.Summary);
  }

  private static JsonObject Volume(string name, string storageClass, string? policy, string phase, string? claimNamespace, string? claimName) {
    var spec = new JsonObject { ["storageClassName"] = storageClass };
    if (policy is not null)
      spec["persistentVolumeReclaimPolicy"] = policy;
    if (claimName is not null) {
      var claim = new JsonObject { ["name"] = claimName };
      if (claimNamespace is not null)
        claim["namespace"] = claimNamespace;
      spec["claimRef"] = claim;
    }

    return new JsonObject {
      ["metadata"] = new JsonObject { ["name"] = name },
      ["spec"] = spec,
      ["status"] = new JsonObject { ["phase"] = phase }
    };
  }
}
