using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;


namespace MaksIT.ClusterConsole.Client;

public sealed class OllamaChatRequest {
  [JsonPropertyName("model")]
  public required string Model { get; set; }

  [JsonPropertyName("messages")]
  public required IReadOnlyList<OllamaChatMessage> Messages { get; set; }

  [JsonPropertyName("tools")]
  public IReadOnlyList<OllamaTool>? Tools { get; set; }

  [JsonPropertyName("stream")]
  public bool Stream { get; set; }

  [JsonPropertyName("think")]
  public bool Think { get; set; }

  [JsonPropertyName("keep_alive")]
  public string KeepAlive { get; set; } = "5m";

  [JsonPropertyName("options")]
  public OllamaChatOptions? Options { get; set; }
}

public sealed class OllamaChatOptions {
  [JsonPropertyName("temperature")]
  public double Temperature { get; set; } = 0.2;

  [JsonPropertyName("num_ctx")]
  public int NumCtx { get; set; } = 8192;
}

public sealed class OllamaChatMessage {
  [JsonPropertyName("role")]
  public required string Role { get; set; }

  [JsonPropertyName("content")]
  public string Content { get; set; } = "";

  [JsonPropertyName("tool_name")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ToolName { get; set; }

  [JsonPropertyName("tool_calls")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<OllamaToolCall>? ToolCalls { get; set; }
}

public sealed class OllamaChatResponse {
  [JsonPropertyName("message")]
  public OllamaChatMessage? Message { get; set; }

  [JsonPropertyName("error")]
  public string? Error { get; set; }
}

public sealed class OllamaTool {
  [JsonPropertyName("type")]
  public string Type { get; set; } = "function";

  [JsonPropertyName("function")]
  public required OllamaToolFunction Function { get; set; }
}

public sealed class OllamaToolFunction {
  [JsonPropertyName("name")]
  public required string Name { get; set; }

  [JsonPropertyName("description")]
  public required string Description { get; set; }

  [JsonPropertyName("parameters")]
  public required JsonObject Parameters { get; set; }
}

public sealed class OllamaToolCall {
  [JsonPropertyName("function")]
  public OllamaToolCallFunction? Function { get; set; }
}

public sealed class OllamaToolCallFunction {
  [JsonPropertyName("name")]
  public string? Name { get; set; }

  [JsonPropertyName("arguments")]
  public JsonElement Arguments { get; set; }
}

public sealed class OllamaTagsResponse {
  [JsonPropertyName("models")]
  public List<OllamaTagModel> Models { get; set; } = [];
}

public sealed class OllamaTagModel {
  [JsonPropertyName("name")]
  public string? Name { get; set; }
}
