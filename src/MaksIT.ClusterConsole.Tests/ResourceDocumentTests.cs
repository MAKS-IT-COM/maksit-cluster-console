using System.Text;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ResourceDocumentTests {
  [Fact]
  public void NewTemplate_seeds_namespaced_data_and_cluster_scope() {
    var config = ResourceDocument.NewTemplate(ResourceCatalog.Find("configmaps")!, "all");
    Assert.Contains("namespace: default", config, StringComparison.Ordinal);
    Assert.Contains("key: value", config, StringComparison.Ordinal);

    var secret = ResourceDocument.NewTemplate(ResourceCatalog.Find("secrets")!, "vault");
    Assert.Contains("namespace: vault", secret, StringComparison.Ordinal);
    Assert.Contains("Opaque", secret, StringComparison.Ordinal);
    Assert.Contains("stringData:", secret, StringComparison.Ordinal);

    var node = ResourceDocument.NewTemplate(ResourceCatalog.Find("nodes")!, "kube-system");
    Assert.DoesNotContain("namespace:", node, StringComparison.Ordinal);
    Assert.Contains("kind: Node", node, StringComparison.Ordinal);
  }

  [Fact]
  public void PrepareForApply_drops_server_managed_metadata() {
    var prepared = ResourceDocument.PrepareForApply(Doc("""
      {
        "kind": "ConfigMap",
        "metadata": {
          "name": "app",
          "generation": 3,
          "creationTimestamp": "2026-01-01T00:00:00Z",
          "deletionTimestamp": "2026-01-02T00:00:00Z",
          "selfLink": "/api/v1/configmaps/app",
          "managedFields": [{ "manager": "kubectl" }]
        },
        "status": { "phase": "Ready" }
      }
      """));

    var meta = prepared["metadata"] as JsonObject;
    Assert.Equal("app", meta!["name"]!.GetValue<string>());
    Assert.Null(meta["generation"]);
    Assert.Null(meta["managedFields"]);
    Assert.Null(prepared["status"]);
  }

  [Fact]
  public void Data_entries_prefer_text_skip_duplicates_and_keep_binary() {
    var secret = Doc("""
      {
        "kind": "Secret",
        "stringData": { "password": "plain" },
        "data": {
          "password": "aWdub3JlZA==",
          "token": "dG9rZW4=",
          "blob": "AAEC"
        }
      }
      """);
    var entries = ResourceDocument.ReadDataEntries(secret);
    Assert.Equal("plain", entries.Single(entry => entry.Key == "password").Value);
    Assert.False(entries.Single(entry => entry.Key == "password").IsBinary);
    Assert.Equal("token", entries.Single(entry => entry.Key == "token").Value);
    Assert.True(entries.Single(entry => entry.Key == "blob").IsBinary);

    var config = Doc("""{ "kind": "ConfigMap", "data": { "note": { "nested": true } }, "binaryData": {} }""");
    var nested = Assert.Single(ResourceDocument.ReadDataEntries(config));
    Assert.Contains("nested", nested.Value, StringComparison.Ordinal);

    ResourceDocument.WriteDataEntries(config, [
      new ResourceDataEntry("", "skip", false),
      new ResourceDataEntry("note", "hello", false),
      new ResourceDataEntry("blob", "not-base64", true)
    ]);
    Assert.Equal("hello", config["data"]!["note"]!.GetValue<string>());
    Assert.Null(config["data"]![""]);
    Assert.Equal(
      Convert.ToBase64String(Encoding.UTF8.GetBytes("not-base64")),
      config["binaryData"]!["blob"]!.GetValue<string>());
  }

  private static JsonObject Doc(string json) =>
    JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("expected object");
}
