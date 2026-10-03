using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public sealed record HelmChart(
  string Name,
  string Version,
  string AppVersion,
  string Status,
  string Namespaces,
  int Releases,
  string ReleaseLines);

public sealed record HelmRevision(
  int Revision,
  string Name,
  string Namespace,
  string Status,
  string Chart,
  string ChartName,
  string ChartVersion,
  string AppVersion,
  string Description,
  DateTimeOffset? Updated,
  string ValuesYaml,
  string Manifest) {
  public string Label => Revision > 0 ? $"r{Revision} · {Status}" : Status;

  public string UpdatedText =>
    Updated is null ? "" : Updated.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm");
}

public static class HelmRelease {
  public static HelmRevision? Read(JsonObject release) {
    var name = JsonPath.Text(release["name"]);

    if (string.IsNullOrWhiteSpace(name))
      return null;

    var info = release["info"] as JsonObject;
    var metadata = release["chart"]?["metadata"] as JsonObject;
    var chartName = JsonPath.Text(metadata?["name"]);
    var chartVersion = JsonPath.Text(metadata?["version"]);
    var chart = string.IsNullOrEmpty(chartVersion) ? chartName : $"{chartName}-{chartVersion}";
    var status = JsonPath.Text(info?["status"]);

    if (string.IsNullOrWhiteSpace(status))
      status = "unknown";

    var config = release["config"] as JsonObject;
    var values = config is null || config.Count == 0
      ? "No user-supplied values.\n"
      : YamlFormatter.FromJson(config);
    var manifest = JsonPath.Text(release["manifest"]);

    if (string.IsNullOrWhiteSpace(manifest))
      manifest = "No manifest stored in this revision.\n";
    else if (!manifest.EndsWith('\n'))
      manifest += "\n";

    DateTimeOffset? updated = null;

    if (DateTimeOffset.TryParse(JsonPath.Text(info?["last_deployed"]), out var parsed))
      updated = parsed;

    return new HelmRevision(
      ReadInt(release["version"]) ?? 0,
      name,
      JsonPath.Text(release["namespace"]),
      status,
      chart,
      string.IsNullOrEmpty(chartName) ? chart : chartName,
      chartVersion,
      JsonPath.Text(metadata?["appVersion"]),
      JsonPath.Text(info?["description"]),
      updated,
      values,
      manifest);
  }

  public static IReadOnlyList<HelmChart> Charts(IEnumerable<HelmRevision> revisions) {
    var latest = revisions
      .GroupBy(revision => (revision.Name, revision.Namespace))
      .Select(group => group.OrderByDescending(revision => revision.Revision).ThenByDescending(revision => revision.Updated).First());

    return latest
      .GroupBy(revision => (
        Name: string.IsNullOrEmpty(revision.ChartName) ? revision.Chart : revision.ChartName,
        revision.ChartVersion,
        revision.AppVersion))
      .Select(group => {
        var installed = group
          .OrderBy(revision => revision.Namespace, StringComparer.OrdinalIgnoreCase)
          .ThenBy(revision => revision.Name, StringComparer.OrdinalIgnoreCase)
          .ToList();
        var statuses = installed
          .Select(revision => revision.Status)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToList();
        var namespaces = installed
          .Select(revision => revision.Namespace)
          .Where(ns => !string.IsNullOrWhiteSpace(ns))
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .Order(StringComparer.OrdinalIgnoreCase);
        var lines = string.Join(
          '\n',
          installed.Select(revision => $"{revision.Namespace}/{revision.Name}  {revision.Status}  r{revision.Revision}"));

        return new HelmChart(
          group.Key.Name,
          group.Key.ChartVersion,
          group.Key.AppVersion,
          statuses.Count == 1 ? statuses[0] : "mixed",
          string.Join(", ", namespaces),
          installed.Count,
          lines);
      })
      .OrderBy(chart => chart.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(chart => chart.Version, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public static string Diff(string before, string after) {
    var left = Lines(before);
    var right = Lines(after);

    if (left.SequenceEqual(right))
      return "No differences.";

    if (left.Length > 800 || right.Length > 800)
      return "This pair is too large to diff here. Read each revision's values or manifest separately.";

    var ops = Changes(left, right);
    var sb = new System.Text.StringBuilder();

    foreach (var (kind, line) in ops)
      sb.Append(kind).Append(' ').AppendLine(line);

    return sb.ToString();
  }

  private static List<(char Kind, string Line)> Changes(string[] left, string[] right) {
    var n = left.Length;
    var m = right.Length;
    var score = new int[n + 1, m + 1];

    for (var i = n - 1; i >= 0; i--) {
      for (var j = m - 1; j >= 0; j--)
        score[i, j] = left[i] == right[j]
          ? score[i + 1, j + 1] + 1
          : Math.Max(score[i + 1, j], score[i, j + 1]);
    }

    var ops = new List<(char Kind, string Line)>();
    var x = 0;
    var y = 0;

    while (x < n && y < m) {
      if (left[x] == right[y]) {
        ops.Add((' ', left[x]));
        x++;
        y++;
      }
      else if (score[x + 1, y] >= score[x, y + 1]) {
        ops.Add(('-', left[x]));
        x++;
      }
      else {
        ops.Add(('+', right[y]));
        y++;
      }
    }

    while (x < n)
      ops.Add(('-', left[x++]));

    while (y < m)
      ops.Add(('+', right[y++]));

    return Collapse(ops);
  }

  private static List<(char Kind, string Line)> Collapse(List<(char Kind, string Line)> ops) {
    var collapsed = new List<(char Kind, string Line)>();
    var context = new List<string>();
    var started = false;

    foreach (var op in ops) {
      if (op.Kind == ' ') {
        context.Add(op.Line);

        continue;
      }

      started = FlushContext(collapsed, context, started);
      collapsed.Add(op);
      context.Clear();
    }

    if (started && context.Count > 0) {
      var tail = context.Count > 2 ? context.Take(2) : context;

      foreach (var line in tail)
        collapsed.Add((' ', line));
    }

    return collapsed;
  }

  private static bool FlushContext(List<(char Kind, string Line)> collapsed, List<string> context, bool started) {
    if (!started) {
      if (context.Count > 2)
        collapsed.Add((' ', "…"));

      foreach (var line in context.TakeLast(2))
        collapsed.Add((' ', line));

      return true;
    }

    if (context.Count > 4) {
      foreach (var line in context.Take(2))
        collapsed.Add((' ', line));

      collapsed.Add((' ', "…"));

      foreach (var line in context.TakeLast(2))
        collapsed.Add((' ', line));
    }
    else {
      foreach (var line in context)
        collapsed.Add((' ', line));
    }

    return true;
  }

  private static string[] Lines(string text) {
    var parts = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    if (parts.Length > 0 && parts[^1].Length == 0)
      return parts[..^1];

    return parts;
  }

  private static int? ReadInt(JsonNode? node) {
    if (node is not JsonValue value)
      return null;

    if (value.TryGetValue<int>(out var number))
      return number;

    if (value.TryGetValue<long>(out var wide))
      return (int)wide;

    if (value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
      return parsed;

    return null;
  }
}
