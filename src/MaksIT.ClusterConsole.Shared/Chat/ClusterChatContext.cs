using System.Text.RegularExpressions;


namespace MaksIT.ClusterConsole.Shared.Chat;

public sealed record ClusterChatContext(
  string Cluster,
  string Namespace,
  string? Kind,
  string? Name,
  string? Pod,
  string? Container,
  string Overview,
  string Events,
  string Logs) {
  public string SystemPrompt(bool agent = false) {
    var selection = string.IsNullOrWhiteSpace(Kind) && string.IsNullOrWhiteSpace(Name)
      ? "No resource is selected in the UI."
      : $"Selected: {Kind} {Name} (namespace {Namespace}).";
    var pod = string.IsNullOrWhiteSpace(Pod)
      ? ""
      : $"Target pod: {Pod}. Container: {Container ?? "(not selected)"}.{Environment.NewLine}";

    var rules = agent
      ? $"""
        You are the SRE assistant inside {AppInfo.ProductName}, a Kubernetes desktop console.
        Diagnose problems and fix them with tools. Do not invent objects, logs, or events.
        Read first. A broad health question is valid. Prefer get_logs with an explicit container on multi-container pods.
        Do not repeat a read you already have. Write the diagnosis before you propose a repair.
        Change the cluster only by calling restart_workload, delete_pod, scale_workload, or apply_manifest.
        Before a write, name the object, the cause, and what will change.
        After the operator approves or rejects, continue and answer the original question. Do not stop at the repair.
        Do not claim a change was applied until the tool result says it succeeded.
        A rejected tool means the operator refused that change. Propose another fix or stop.
        restart_workload is for Deployment, StatefulSet, and DaemonSet. delete_pod is not a force delete.
        scale_workload replicas must be an integer from 0 to 100.
        Be concise.
        """
      : $"""
        You are the SRE assistant inside {AppInfo.ProductName}, a Kubernetes desktop console.
        Diagnose problems using the provided UI context and tools. Do not invent objects, logs, or events.
        If data is missing, call a tool. Prefer get_logs with an explicit container on multi-container pods.
        You can only read the cluster. Do not claim you restarted, scaled, deleted, or applied YAML.
        Be concise. Name the object, the failing container, and the most likely cause.
        """;

    return rules
      + Environment.NewLine
      + $"Cluster context: {Cluster}. UI namespace filter: {Namespace}."
      + Environment.NewLine
      + selection
      + Environment.NewLine
      + pod
      + Clip("Overview", Overview)
      + Clip("Events", Events)
      + Clip("Logs", Logs);
  }

  public static string Clip(string title, string? text, int max = 3500) {
    if (string.IsNullOrWhiteSpace(text))
      return "";

    var value = text.Trim();

    if (value.Length > max)
      value = value[^max..];

    return $"{title}:{Environment.NewLine}{value}{Environment.NewLine}{Environment.NewLine}";
  }

  public static string StripThink(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return "";

    return Regex.Replace(text, @"<think>[\s\S]*?</think>", string.Empty, RegexOptions.IgnoreCase).Trim();
  }
}
