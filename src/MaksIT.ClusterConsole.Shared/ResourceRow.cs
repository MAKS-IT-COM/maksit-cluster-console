using System.ComponentModel;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Shared;

public sealed class ResourceRow : INotifyPropertyChanged {
  public event PropertyChangedEventHandler? PropertyChanged;

  public required string Uid { get; init; }

  public required string Name { get; set; }

  public string? Namespace { get; set; }

  public required JsonObject Document { get; set; }

  public required IReadOnlyDictionary<string, string> Cells { get; set; }

  public IReadOnlyDictionary<string, string> CellTips { get; set; } =
    EmptyCellTips;

  private static readonly IReadOnlyDictionary<string, string> EmptyCellTips =
    new Dictionary<string, string>(StringComparer.Ordinal);

  public string Status =>
    Cell("Status");

  public string Cell(string header) =>
    Cells.TryGetValue(header, out var value) ? value : string.Empty;

  public string CellTip(string header) =>
    CellTips.TryGetValue(header, out var value) ? value : string.Empty;

  public string FormatOverview(IEnumerable<PodContainer>? containers = null) {
    var lines = Cells.Select(kv => $"{kv.Key}: {kv.Value}").ToList();
    var workloads = ApplicationManifest.Workloads(Document);
    if (workloads.Count > 0) {
      lines.Add("");
      lines.Add("Workloads:");
      foreach (var (kind, name) in workloads)
        lines.Add($"  {kind}/{name}");
    }

    var listed = (containers ?? []).ToList();
    if (listed.Count == 0)
      return string.Join('\n', lines);

    lines.Add("");
    lines.Add("Containers:");
    foreach (var container in listed)
      lines.Add($"  {container.Name}  [{container.Kind}]  {container.StatusLine}  {container.ImageLabel}");

    return string.Join('\n', lines);
  }

  public void CopyFrom(ResourceRow source) {
    ArgumentNullException.ThrowIfNull(source);
    if (ReferenceEquals(this, source))
      return;
    if (!string.Equals(Uid, source.Uid, StringComparison.Ordinal))
      throw new ArgumentException("Cannot copy a row with a different uid.", nameof(source));

    var nameChanged = !string.Equals(Name, source.Name, StringComparison.Ordinal);
    var namespaceChanged = !string.Equals(Namespace, source.Namespace, StringComparison.Ordinal);
    var cellsChanged = !CellsEqual(Cells, source.Cells);
    var tipsChanged = !CellsEqual(CellTips, source.CellTips);

    Name = source.Name;
    Namespace = source.Namespace;
    Document = source.Document;
    Cells = source.Cells;
    CellTips = source.CellTips;

    if (nameChanged)
      OnPropertyChanged(nameof(Name));
    if (namespaceChanged)
      OnPropertyChanged(nameof(Namespace));
    if (!cellsChanged && !tipsChanged)
      return;

    if (cellsChanged) {
      OnPropertyChanged(nameof(Cells));
      OnPropertyChanged(nameof(Status));
    }

    if (tipsChanged)
      OnPropertyChanged(nameof(CellTips));
  }

  public static ResourceRow From(JsonObject item, ResourceDescriptor descriptor, ResourceMetrics? metrics = null) {
    var cells = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var column in descriptor.Columns) {
      cells[column.Header] = column.Path switch {
        "status.containerStatuses" when column.Header == "Ready" => JsonPath.PodReady(item),
        "status.containerStatuses" when column.Header == "Restarts" => JsonPath.PodRestarts(item),
        "metrics.cpu" => metrics?.Cpu ?? "",
        "metrics.memory" => FormatMetricMemory(metrics?.Memory),
        _ => JsonPath.Read(item, column.Path)
      };
    }

    return new ResourceRow {
      Uid = JsonPath.Uid(item),
      Name = JsonPath.Name(item),
      Namespace = JsonPath.Namespace(item),
      Document = item,
      Cells = cells
    };
  }

  private void OnPropertyChanged(string propertyName) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

  private static string FormatMetricMemory(string? value) {
    if (string.IsNullOrEmpty(value))
      return "";
    if (value == "-")
      return value;

    return KubeQuantity.FormatBytesCompact(KubeQuantity.ToBytes(value));
  }

  private static bool CellsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) {
    if (ReferenceEquals(left, right))
      return true;
    if (left.Count != right.Count)
      return false;

    foreach (var (key, value) in left) {
      if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
        return false;
    }

    return true;
  }
}
