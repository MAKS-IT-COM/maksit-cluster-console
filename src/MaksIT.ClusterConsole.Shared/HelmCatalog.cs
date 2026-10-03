using System.Text.Json.Nodes;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public static class HelmCatalog {
  public static async Task<Result<IReadOnlyList<HelmRevision>>> HistoryAsync(
    IClusterSession? session,
    string name,
    string? @namespace,
    CancellationToken cancellationToken = default) {
    if (session is null)
      return Result<IReadOnlyList<HelmRevision>>.ServiceUnavailable(null, "not connected");

    var listed = await session.ListHelmReleaseDocumentsAsync(@namespace, name, cancellationToken)
      .ConfigureAwait(false);

    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<HelmRevision>>(null, false, listed.Messages, listed.StatusCode);

    return Result<IReadOnlyList<HelmRevision>>.Ok(ReadRevisions(listed.Value));
  }

  public static async Task<Result<IReadOnlyList<ResourceRow>>> ListReleasesAsync(
    IClusterSession session,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var listed = await session.ListHelmReleaseDocumentsAsync(@namespace, null, cancellationToken)
      .ConfigureAwait(false);

    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = ReadRevisions(listed.Value)
      .GroupBy(revision => (revision.Name, revision.Namespace))
      .Select(group => group.OrderByDescending(revision => revision.Revision).ThenByDescending(revision => revision.Updated).First())
      .OrderBy(revision => revision.Namespace, StringComparer.OrdinalIgnoreCase)
      .ThenBy(revision => revision.Name, StringComparer.OrdinalIgnoreCase)
      .Select(revision => {
        var doc = new JsonObject {
          ["metadata"] = new JsonObject { ["name"] = revision.Name, ["namespace"] = revision.Namespace },
          ["status"] = revision.Status,
          ["chart"] = revision.Chart,
          ["appVersion"] = revision.AppVersion
        };

        return new ResourceRow {
          Uid = $"{revision.Namespace}/{revision.Name}",
          Name = revision.Name,
          Namespace = revision.Namespace,
          Document = doc,
          Cells = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Name"] = revision.Name,
            ["Namespace"] = revision.Namespace,
            ["Revision"] = revision.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Status"] = revision.Status,
            ["Chart"] = revision.Chart,
            ["App"] = revision.AppVersion,
            ["Updated"] = revision.UpdatedText
          }
        };
      })
      .Where(row => ClusterWorkspace.Matches(row, filter))
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  public static async Task<Result<IReadOnlyList<ResourceRow>>> ListChartsAsync(
    IClusterSession session,
    string? @namespace,
    string? filter,
    CancellationToken cancellationToken) {
    var listed = await session.ListHelmReleaseDocumentsAsync(@namespace, null, cancellationToken)
      .ConfigureAwait(false);

    if (!listed.IsSuccess)
      return new Result<IReadOnlyList<ResourceRow>>(null, false, listed.Messages, listed.StatusCode);

    var rows = HelmRelease.Charts(ReadRevisions(listed.Value))
      .Select(chart => {
        var doc = new JsonObject {
          ["metadata"] = new JsonObject { ["name"] = chart.Name },
          ["releases"] = chart.ReleaseLines
        };

        return new ResourceRow {
          Uid = $"{chart.Name}/{chart.Version}/{chart.AppVersion}",
          Name = chart.Name,
          Document = doc,
          Cells = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Chart"] = chart.Name,
            ["Version"] = chart.Version,
            ["App"] = chart.AppVersion,
            ["Status"] = chart.Status,
            ["Releases"] = chart.Releases.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Namespaces"] = chart.Namespaces
          }
        };
      })
      .Where(row => ClusterWorkspace.Matches(row, filter))
      .ToList();

    return Result<IReadOnlyList<ResourceRow>>.Ok(rows);
  }

  private static List<HelmRevision> ReadRevisions(IReadOnlyList<JsonObject>? documents) =>
    (documents ?? [])
      .Select(HelmRelease.Read)
      .OfType<HelmRevision>()
      .ToList();
}
