using System.Text.Json.Serialization;


namespace MaksIT.ClusterConsole.Shared;

public sealed class Configuration {
  public const string AllNamespaces = "all";

  public string SelectedNamespace { get; set; } = AllNamespaces;

  public string? ActiveContext { get; set; }

  public List<string> OpenContexts { get; set; } = [];

  public Dictionary<string, string> NamespacesByContext { get; set; } = new(StringComparer.Ordinal);

  public Dictionary<string, bool> NavigatorExpanded { get; set; } = new(StringComparer.Ordinal);

  public bool OverviewPerNode { get; set; }

  [JsonIgnore]
  public AiSettings Ai { get; } = new();

  public bool AiEnabled {
    get => Ai.Enabled;
    set => Ai.Enabled = value;
  }

  public bool AiAgentEnabled {
    get => Ai.AgentEnabled;
    set => Ai.AgentEnabled = value;
  }

  public string OllamaEndpoint {
    get => Ai.Endpoint;
    set => Ai.Endpoint = value;
  }

  public string OllamaModel {
    get => Ai.Model;
    set => Ai.Model = value;
  }

  public int OllamaTimeoutSeconds {
    get => Ai.TimeoutSeconds;
    set => Ai.TimeoutSeconds = value;
  }

  public LayoutSettings Layout { get; set; } = new();

  public string? WhatsNewSeenVersion { get; set; }

  [JsonIgnore]
  public PortForwardDirectory Forwards { get; } = new();

  public List<PersistedPortForward> PortForwards {
    get => Forwards.Items;
    set => Forwards.Items = value ?? [];
  }

  public void EnsureDefaults() {
    OpenContexts ??= [];
    NamespacesByContext ??= new Dictionary<string, string>(StringComparer.Ordinal);
    NavigatorExpanded ??= new Dictionary<string, bool>(StringComparer.Ordinal);
    Layout ??= new LayoutSettings();
    Layout.Normalize();
    Forwards.Ensure();
    Ai.Ensure();
  }

  public bool IsNavigatorExpanded(string path) {
    var map = NavigatorExpanded;

    return map is not null && map.TryGetValue(path, out var expanded) && expanded;
  }

  public void SetNavigatorExpanded(IReadOnlyDictionary<string, bool> snapshot) {
    NavigatorExpanded = new Dictionary<string, bool>(snapshot, StringComparer.Ordinal);
  }

  public string NamespaceFor(string? contextName) {
    var map = NamespacesByContext;

    if (!string.IsNullOrWhiteSpace(contextName)
        && map is not null
        && map.TryGetValue(contextName, out var ns)
        && !string.IsNullOrWhiteSpace(ns))
      return ns;

    if ((map is null || map.Count == 0) && !string.IsNullOrWhiteSpace(SelectedNamespace))
      return SelectedNamespace;

    return AllNamespaces;
  }

  public void SetNamespace(string contextName, string ns) {
    NamespacesByContext ??= new Dictionary<string, string>(StringComparer.Ordinal);
    NamespacesByContext[contextName] = ns;
    SelectedNamespace = ns;
    ActiveContext = contextName;
  }

  public void UseAllNamespaces() {
    SelectedNamespace = AllNamespaces;

    if (NamespacesByContext is null)
      return;

    foreach (var key in NamespacesByContext.Keys.ToList())
      NamespacesByContext[key] = AllNamespaces;
  }

}

public sealed class PersistedPortForward {
  public string Context { get; set; } = "";

  public string Kind { get; set; } = "Pod";

  public string Name { get; set; } = "";

  public string Namespace { get; set; } = "default";

  public string PodName { get; set; } = "";

  public int LocalPort { get; set; }

  public int RemotePort { get; set; }

  public Dictionary<string, string>? MatchLabels { get; set; }
}
