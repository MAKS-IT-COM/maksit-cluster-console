namespace MaksIT.ClusterConsole.Shared;

public sealed class ResourceColumnFilter {
  public required string Header { get; init; }

  public string Text { get; set; } = "";

  public HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);

  public bool IsActive =>
    !string.IsNullOrWhiteSpace(Text) || Excluded.Count > 0;

  public bool Matches(ResourceRow row) {
    var cell = row.Cell(Header);
    if (!string.IsNullOrWhiteSpace(Text)
        && cell.IndexOf(Text, StringComparison.OrdinalIgnoreCase) < 0)
      return false;
    return !Excluded.Contains(cell);
  }

  public static IReadOnlyList<string> DistinctValues(IEnumerable<ResourceRow> rows, string header) =>
    rows.Select(row => row.Cell(header))
      .Distinct(StringComparer.Ordinal)
      .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
      .ToList();

  public static string Scope(ResourceColumnFilter filter, IEnumerable<string> distinct) {
    if (!string.IsNullOrWhiteSpace(filter.Text))
      return Configuration.AllNamespaces;

    var included = distinct
      .Where(value => !filter.Excluded.Contains(value) && !string.IsNullOrEmpty(value))
      .Distinct(StringComparer.Ordinal)
      .ToList();
    return included.Count == 1 ? included[0] : Configuration.AllNamespaces;
  }

  public static bool MatchesAll(ResourceRow row, IEnumerable<ResourceColumnFilter> filters) {
    foreach (var filter in filters) {
      if (!filter.Matches(row))
        return false;
    }

    return true;
  }
}
