namespace MaksIT.ClusterConsole.Shared;

public sealed class SavedColumnFilter {
  public string Text { get; set; } = "";

  public List<string> Excluded { get; set; } = [];
}

public sealed class LayoutSettings {
  public const string OverviewWarningsTable = "overview-warnings";
  public const string OverviewErrorsTable = "overview-errors";
  public const string OverviewLimitsTable = "overview-limits";
  public const string DataEditorTable = "data-editor";

  public double WindowWidth { get; set; } = 1400;

  public double WindowHeight { get; set; } = 860;

  public int? WindowX { get; set; }

  public int? WindowY { get; set; }

  public string WindowState { get; set; } = "Normal";

  public double CatalogWidth { get; set; } = 248;

  public double NavigatorWidth { get; set; } = 228;

  public double DetailsWidth { get; set; } = 380;

  public string? SelectedNavId { get; set; }

  public Dictionary<string, Dictionary<string, double>> ColumnWidths { get; set; } = new(StringComparer.Ordinal);

  public Dictionary<string, Dictionary<string, SavedColumnFilter>> ColumnFilters { get; set; } = new(StringComparer.Ordinal);

  public Dictionary<string, string> SearchByResource { get; set; } = new(StringComparer.Ordinal);

  public static string ResourceTable(string? resourceId) =>
    string.IsNullOrWhiteSpace(resourceId) ? "resources" : $"resources/{resourceId}";

  public IReadOnlyDictionary<string, double>? ColumnsFor(string tableKey) {
    if (ColumnWidths is not null && ColumnWidths.TryGetValue(tableKey, out var widths) && widths.Count > 0)
      return widths;
    return null;
  }

  public void SetColumns(string tableKey, Dictionary<string, double> widths) {
    ColumnWidths ??= new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
    ColumnWidths[tableKey] = widths;
  }

  public SavedColumnFilter? FilterFor(string tableKey, string header) {
    if (ColumnFilters is not null
        && ColumnFilters.TryGetValue(tableKey, out var filters)
        && filters.TryGetValue(header, out var filter))
      return filter;
    return null;
  }

  public void SetFilters(string tableKey, Dictionary<string, SavedColumnFilter> filters) {
    ColumnFilters ??= new Dictionary<string, Dictionary<string, SavedColumnFilter>>(StringComparer.Ordinal);
    ColumnFilters[tableKey] = filters;
  }

  public string SearchFor(string? resourceId) {
    var key = ResourceTable(resourceId);
    if (SearchByResource is not null && SearchByResource.TryGetValue(key, out var text))
      return text ?? "";
    return "";
  }

  public void SetSearch(string? resourceId, string text) {
    SearchByResource ??= new Dictionary<string, string>(StringComparer.Ordinal);
    SearchByResource[ResourceTable(resourceId)] = text ?? "";
  }
}
