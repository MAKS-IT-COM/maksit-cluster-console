using System.Net;
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
      _ when IsIpHeader(header) => CompareIpList(left, right),
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

  private static bool IsIpHeader(string header) =>
    header.Equals("IP", StringComparison.OrdinalIgnoreCase)
    || header.EndsWith(" IP", StringComparison.OrdinalIgnoreCase);

  private static int CompareIpList(string left, string right) {
    var leftParts = SplitIpCell(left);
    var rightParts = SplitIpCell(right);
    var count = Math.Min(leftParts.Length, rightParts.Length);
    for (var i = 0; i < count; i++) {
      var cmp = CompareIpToken(leftParts[i], rightParts[i]);
      if (cmp != 0)
        return cmp;
    }

    return leftParts.Length.CompareTo(rightParts.Length);
  }

  private static string[] SplitIpCell(string text) =>
    text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

  private static int CompareIpToken(string left, string right) {
    var leftMissing = IsMissingIp(left);
    var rightMissing = IsMissingIp(right);
    if (leftMissing && rightMissing)
      return 0;
    if (leftMissing)
      return -1;
    if (rightMissing)
      return 1;

    if (IPAddress.TryParse(left, out var leftIp)) {
      if (IPAddress.TryParse(right, out var rightIp))
        return CompareAddress(leftIp, rightIp);

      return -1;
    }

    if (IPAddress.TryParse(right, out _))
      return 1;

    return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsMissingIp(string text) =>
    text.Equals("None", StringComparison.OrdinalIgnoreCase)
    || text.Equals("<none>", StringComparison.OrdinalIgnoreCase);

  private static int CompareAddress(IPAddress left, IPAddress right) {
    var family = left.AddressFamily.CompareTo(right.AddressFamily);
    if (family != 0)
      return family;

    Span<byte> leftBytes = stackalloc byte[16];
    Span<byte> rightBytes = stackalloc byte[16];
    if (!left.TryWriteBytes(leftBytes, out var leftLength)
        || !right.TryWriteBytes(rightBytes, out var rightLength))
      return string.CompareOrdinal(left.ToString(), right.ToString());

    return leftBytes[..leftLength].SequenceCompareTo(rightBytes[..rightLength]);
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
