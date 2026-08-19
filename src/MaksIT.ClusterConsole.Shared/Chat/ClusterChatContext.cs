using System.Text.RegularExpressions;


namespace MaksIT.ClusterConsole.Shared;

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
  public string SystemPrompt() {
    var selection = string.IsNullOrWhiteSpace(Kind) && string.IsNullOrWhiteSpace(Name)
      ? "No resource is selected in the UI."
      : $"Selected: {Kind} {Name} (namespace {Namespace}).";
    var pod = string.IsNullOrWhiteSpace(Pod)
      ? ""
      : $"Target pod: {Pod}. Container: {Container ?? "(not selected)"}.{Environment.NewLine}";

    return """
      You are the SRE assistant inside MaksIT.ClusterConsole, a Kubernetes desktop console.
      Diagnose problems using the provided UI context and tools. Do not invent objects, logs, or events.
      If data is missing, call a tool. Prefer get_logs with an explicit container on multi-container pods.
      You can only read the cluster. Do not claim you restarted, scaled, deleted, or applied YAML.
      Be concise. Name the object, the failing container, and the most likely cause.
      """
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
