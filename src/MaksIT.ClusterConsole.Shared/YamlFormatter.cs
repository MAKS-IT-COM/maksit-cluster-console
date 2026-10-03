using System.Globalization;
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
      return l.ToString(CultureInfo.InvariantCulture);

    if (value.TryGetValue<double>(out var d))
      return d.ToString(CultureInfo.InvariantCulture);

    if (value.TryGetValue<string>(out var text))
      return Quote(text ?? "");

    return Quote(value.ToString());
  }

  private static string Quote(string text) {
    if (!NeedsQuote(text))
      return text;

    var escaped = new StringBuilder(text.Length + 2);
    escaped.Append('"');

    foreach (var c in text) {
      switch (c) {
        case '\\':
          escaped.Append("\\\\");

          break;
        case '"':
          escaped.Append("\\\"");

          break;
        case '\n':
          escaped.Append("\\n");

          break;
        case '\r':
          escaped.Append("\\r");

          break;
        case '\t':
          escaped.Append("\\t");

          break;
        default:
          escaped.Append(c);

          break;
      }
    }

    escaped.Append('"');

    return escaped.ToString();
  }

  private static bool NeedsQuote(string text) {
    if (text.Length == 0)
      return true;

    if (text is "true" or "false" or "null" or "yes" or "no" or "~")
      return true;

    if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
      return true;

    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
      return true;

    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        && text.Contains('.') && !text.Contains(".."))
      return true;

    foreach (var c in text) {
      if (c is ':' or '#' or '\n' or '\r' or '\t' or '"' or '\\'
          or '{' or '}' or '[' or ']' or ',' or '&' or '*' or '!' or '|' or '>' or '%' or '@' or '`' or '\'')
        return true;
    }

    return false;
  }

  private static JsonObject ParseSimpleYaml(string yaml) {
    var deserialized = KubernetesYaml.Deserialize<object>(yaml);

    if (deserialized is null)
      return new JsonObject();

    var json = JsonSerializer.Serialize(deserialized);

    return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
  }
}
