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

  public void EnsureDefaults() {
    OpenContexts ??= [];
    NamespacesByContext ??= new Dictionary<string, string>(StringComparer.Ordinal);
    NavigatorExpanded ??= new Dictionary<string, bool>(StringComparer.Ordinal);
    Layout ??= new LayoutSettings();
    Layout.ColumnWidths ??= new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
    Layout.ColumnFilters ??= new Dictionary<string, Dictionary<string, SavedColumnFilter>>(StringComparer.Ordinal);
    Layout.SearchByResource ??= new Dictionary<string, string>(StringComparer.Ordinal);
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
}
