using System.Text.Json;
using System.Text.Json.Nodes;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

internal static class KubernetesResult {
  public static Result Map(Exception ex) {
    var message = ex.Message;
    var text = message + " " + ex.GetType().Name;

    if (text.Contains("403") || text.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
      return Result.Forbidden(message);

    if (text.Contains("401") || text.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
      return Result.Unauthorized(message);

    if (text.Contains("404") || text.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
      return Result.NotFound(message);

    if (text.Contains("409") || text.Contains("Conflict", StringComparison.OrdinalIgnoreCase))
      return Result.Conflict(message);

    if (text.Contains("422"))
      return Result.UnprocessableEntity(message);

    return Result.InternalServerError(message);
  }

  public static Result<T> Map<T>(Exception ex) {
    var mapped = Map(ex);
    return new Result<T>(default, mapped.IsSuccess, mapped.Messages, mapped.StatusCode);
  }

  public static JsonObject? ToObject(object? raw) {
    if (raw is null)
      return null;

    if (raw is JsonObject jsonObject)
      return jsonObject;

    if (raw is string text)
      return JsonNode.Parse(text) as JsonObject;

    if (raw is JsonElement element)
      return JsonNode.Parse(element.GetRawText()) as JsonObject;

    var json = JsonSerializer.Serialize(raw);
    return JsonNode.Parse(json) as JsonObject;
  }

  public static IReadOnlyList<JsonObject> Items(object? raw) {
    var root = ToObject(raw);
    if (root is null)
      return [];

    var items = ArrayProperty(root, "items");
    if (items is not null)
      return items.OfType<JsonObject>().ToList();

    return [root];
  }

  public static string? ContinueToken(JsonObject? root) {
    var node = Property(root?["metadata"] as JsonObject, "continue");
    if (node is null)
      return null;

    var text = node is JsonValue value && value.TryGetValue<string>(out var typed)
      ? typed
      : node.ToString();
    return string.IsNullOrWhiteSpace(text) ? null : text;
  }

  private static JsonArray? ArrayProperty(JsonObject root, string name) {
    if (Property(root, name) is JsonArray items)
      return items;
    return null;
  }

  private static JsonNode? Property(JsonObject? root, string name) {
    if (root is null)
      return null;
    if (root[name] is { } exact)
      return exact;

    foreach (var kv in root) {
      if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
        return kv.Value;
    }

    return null;
  }
}
