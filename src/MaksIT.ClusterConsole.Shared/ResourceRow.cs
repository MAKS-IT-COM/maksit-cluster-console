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

  public string Status =>
    Cell("Status");

  public string Cell(string header) =>
    Cells.TryGetValue(header, out var value) ? value : string.Empty;

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

    Name = source.Name;
    Namespace = source.Namespace;
    Document = source.Document;
    Cells = source.Cells;

    if (nameChanged)
      OnPropertyChanged(nameof(Name));
    if (namespaceChanged)
      OnPropertyChanged(nameof(Namespace));
    if (!cellsChanged)
      return;

    OnPropertyChanged(nameof(Cells));
    OnPropertyChanged(nameof(Status));
  }

  public static ResourceRow From(JsonObject item, ResourceDescriptor descriptor, ResourceMetrics? metrics = null) {
    var cells = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var column in descriptor.Columns) {
      cells[column.Header] = column.Path switch {
        "status.containerStatuses" when column.Header == "Ready" => JsonPath.PodReady(item),
        "status.containerStatuses" when column.Header == "Restarts" => JsonPath.PodRestarts(item),
        "metrics.cpu" => metrics?.Cpu ?? "",
        "metrics.memory" => metrics?.Memory ?? "",
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
