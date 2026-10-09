using System.Net;
using System.Text.Json;
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

  [Theory]
  [InlineData("403 Forbidden", "403 Forbidden")]
  [InlineData("no", "no")]
  public void Map_keeps_the_api_message_for_auth_and_generic_failures(string message, string expected) {
    var mapped = message.Contains("403", StringComparison.Ordinal)
      ? KubernetesResult.Map(new Exception(message))
      : KubernetesResult.Map(new InvalidOperationException(message));

    Assert.Equal(expected, mapped.Messages[0]);
    Assert.False(mapped.IsSuccess);
  }

  [Fact]
  public void Map_classifies_status_codes_and_typed_results() {
    Assert.Equal(HttpStatusCode.Unauthorized, KubernetesResult.Map(new UnauthorizedAccessException("denied")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, KubernetesResult.Map(new Exception("403 Forbidden")).StatusCode);

    var missing = KubernetesResult.Map<string>(new Exception("404 missing"));
    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    Assert.Null(missing.Value);
    Assert.Equal("404 missing", missing.Messages[0]);

    Assert.Equal(HttpStatusCode.Conflict, KubernetesResult.Map(new Exception("409 Conflict")).StatusCode);
    Assert.Equal(HttpStatusCode.UnprocessableEntity, KubernetesResult.Map(new Exception("422 invalid")).StatusCode);
    Assert.Equal(HttpStatusCode.InternalServerError, KubernetesResult.Map(new Exception("boom")).StatusCode);
  }

  [Fact]
  public void ToObject_and_items_accept_objects_elements_and_single_documents() {
    Assert.Null(KubernetesResult.ToObject(null));
    var passthrough = new JsonObject { ["kind"] = "Pod" };
    Assert.Same(passthrough, KubernetesResult.ToObject(passthrough));

    using var document = JsonDocument.Parse("""{"metadata":{"name":"from-element","resourceVersion":"12"}}""");
    var fromElement = KubernetesResult.ToObject(document.RootElement);
    Assert.Equal("from-element", fromElement!["metadata"]!["name"]!.GetValue<string>());
    Assert.Equal("12", KubernetesResult.ResourceVersion(fromElement));

    var fromAnonymous = KubernetesResult.ToObject(new { metadata = new { name = "anon" } });
    Assert.Equal("anon", fromAnonymous!["metadata"]!["name"]!.GetValue<string>());

    Assert.Empty(KubernetesResult.Items(null));
    Assert.Equal("solo", KubernetesResult.Items("""{"metadata":{"name":"solo"}}""")[0]["metadata"]!["name"]!.GetValue<string>());

    var blank = JsonNode.Parse("""{"metadata":{"continue":"  "}}""") as JsonObject;
    Assert.Null(KubernetesResult.ContinueToken(blank));
    var typed = JsonNode.Parse("""{"metadata":{"Continue":"next"}}""") as JsonObject;
    Assert.Equal("next", KubernetesResult.ContinueToken(typed));
  }
}
