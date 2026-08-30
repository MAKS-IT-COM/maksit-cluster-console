using MaksIT.Results;


namespace MaksIT.ClusterConsole.Shared;

public readonly record struct ResourceActionBatchOutcome(
  int Succeeded,
  int Total,
  IReadOnlyList<string> Failures) {
  public string Format(string oneDone, string manyDone) {
    if (Total <= 0)
      return "";
    if (Failures.Count == 0)
      return Total == 1 ? oneDone : manyDone;
    if (Succeeded == 0)
      return string.Join("; ", Failures);
    return $"{Succeeded} of {Total} succeeded. {string.Join("; ", Failures)}";
  }
}

public static class ResourceActionBatch {
  public static IReadOnlyList<ResourceRow> Targets(
    IReadOnlyList<ResourceRow> selectedRows,
    ResourceRow? selectedRow) {
    if (selectedRows.Count > 0)
      return selectedRows;
    return selectedRow is null ? [] : [selectedRow];
  }

  public static string Label(ResourceRow row) =>
    string.IsNullOrEmpty(row.Namespace) ? row.Name : $"{row.Namespace}/{row.Name}";

  public static async Task<ResourceActionBatchOutcome> RunAsync(
    IReadOnlyList<ResourceRow> rows,
    Func<ResourceRow, Task<Result>> action) {
    ArgumentNullException.ThrowIfNull(rows);
    ArgumentNullException.ThrowIfNull(action);

    var succeeded = 0;
    var failures = new List<string>();
    foreach (var row in rows) {
      var result = await action(row);
      if (result.IsSuccess) {
        succeeded++;
        continue;
      }

      failures.Add($"{Label(row)}: {string.Join("; ", result.Messages)}");
    }

    return new ResourceActionBatchOutcome(succeeded, rows.Count, failures);
  }
}
