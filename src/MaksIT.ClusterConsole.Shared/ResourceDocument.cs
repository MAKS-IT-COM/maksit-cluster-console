using System.Text;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public sealed record ResourceDataEntry(string Key, string Value, bool IsBinary);

public static class ResourceDocument {
  public static bool IsDataKind(string? kind) =>
    kind is "Secret" or "ConfigMap";

  public static JsonObject Clone(JsonObject document) =>
    JsonNode.Parse(document.ToJsonString()) as JsonObject ?? [];

  public static JsonObject PrepareForEdit(JsonObject document) {
    var clone = Clone(document);
    clone.Remove("status");
    if (clone["metadata"] is JsonObject meta)
      meta.Remove("managedFields");
    return clone;
  }

  public static JsonObject PrepareForApply(JsonObject document) {
    var clone = PrepareForEdit(document);
    if (clone["metadata"] is JsonObject meta) {
      meta.Remove("generation");
      meta.Remove("creationTimestamp");
      meta.Remove("deletionTimestamp");
      meta.Remove("selfLink");
    }

    return clone;
  }

  public static IReadOnlyList<ResourceDataEntry> ReadDataEntries(JsonObject document) {
    var kind = document["kind"]?.GetValue<string>();
    var entries = new List<ResourceDataEntry>();
    if (kind == "Secret") {
      AddMap(entries, document["stringData"] as JsonObject, encoded: false);
      AddMap(entries, document["data"] as JsonObject, encoded: true);
    }
    else {
      AddMap(entries, document["data"] as JsonObject, encoded: false);
      AddMap(entries, document["binaryData"] as JsonObject, encoded: true);
    }

    return entries;
  }

  public static void WriteDataEntries(JsonObject document, IEnumerable<ResourceDataEntry> entries) {
    var kind = document["kind"]?.GetValue<string>();
    var text = new JsonObject();
    var binary = new JsonObject();
    foreach (var entry in entries) {
      if (string.IsNullOrWhiteSpace(entry.Key))
        continue;

      if (entry.IsBinary)
        binary[entry.Key] = NormalizeBase64(entry.Value);
      else
        text[entry.Key] = entry.Value;
    }

    if (kind == "Secret") {
      SetOrRemove(document, "stringData", text);
      SetOrRemove(document, "data", binary);
    }
    else {
      SetOrRemove(document, "data", text);
      SetOrRemove(document, "binaryData", binary);
    }
  }

  public static string NewTemplate(ResourceDescriptor descriptor, string? @namespace) {
    var apiVersion = string.IsNullOrEmpty(descriptor.Group)
      ? descriptor.Version
      : $"{descriptor.Group}/{descriptor.Version}";
    var ns = string.IsNullOrWhiteSpace(@namespace) || @namespace == "all" ? "default" : @namespace;
    var meta = new JsonObject { ["name"] = $"new-{descriptor.Id}" };
    if (descriptor.Namespaced)
      meta["namespace"] = ns;

    var doc = new JsonObject {
      ["apiVersion"] = apiVersion,
      ["kind"] = descriptor.Kind,
      ["metadata"] = meta
    };

    if (descriptor.Id == "configmaps")
      doc["data"] = new JsonObject { ["key"] = "value" };
    else if (descriptor.Id == "secrets") {
      doc["type"] = "Opaque";
      doc["stringData"] = new JsonObject { ["key"] = "value" };
    }

    return YamlFormatter.FromJson(doc);
  }

  private static void AddMap(List<ResourceDataEntry> entries, JsonObject? map, bool encoded) {
    if (map is null)
      return;

    foreach (var prop in map) {
      if (entries.Any(e => e.Key == prop.Key))
        continue;

      var raw = prop.Value?.GetValue<string>() ?? prop.Value?.ToJsonString() ?? "";
      if (!encoded) {
        entries.Add(new ResourceDataEntry(prop.Key, raw, false));
        continue;
      }

      if (TryDecodeUtf8(raw, out var text))
        entries.Add(new ResourceDataEntry(prop.Key, text, false));
      else
        entries.Add(new ResourceDataEntry(prop.Key, raw, true));
    }
  }

  private static void SetOrRemove(JsonObject document, string name, JsonObject map) {
    if (map.Count == 0)
      document.Remove(name);
    else
      document[name] = map;
  }

  private static string NormalizeBase64(string value) {
    if (TryDecodeUtf8(value, out _))
      return value.Replace("\r", "").Replace("\n", "");
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
  }

  private static bool TryDecodeUtf8(string base64, out string text) {
    text = "";
    try {
      var bytes = Convert.FromBase64String(base64.Trim());
      if (bytes.Length == 0) {
        text = "";
        return true;
      }

      if (bytes.Contains((byte)0))
        return false;

      text = Encoding.UTF8.GetString(bytes);
      var roundTrip = Encoding.UTF8.GetBytes(text);
      return roundTrip.AsSpan().SequenceEqual(bytes) && !text.Contains('\uFFFD');
    }
    catch (FormatException) {
      return false;
    }
  }
}
