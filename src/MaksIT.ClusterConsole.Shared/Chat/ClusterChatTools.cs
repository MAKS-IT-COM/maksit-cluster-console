using System.Text.Json;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Ollama;


namespace MaksIT.ClusterConsole.Shared.Chat;

public sealed class ClusterChatTools(ClusterWorkspace workspace) {
  public const int MaxResultChars = 12_000;
  public const string RejectedByOperator =
    "Rejected by the operator. The cluster was not changed. Propose a different fix or explain the diagnosis.";

  public IReadOnlyList<OllamaTool> ReadOnlyDefinitions { get; } = [
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

  private IReadOnlyList<OllamaTool>? _agentDefinitions;

  public IReadOnlyList<OllamaTool> AgentDefinitions => _agentDefinitions ??= [
    ..ReadOnlyDefinitions,
    Tool(
      "restart_workload",
      "Restart a Deployment, StatefulSet, or DaemonSet by rolling its pods.",
      ObjectSchema(
        ("kind", "Deployment, StatefulSet, or DaemonSet", true),
        ("name", "Workload name", true),
        ("namespace", "Namespace. Defaults to the UI selection.", false))),
    Tool(
      "delete_pod",
      "Delete one Pod so its controller can recreate it. Never force-deletes.",
      ObjectSchema(
        ("name", "Pod name", true),
        ("namespace", "Pod namespace. Defaults to the UI selection.", false))),
    Tool(
      "scale_workload",
      "Set replica count. replicas must be an integer from 0 to 100.",
      ScaleSchema()),
    Tool(
      "apply_manifest",
      "Apply one YAML manifest. Status and server metadata are stripped before apply.",
      ObjectSchema(
        ("yaml", "Full YAML document for one object", true)))
  ];

  public static bool IsMutating(string name) =>
    name is "restart_workload" or "delete_pod" or "scale_workload" or "apply_manifest";

  public static JsonObject? PrepareManifest(string yaml) {
    var document = YamlFormatter.ToJsonObject(yaml);

    return document is null ? null : ResourceDocument.PrepareForApply(document);
  }

  public string Describe(string name, JsonObject args) {
    var kind = Arg(args, "kind") ?? "resource";
    var resourceName = Arg(args, "name") ?? "(unnamed)";
    var ns = Arg(args, "namespace");
    var where = string.IsNullOrWhiteSpace(ns) ? "" : $" in {ns}";

    return name switch {
      "restart_workload" => $"Restart {kind}/{resourceName}{where}.",
      "delete_pod" => $"Delete Pod/{resourceName}{where}. The controller can recreate it.",
      "scale_workload" => $"Scale {kind}/{resourceName}{where} to {Arg(args, "replicas") ?? "?"} replicas.",
      "apply_manifest" => DescribeManifest(args),
      _ => name
    };
  }

  public string ChangePreview(string name, JsonObject args) {
    if (name == "scale_workload" && TryReplicas(args, out var replicas, out _))
      return $"replicas: {replicas}";

    if (name != "apply_manifest")
      return "";

    var yaml = Arg(args, "yaml");

    if (string.IsNullOrWhiteSpace(yaml))
      return "No YAML was provided. Nothing will be applied.";

    var prepared = PrepareManifest(yaml);

    if (prepared is null)
      return "The YAML could not be parsed. Nothing will be applied.";

    var text = YamlFormatter.FromJson(prepared);
    const int max = 6000;

    return text.Length <= max
      ? text
      : text[..max] + $"{Environment.NewLine}… truncated";
  }

  public async Task<string> InvokeAsync(
    string name,
    JsonObject args,
    ClusterChatContext context,
    bool approved,
    CancellationToken cancellationToken) {
    if (IsMutating(name) && !approved)
      return RejectedByOperator;

    var result = name switch {
      "get_cluster_issues" => await GetIssuesAsync(cancellationToken).ConfigureAwait(false),
      "get_resource" => await GetResourceAsync(args, context, cancellationToken).ConfigureAwait(false),
      "get_logs" => await GetLogsAsync(args, context, cancellationToken).ConfigureAwait(false),
      "get_events" => await GetEventsAsync(args, context, cancellationToken).ConfigureAwait(false),
      "restart_workload" => await RestartWorkloadAsync(args, context, cancellationToken).ConfigureAwait(false),
      "delete_pod" => await DeletePodAsync(args, context, cancellationToken).ConfigureAwait(false),
      "scale_workload" => await ScaleWorkloadAsync(args, context, cancellationToken).ConfigureAwait(false),
      "apply_manifest" => await ApplyManifestAsync(args, cancellationToken).ConfigureAwait(false),
      _ => $"Unknown tool '{name}'."
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

  private async Task<string> RestartWorkloadAsync(
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var kind = Arg(args, "kind") ?? context.Kind;
    var name = Arg(args, "name") ?? context.Name;

    if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
      return "restart_workload needs kind and name.";

    var descriptor = Resolve(kind);

    if (descriptor is null || !descriptor.Actions.CanRestart)
      return "restart_workload only supports Deployment, StatefulSet, and DaemonSet.";

    var ns = NamespaceArg(args, context, descriptor.Namespaced);
    var restarted = await workspace.Session.RestartAsync(descriptor.ToRef(), name, ns, cancellationToken)
      .ConfigureAwait(false);

    return restarted.IsSuccess
      ? $"Restarted {descriptor.Kind}/{name}."
      : JoinMessages(restarted.Messages);
  }

  private async Task<string> DeletePodAsync(
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var name = Arg(args, "name") ?? (IsPod(context.Kind) ? context.Name : context.Pod);

    if (string.IsNullOrWhiteSpace(name))
      return "delete_pod needs a pod name.";

    var pods = ResourceCatalog.Find("pods");

    if (pods is null)
      return "Pod catalog entry is missing.";

    var ns = NamespaceArg(args, context, true);
    var deleted = await workspace.Session.DeleteAsync(pods.ToRef(), name, ns, force: false, cancellationToken)
      .ConfigureAwait(false);

    return deleted.IsSuccess
      ? $"Deleted Pod/{name}."
      : JoinMessages(deleted.Messages);
  }

  private async Task<string> ScaleWorkloadAsync(
    JsonObject args,
    ClusterChatContext context,
    CancellationToken cancellationToken) {
    if (!TryReplicas(args, out var replicas, out var error))
      return error;

    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var kind = Arg(args, "kind") ?? context.Kind;
    var name = Arg(args, "name") ?? context.Name;

    if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
      return "scale_workload needs kind and name.";

    var descriptor = Resolve(kind);

    if (descriptor is null || !descriptor.Actions.CanScale)
      return "scale_workload only supports scalable workloads.";

    var ns = NamespaceArg(args, context, descriptor.Namespaced);
    var scaled = await workspace.Session.ScaleAsync(descriptor.ToRef(), name, ns, replicas, cancellationToken)
      .ConfigureAwait(false);

    return scaled.IsSuccess
      ? $"Scaled {descriptor.Kind}/{name} to {replicas}."
      : JoinMessages(scaled.Messages);
  }

  private async Task<string> ApplyManifestAsync(JsonObject args, CancellationToken cancellationToken) {
    var yaml = Arg(args, "yaml");

    if (string.IsNullOrWhiteSpace(yaml))
      return "apply_manifest needs yaml.";

    var prepared = PrepareManifest(yaml);

    if (prepared is null)
      return "apply_manifest could not parse the YAML.";

    if (workspace.Session is null)
      return "Not connected to a cluster.";

    var applied = await workspace.ApplyDocumentAsync(prepared, cancellationToken).ConfigureAwait(false);

    if (!applied.IsSuccess || applied.Value is null)
      return JoinMessages(applied.Messages);

    var kind = prepared["kind"]?.GetValue<string>() ?? "object";
    var name = (prepared["metadata"] as JsonObject)?["name"]?.GetValue<string>() ?? "(unnamed)";

    return $"Applied {kind}/{name}.";
  }

  private static string DescribeManifest(JsonObject args) {
    var yaml = Arg(args, "yaml");
    var prepared = string.IsNullOrWhiteSpace(yaml) ? null : PrepareManifest(yaml);

    if (prepared is null)
      return "Apply a manifest.";

    var kind = prepared["kind"]?.GetValue<string>() ?? "object";
    var meta = prepared["metadata"] as JsonObject;
    var name = meta?["name"]?.GetValue<string>() ?? "(unnamed)";
    var ns = meta?["namespace"]?.GetValue<string>();
    var where = string.IsNullOrWhiteSpace(ns) ? "" : $" in {ns}";

    return $"Apply {kind}/{name}{where}.";
  }

  private static bool TryReplicas(JsonObject args, out int replicas, out string error) {
    replicas = 0;
    error = "scale_workload replicas must be an integer from 0 to 100.";
    var node = args["replicas"];

    if (node is not JsonValue value)
      return false;

    int number;

    if (value.TryGetValue<int>(out number)) {
    }
    else if (value.TryGetValue<long>(out var wide) && wide is >= 0 and <= 100) {
      number = (int)wide;
    }
    else if (value.TryGetValue<string>(out var text) && int.TryParse(text, out number)) {
    }
    else {
      return false;
    }

    if (number is < 0 or > 100)
      return false;

    replicas = number;
    error = "";

    return true;
  }

  private static JsonObject ScaleSchema() {
    var schema = ObjectSchema(
      ("kind", "Deployment, StatefulSet, ReplicaSet, or ReplicationController", true),
      ("name", "Workload name", true),
      ("namespace", "Namespace. Defaults to the UI selection.", false));

    if (schema["properties"] is JsonObject properties)
      properties["replicas"] = new JsonObject {
        ["type"] = "integer",
        ["description"] = "Desired replicas, from 0 to 100"
      };

    if (schema["required"] is JsonArray required)
      required.Add("replicas");
    else
      schema["required"] = new JsonArray { "replicas" };

    return schema;
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
