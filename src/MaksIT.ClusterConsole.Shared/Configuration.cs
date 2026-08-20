namespace MaksIT.ClusterConsole.Shared;

public sealed class Configuration {
  public const string AllNamespaces = "all";

  public string SelectedNamespace { get; set; } = AllNamespaces;

  public string? ActiveContext { get; set; }

  public List<string> OpenContexts { get; set; } = [];

  public Dictionary<string, string> NamespacesByContext { get; set; } = new(StringComparer.Ordinal);

  public Dictionary<string, bool> NavigatorExpanded { get; set; } = new(StringComparer.Ordinal);

  public bool OverviewPerNode { get; set; }

  public string OllamaEndpoint { get; set; } = ClusterChatService.DefaultEndpoint;

  public string OllamaModel { get; set; } = ClusterChatService.DefaultModel;

  public LayoutSettings Layout { get; set; } = new();

  public List<PersistedPortForward> PortForwards { get; set; } = [];

  public void EnsureDefaults() {
    OpenContexts ??= [];
    NamespacesByContext ??= new Dictionary<string, string>(StringComparer.Ordinal);
    NavigatorExpanded ??= new Dictionary<string, bool>(StringComparer.Ordinal);
    Layout ??= new LayoutSettings();
    Layout.Normalize();
    PortForwards ??= [];
    if (string.IsNullOrWhiteSpace(OllamaEndpoint))
      OllamaEndpoint = ClusterChatService.DefaultEndpoint;
    if (string.IsNullOrWhiteSpace(OllamaModel))
      OllamaModel = ClusterChatService.DefaultModel;
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

  public IReadOnlyList<PersistedPortForward> PortForwardsFor(string context) =>
    (PortForwards ?? [])
      .Where(p => string.Equals(p.Context, context, StringComparison.Ordinal))
      .ToList();

  public void UpsertPortForward(PersistedPortForward forward) {
    ArgumentNullException.ThrowIfNull(forward);
    PortForwards ??= [];
    PortForwards.RemoveAll(p => SamePortForward(p, forward.Context, forward.LocalPort));
    PortForwards.Add(forward);
  }

  public void RemovePortForward(string context, int localPort) {
    PortForwards?.RemoveAll(p => SamePortForward(p, context, localPort));
  }

  private static bool SamePortForward(PersistedPortForward item, string context, int localPort) =>
    string.Equals(item.Context, context, StringComparison.Ordinal) && item.LocalPort == localPort;
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
