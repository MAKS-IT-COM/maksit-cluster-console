using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using k8s;


namespace MaksIT.ClusterConsole.Shared;

public static class YamlFormatter {
  public static string FromJson(JsonNode? node, int indent = 0) {
    var sb = new StringBuilder();
    Write(sb, node, indent, isRoot: true);
    return sb.ToString().TrimEnd() + Environment.NewLine;
  }

  public static JsonObject? ToJsonObject(string yaml) {
    if (string.IsNullOrWhiteSpace(yaml))
      return null;

    var trimmed = yaml.TrimStart();
    if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
      return JsonNode.Parse(yaml) as JsonObject;

    return ParseSimpleYaml(yaml);
  }

  private static void Write(StringBuilder sb, JsonNode? node, int indent, bool isRoot) {
    var pad = new string(' ', indent);
    switch (node) {
      case null:
        sb.AppendLine("null");
        break;
      case JsonValue value:
        sb.AppendLine(FormatScalar(value));
        break;
      case JsonArray array:
        if (array.Count == 0) {
          sb.AppendLine("[]");
          break;
        }

        foreach (var item in array) {
          sb.Append(pad).Append("- ");
          if (item is JsonObject or JsonArray) {
            sb.AppendLine();
            Write(sb, item, indent + 2, false);
          }
          else {
            Write(sb, item, 0, false);
          }
        }

        break;
      case JsonObject obj:
        if (!isRoot && indent == 0)
          sb.AppendLine();

        foreach (var prop in obj) {
          sb.Append(pad).Append(prop.Key).Append(':');
          if (prop.Value is JsonObject or JsonArray) {
            sb.AppendLine();
            Write(sb, prop.Value, indent + 2, false);
          }
          else {
            sb.Append(' ');
            Write(sb, prop.Value, 0, false);
          }
        }

        break;
    }
  }

  private static string FormatScalar(JsonValue value) {
    if (value.TryGetValue<bool>(out var b))
      return b ? "true" : "false";
    if (value.TryGetValue<long>(out var l))
      return l.ToString();
    if (value.TryGetValue<double>(out var d))
      return d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    var s = value.ToString();
    if (s.Contains(':') || s.Contains('#') || s.Contains('\n') || s.Length == 0)
      return $"\"{s.Replace("\"", "\\\"")}\"";
    return s;
  }

  private static JsonObject ParseSimpleYaml(string yaml) {
    var deserialized = KubernetesYaml.Deserialize<object>(yaml);
    if (deserialized is null)
      return new JsonObject();

    var json = JsonSerializer.Serialize(deserialized);
    return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
  }
}
