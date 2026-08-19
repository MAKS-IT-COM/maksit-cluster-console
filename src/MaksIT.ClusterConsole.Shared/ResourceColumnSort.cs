using System.Collections;
using System.Globalization;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Shared;

public sealed class ResourceRowComparer(string header) : IComparer, IComparer<ResourceRow> {
  public string Header { get; } = header;

  public int Compare(object? x, object? y) {
    if (ReferenceEquals(x, y))
      return 0;
    if (x is not ResourceRow left)
      return y is null ? 0 : -1;
    if (y is not ResourceRow right)
      return 1;

    return Compare(left, right);
  }

  public int Compare(ResourceRow? x, ResourceRow? y) {
    if (ReferenceEquals(x, y))
      return 0;
    if (x is null)
      return -1;
    if (y is null)
      return 1;

    return ResourceColumnSort.Compare(Header, x.Cell(Header), y.Cell(Header));
  }
}

public static class ResourceColumnSort {
  public static int Compare(string header, string? left, string? right) {
    if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
      return 0;
    if (string.IsNullOrEmpty(left))
      return -1;
    if (string.IsNullOrEmpty(right))
      return 1;

    return header switch {
      "Age" => AgeToSeconds(left).CompareTo(AgeToSeconds(right)),
      "Restarts" => ParseInt(left).CompareTo(ParseInt(right)),
      "Ready" => CompareReady(left, right),
      "CPU" => KubeQuantity.ToCores(left).CompareTo(KubeQuantity.ToCores(right)),
      "Memory" => KubeQuantity.ToBytes(left).CompareTo(KubeQuantity.ToBytes(right)),
      "Replicas" or "Desired" or "Current" or "Min" or "Max" or "Port" =>
        ParseInt(left).CompareTo(ParseInt(right)),
      _ => string.Compare(left, right, StringComparison.OrdinalIgnoreCase)
    };
  }

  public static double AgeToSeconds(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return 0;

    var total = 0d;
    var number = 0d;
    var hasDigit = false;
    foreach (var c in text) {
      if (char.IsDigit(c)) {
        number = number * 10 + (c - '0');
        hasDigit = true;
        continue;
      }

      if (!hasDigit)
        continue;

      total += c switch {
        'd' or 'D' => number * 86_400,
        'h' or 'H' => number * 3_600,
        'm' or 'M' => number * 60,
        's' or 'S' => number,
        _ => 0
      };
      number = 0;
      hasDigit = false;
    }

    return total;
  }

  private static int CompareReady(string left, string right) {
    ParseReady(left, out var leftReady, out var leftTotal);
    ParseReady(right, out var rightReady, out var rightTotal);
    var cmp = leftReady.CompareTo(rightReady);
    return cmp != 0 ? cmp : leftTotal.CompareTo(rightTotal);
  }

  private static void ParseReady(string text, out int ready, out int total) {
    ready = 0;
    total = 0;
    var slash = text.IndexOf('/');
    if (slash < 0) {
      ready = ParseInt(text);
      total = ready;
      return;
    }

    _ = int.TryParse(text[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out ready);
    _ = int.TryParse(text[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out total);
  }

  private static int ParseInt(string text) {
    var span = text.AsSpan().Trim();
    var end = 0;
    while (end < span.Length && (char.IsDigit(span[end]) || span[end] is '+' or '-'))
      end++;

    return end > 0 && int.TryParse(span[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
      ? value
      : 0;
  }
}
