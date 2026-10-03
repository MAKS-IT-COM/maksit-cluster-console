using System.Net;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client.Internal;


namespace MaksIT.ClusterConsole.Tests;

public class KubernetesResultTests {
  [Fact]
  public void Items_reads_camel_case_list() {
    var raw = """{"items":[{"metadata":{"name":"maksit-cicd-build-a"}}]}""";
    var items = KubernetesResult.Items(raw);
    Assert.Equal("maksit-cicd-build-a", items[0]["metadata"]?["name"]?.GetValue<string>());
  }

  [Fact]
  public void Items_reads_pascal_case_list() {
    var raw = """{"Items":[{"metadata":{"name":"maksit-cicd-build-b"}}]}""";
    var items = KubernetesResult.Items(raw);
    Assert.Equal("maksit-cicd-build-b", items[0]["metadata"]?["name"]?.GetValue<string>());
  }

  [Fact]
  public void ContinueToken_reads_reserved_continue_field() {
    var root = JsonNode.Parse("""{"metadata":{"continue":"token-1"}}""") as JsonObject;
    Assert.Equal("token-1", KubernetesResult.ContinueToken(root));
  }

  [Fact]
  public void Map_transient_http_errors_are_service_unavailable() {
    var mapped = KubernetesResult.Map(new HttpRequestException("The response ended prematurely while waiting for the next frame from the server. (ResponseEnded)"));
    Assert.False(mapped.IsSuccess);
    Assert.Contains("connection dropped", mapped.Messages[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Map_storage_initializing_is_a_short_retry_message() {
    var ex = new HttpRequestException(
      "Operation returned an invalid status code 'TooManyRequests', response body {\"message\":\"storage is (re)initializing\",\"reason\":\"TooManyRequests\",\"details\":{\"retryAfterSeconds\":1},\"code\":429}");
    var mapped = KubernetesResult.Map(ex);

    Assert.False(mapped.IsSuccess);
    Assert.Contains("still preparing", mapped.Messages[0], StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("response body", mapped.Messages[0], StringComparison.OrdinalIgnoreCase);
  }
}
