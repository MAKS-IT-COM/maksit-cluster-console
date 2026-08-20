using System.Text.Json.Serialization;


namespace MaksIT.ClusterConsole.Shared;

public sealed class SavedColumnFilter {
  public string Text { get; set; } = "";

  public List<string> Excluded { get; set; } = [];
}

public sealed class SavedColumnSort {
  public string Header { get; set; } = "";

  public string Direction { get; set; } = "Ascending";
}

public sealed class SavedTableLayout {
  public Dictionary<string, double> Widths { get; set; } = new(StringComparer.Ordinal);

  public Dictionary<string, SavedColumnFilter> Filters { get; set; } = new(StringComparer.Ordinal);

  public SavedColumnSort? Sort { get; set; }

  public string Search { get; set; } = "";
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

  public Dictionary<string, SavedTableLayout> Tables { get; set; } = new(StringComparer.Ordinal);

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, Dictionary<string, double>>? ColumnWidths { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, Dictionary<string, SavedColumnFilter>>? ColumnFilters { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, SavedColumnSort>? ColumnSorts { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, string>? SearchByResource { get; set; }

  public static string ResourceTable(string? resourceId) =>
    string.IsNullOrWhiteSpace(resourceId) ? "resources" : $"resources/{resourceId}";

  public static string ContextTable(string? context, string tableKey) =>
    string.IsNullOrWhiteSpace(context) ? tableKey : $"{context}/{tableKey}";

  public void Normalize() {
    Tables ??= new Dictionary<string, SavedTableLayout>(StringComparer.Ordinal);
    foreach (var table in Tables.Values)
      NormalizeTable(table);

    MergeLegacy();
    ColumnWidths = null;
    ColumnFilters = null;
    ColumnSorts = null;
    SearchByResource = null;
  }

  public IReadOnlyDictionary<string, double>? ColumnsFor(string tableKey) =>
    ColumnsFor(null, tableKey);

  public IReadOnlyDictionary<string, double>? ColumnsFor(string? context, string tableKey) {
    var widths = Find(context, tableKey)?.Widths;
    return widths is { Count: > 0 } ? widths : null;
  }

  public void SetColumns(string tableKey, Dictionary<string, double> widths) =>
    SetColumns(null, tableKey, widths);

  public void SetColumns(string? context, string tableKey, Dictionary<string, double> widths) =>
    GetOrAdd(context, tableKey).Widths = new Dictionary<string, double>(widths, StringComparer.Ordinal);

  public SavedColumnFilter? FilterFor(string tableKey, string header) =>
    FilterFor(null, tableKey, header);

  public SavedColumnFilter? FilterFor(string? context, string tableKey, string header) {
    var filters = Find(context, tableKey)?.Filters;
    if (filters is not null && filters.TryGetValue(header, out var filter))
      return filter;
    return null;
  }

  public void SetFilters(string tableKey, Dictionary<string, SavedColumnFilter> filters) =>
    SetFilters(null, tableKey, filters);

  public void SetFilters(string? context, string tableKey, Dictionary<string, SavedColumnFilter> filters) =>
    GetOrAdd(context, tableKey).Filters = new Dictionary<string, SavedColumnFilter>(filters, StringComparer.Ordinal);

  public SavedColumnSort? SortFor(string tableKey) =>
    SortFor(null, tableKey);

  public SavedColumnSort? SortFor(string? context, string tableKey) {
    var sort = Find(context, tableKey)?.Sort;
    return sort is null || string.IsNullOrWhiteSpace(sort.Header) ? null : sort;
  }

  public void SetSort(string tableKey, SavedColumnSort? sort) =>
    SetSort(null, tableKey, sort);

  public void SetSort(string? context, string tableKey, SavedColumnSort? sort) {
    var table = GetOrAdd(context, tableKey);
    table.Sort = sort is null || string.IsNullOrWhiteSpace(sort.Header) ? null : sort;
  }

  public string SearchFor(string? resourceId) =>
    SearchFor(null, resourceId);

  public string SearchFor(string? context, string? resourceId) =>
    Find(context, ResourceTable(resourceId))?.Search ?? "";

  public void SetSearch(string? resourceId, string text) =>
    SetSearch(null, resourceId, text);

  public void SetSearch(string? context, string? resourceId, string text) =>
    GetOrAdd(context, ResourceTable(resourceId)).Search = text ?? "";

  private SavedTableLayout GetOrAdd(string? context, string tableKey) {
    Tables ??= new Dictionary<string, SavedTableLayout>(StringComparer.Ordinal);
    var key = ContextTable(context, tableKey);
    if (!Tables.TryGetValue(key, out var table)) {
      table = new SavedTableLayout();
      Tables[key] = table;
    }

    NormalizeTable(table);
    return table;
  }

  private SavedTableLayout? Find(string? context, string tableKey) {
    if (Tables is null)
      return null;

    if (!string.IsNullOrWhiteSpace(context)
        && Tables.TryGetValue(ContextTable(context, tableKey), out var keyed))
      return keyed;

    if (Tables.TryGetValue(tableKey, out var shared))
      return shared;

    return null;
  }

  private void MergeLegacy() {
    if (ColumnWidths is not null) {
      foreach (var (key, widths) in ColumnWidths) {
        if (widths is not { Count: > 0 })
          continue;
        var table = GetOrAdd(null, key);
        if (table.Widths.Count == 0)
          table.Widths = new Dictionary<string, double>(widths, StringComparer.Ordinal);
      }
    }

    if (ColumnFilters is not null) {
      foreach (var (key, filters) in ColumnFilters) {
        if (filters is not { Count: > 0 })
          continue;
        var table = GetOrAdd(null, key);
        if (table.Filters.Count == 0)
          table.Filters = new Dictionary<string, SavedColumnFilter>(filters, StringComparer.Ordinal);
      }
    }

    if (ColumnSorts is not null) {
      foreach (var (key, sort) in ColumnSorts) {
        if (sort is null || string.IsNullOrWhiteSpace(sort.Header))
          continue;
        var table = GetOrAdd(null, key);
        table.Sort ??= sort;
      }
    }

    if (SearchByResource is not null) {
      foreach (var (key, text) in SearchByResource) {
        if (string.IsNullOrEmpty(text))
          continue;
        var table = GetOrAdd(null, key);
        if (string.IsNullOrEmpty(table.Search))
          table.Search = text;
      }
    }
  }

  private static void NormalizeTable(SavedTableLayout table) {
    table.Widths ??= new Dictionary<string, double>(StringComparer.Ordinal);
    table.Filters ??= new Dictionary<string, SavedColumnFilter>(StringComparer.Ordinal);
    table.Search ??= "";
  }
}
