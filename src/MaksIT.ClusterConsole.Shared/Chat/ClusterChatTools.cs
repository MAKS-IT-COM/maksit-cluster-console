using System.Text.Json;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Shared;

public sealed class ClusterChatTools(ClusterWorkspace workspace) {
  public const int MaxResultChars = 12_000;

  public IReadOnlyList<OllamaTool> Definitions { get; } = [
    Tool(
      "get_cluster_issues",
      "List cluster warning and error issues from nodes, pods, and events. Each line includes Active or Resolved.",
      new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
    Tool(
      "get_resource",
      "Get a Kubernetes object as YAML. Kind can be Pod, Deployment, Service, etc.",
      ObjectSchema(
        ("kind", "Resource kind, plural, or catalog id, e.g. Pod or deployments", true),
        ("name", "Object name", true),
        ("namespace", "Namespace. Omit for cluster-scoped objects or to use the UI namespace.", false))),
    Tool(
      "get_logs",
      "Read recent logs from a pod container. Always pass container when the pod has sidecars.",
      ObjectSchema(
        ("pod", "Pod name. Defaults to the UI selection.", false),
        ("namespace", "Pod namespace. Defaults to the UI selection.", false),
        ("container", "Container name. Required when the pod has more than one container.", false),
        ("tailLines", "Number of log lines to return (1-200). Default 80.", false))),
    Tool(
      "get_events",
      "List Kubernetes events for an object name.",
      ObjectSchema(
        ("name", "Object name. Defaults to the UI selection.", false),
        ("namespace", "Namespace. Defaults to the UI selection.", false)))
  ];

  public async Task<string> InvokeAsync(
    string name,
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    var result = name switch {
      "get_cluster_issues" => await GetIssuesAsync(cancellationToken).ConfigureAwait(false),
      "get_resource" => await GetResourceAsync(args, context, cancellationToken).ConfigureAwait(false),
      "get_logs" => await GetLogsAsync(args, context, cancellationToken).ConfigureAwait(false),
      "get_events" => await GetEventsAsync(args, context, cancellationToken).ConfigureAwait(false),
      _ => $"Unknown tool '{name}'. Available: get_cluster_issues, get_resource, get_logs, get_events."
    };
    return Truncate(result);
  }

  public static JsonObject ParseArguments(JsonElement arguments) {
    if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
      return [];

    if (arguments.ValueKind == JsonValueKind.String) {
      var text = arguments.GetString();
      return string.IsNullOrWhiteSpace(text)
        ? []
        : JsonNode.Parse(text) as JsonObject ?? [];
    }

    if (arguments.ValueKind == JsonValueKind.Object)
      return JsonNode.Parse(arguments.GetRawText()) as JsonObject ?? [];

    return [];
  }

  private async Task<string> GetIssuesAsync(CancellationToken cancellationToken) {
    var issues = await workspace.GetClusterIssuesAsync(cancellationToken).ConfigureAwait(false);
    if (!issues.IsSuccess || issues.Value is null)
      return JoinMessages(issues.Messages);

    var lines = new List<string>();
    foreach (var error in issues.Value.Errors.Take(25))
      lines.Add($"ERROR {error.State} {error.Kind}/{error.ObjectName}: {error.Message} ({error.Age})");
    foreach (var warning in issues.Value.Warnings.Take(25))
      lines.Add($"WARN {warning.State} {warning.Kind}/{warning.ObjectName}: {warning.Message} ({warning.Age})");

    return lines.Count == 0
      ? "No cluster warnings or errors were found."
      : string.Join(Environment.NewLine, lines);
  }

  private async Task<string> GetResourceAsync(
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var kind = Arg(args, "kind") ?? context.Kind;
    var name = Arg(args, "name") ?? context.Name;
    if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
      return "get_resource needs kind and name.";

    var descriptor = Resolve(kind);
    if (descriptor is null)
      return $"Unknown kind '{kind}'.";

    var ns = NamespaceArg(args, context, descriptor.Namespaced);
    var got = await workspace.Session.GetAsync(descriptor.ToRef(), name, ns, cancellationToken).ConfigureAwait(false);
    if (!got.IsSuccess || got.Value is null)
      return JoinMessages(got.Messages);

    return YamlFormatter.FromJson(got.Value);
  }

  private async Task<string> GetLogsAsync(
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var pod = Arg(args, "pod") ?? context.Pod ?? (IsPod(context.Kind) ? context.Name : null);
    var ns = Arg(args, "namespace") ?? context.Namespace;
    if (string.IsNullOrWhiteSpace(pod))
      return "get_logs needs a pod name. Select a pod in the UI or pass pod.";

    if (string.IsNullOrWhiteSpace(ns) || ns == Configuration.AllNamespaces)
      ns = "default";

    var container = Arg(args, "container") ?? context.Container;
    var tail = IntArg(args, "tailLines", 80, 1, 200);
    var logs = await workspace.Session.GetLogsAsync(pod, ns, container, false, tail, cancellationToken)
      .ConfigureAwait(false);
    if (!logs.IsSuccess)
      return JoinMessages(logs.Messages);

    var text = logs.Value ?? "";
    return string.IsNullOrWhiteSpace(text)
      ? $"No log lines for {pod}/{container ?? "(default container)"}."
      : text;
  }

  private async Task<string> GetEventsAsync(
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var name = Arg(args, "name") ?? context.Name;
    if (string.IsNullOrWhiteSpace(name))
      return "get_events needs an object name.";

    var ns = Arg(args, "namespace") ?? context.Namespace;
    var events = ResourceCatalog.Find("events")!;
    var listed = await workspace.Session.ListAsync(
      events.ToRef(),
      ns == Configuration.AllNamespaces ? null : ns,
      cancellationToken).ConfigureAwait(false);
    if (!listed.IsSuccess)
      return JoinMessages(listed.Messages);

    var matches = (listed.Value ?? [])
      .Where(e => e["involvedObject"]?["name"]?.GetValue<string>() == name)
      .Take(30)
      .Select(e => {
        var type = e["type"]?.ToString();
        var reason = e["reason"]?.ToString();
        var message = e["message"]?.ToString();
        return $"{type} {reason}: {message}";
      })
      .ToList();

    return matches.Count == 0
      ? $"No events found for {name}."
      : string.Join(Environment.NewLine, matches);
  }

  private ResourceDescriptor? Resolve(string kind) {
    var direct = workspace.FindDescriptor(kind) ?? ResourceCatalog.Find(kind);
    if (direct is not null)
      return direct;

    return ResourceCatalog.BuiltIns.FirstOrDefault(d =>
        d.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
        || d.Plural.Equals(kind, StringComparison.OrdinalIgnoreCase)
        || d.Title.Equals(kind, StringComparison.OrdinalIgnoreCase))
      ?? workspace.FindByGvk(null, kind);
  }

  private static OllamaTool Tool(string name, string description, JsonObject parameters) =>
    new() {
      Function = new OllamaToolFunction {
        Name = name,
        Description = description,
        Parameters = parameters
      }
    };

  private static JsonObject ObjectSchema(params (string Name, string Description, bool Required)[] fields) {
    var properties = new JsonObject();
    var required = new JsonArray();
    foreach (var field in fields) {
      properties[field.Name] = new JsonObject {
        ["type"] = field.Name == "tailLines" ? "integer" : "string",
        ["description"] = field.Description
      };
      if (field.Required)
        required.Add(field.Name);
    }

    var schema = new JsonObject {
      ["type"] = "object",
      ["properties"] = properties
    };
    if (required.Count > 0)
      schema["required"] = required;

    return schema;
  }

  private static string? Arg(JsonObject args, string key) {
    var node = args[key];
    if (node is null)
      return null;

    var text = node is JsonValue value && value.TryGetValue<string>(out var typed)
      ? typed
      : node.ToString();
    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
  }

  private static int IntArg(JsonObject args, string key, int fallback, int min, int max) {
    var node = args[key];
    if (node is JsonValue value) {
      if (value.TryGetValue<int>(out var number))
        return Math.Clamp(number, min, max);
      if (value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
        return Math.Clamp(parsed, min, max);
    }

    return fallback;
  }

  private static string? NamespaceArg(JsonObject args, ClusterChatContext context, bool namespaced) {
    if (!namespaced)
      return null;

    var ns = Arg(args, "namespace") ?? context.Namespace;
    if (string.IsNullOrWhiteSpace(ns) || ns == Configuration.AllNamespaces)
      return "default";

    return ns;
  }

  private static bool IsPod(string? kind) =>
    string.Equals(kind, "Pod", StringComparison.OrdinalIgnoreCase);

  private static string JoinMessages(IEnumerable<string> messages) =>
    string.Join("; ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));

  private static string Truncate(string text) {
    if (string.IsNullOrEmpty(text) || text.Length <= MaxResultChars)
      return text;

    return text[..MaxResultChars] + $"{Environment.NewLine}… truncated at {MaxResultChars} characters.";
  }
}
