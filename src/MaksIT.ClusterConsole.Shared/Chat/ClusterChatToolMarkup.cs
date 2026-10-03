using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.Shared.Chat;

public static partial class ClusterChatToolMarkup {
  public static IReadOnlyList<OllamaToolCall> Parse(string? content) {
    if (string.IsNullOrWhiteSpace(content))
      return [];

    var calls = new List<OllamaToolCall>();

    foreach (Match function in FunctionBlock().Matches(content)) {
      var args = new JsonObject();

      foreach (Match parameter in ParameterBlock().Matches(function.Groups[2].Value))
        args[parameter.Groups[1].Value] = parameter.Groups[2].Value.Trim();

      calls.Add(new OllamaToolCall {
        Function = new OllamaToolCallFunction {
          Name = function.Groups[1].Value,
          Arguments = JsonSerializer.SerializeToElement(args)
        }
      });
    }

    return calls;
  }

  public static string Strip(string? content) {
    if (string.IsNullOrEmpty(content))
      return "";

    var text = FunctionBlock().Replace(content, "");
    text = ToolCallTag().Replace(text, "");

    return text.Trim();
  }

  [GeneratedRegex(@"<function=([A-Za-z0-9_\-]+)>\s*(.*?)\s*</function>", RegexOptions.Singleline)]
  private static partial Regex FunctionBlock();

  [GeneratedRegex(@"<parameter=([A-Za-z0-9_\-]+)>\s*(.*?)\s*</parameter>", RegexOptions.Singleline)]
  private static partial Regex ParameterBlock();

  [GeneratedRegex(@"</?tool_call>", RegexOptions.IgnoreCase)]
  private static partial Regex ToolCallTag();
}
