using System.Globalization;
using System.Text.RegularExpressions;


namespace MaksIT.ClusterConsole.Client;

public static class KubeQuantity {
  private static readonly Regex Pattern = new(@"^\s*([+-]?\d+(?:\.\d+)?)([a-zA-Z]*)\s*$", RegexOptions.Compiled);

  public static double ToCores(string? value) {
    if (!TrySplit(value, out var number, out var suffix))
      return 0;

    return suffix switch {
      "n" => number / 1_000_000_000d,
      "u" => number / 1_000_000d,
      "m" => number / 1_000d,
      "" => number,
      "k" or "K" => number * 1_000d,
      _ => number
    };
  }

  public static long ToBytes(string? value) {
    if (!TrySplit(value, out var number, out var suffix))
      return 0;

    var scale = suffix switch {
      "Ki" or "KiB" => 1024d,
      "Mi" or "MiB" => 1024d * 1024,
      "Gi" or "GiB" => 1024d * 1024 * 1024,
      "Ti" or "TiB" => 1024d * 1024 * 1024 * 1024,
      "Pi" or "PiB" => 1024d * 1024 * 1024 * 1024 * 1024,
      "k" or "K" => 1_000d,
      "M" => 1_000_000d,
      "G" => 1_000_000_000d,
      "T" => 1_000_000_000_000d,
      "m" => 0.001d,
      "" => 1d,
      _ => 1d
    };

    return (long)Math.Round(number * scale);
  }

  public static string FormatCores(double cores) {
    if (cores < 1)
      return $"{cores * 1000:0}m";
    return cores.ToString("0.##", CultureInfo.InvariantCulture);
  }

  public static string FormatCoresFixed(double cores) =>
    cores.ToString("0.00", CultureInfo.InvariantCulture);

  public static string FormatBytes(long bytes) {
    const double gi = 1024d * 1024 * 1024;
    const double mi = 1024d * 1024;
    if (bytes >= gi)
      return $"{bytes / gi:0.##} GiB";
    if (bytes >= mi)
      return $"{bytes / mi:0.##} MiB";
    return $"{bytes} B";
  }

  public static string FormatBytesCompact(long bytes) {
    const double gi = 1024d * 1024 * 1024;
    const double mi = 1024d * 1024;
    if (bytes >= gi)
      return $"{bytes / gi:0.0}GiB";
    if (bytes >= mi)
      return $"{bytes / mi:0.0}MiB";
    return $"{bytes}B";
  }

  public static string FormatMegabytes(long bytes) {
    const double mi = 1024d * 1024;
    var megabytes = bytes / mi;
    if (megabytes < 1)
      return "<1 MB";

    return $"{Math.Round(megabytes):0} MB";
  }

  public static string FormatMemoryQuantity(long bytes) {
    if (bytes <= 0)
      return "0";

    const long ki = 1024;
    const long mi = 1024 * 1024;
    const long gi = 1024L * 1024 * 1024;
    if (bytes % gi == 0)
      return $"{bytes / gi}Gi";
    if (bytes % mi == 0)
      return $"{bytes / mi}Mi";
    if (bytes % ki == 0)
      return $"{bytes / ki}Ki";
    if (bytes >= mi)
      return $"{(bytes / (double)mi).ToString("0.##", CultureInfo.InvariantCulture)}Mi";
    if (bytes >= ki)
      return $"{(bytes / (double)ki).ToString("0.##", CultureInfo.InvariantCulture)}Ki";

    return bytes.ToString(CultureInfo.InvariantCulture);
  }

  private static bool TrySplit(string? value, out double number, out string suffix) {
    number = 0;
    suffix = "";
    if (string.IsNullOrWhiteSpace(value) || value == "-")
      return false;

    var trimmed = value.Trim().Trim('"');
    var match = Pattern.Match(trimmed);
    if (!match.Success)
      return false;

    if (!double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out number))
      return false;

    suffix = match.Groups[2].Value;
    return true;
  }
}
