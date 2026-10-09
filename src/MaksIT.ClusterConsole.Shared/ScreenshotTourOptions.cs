using MaksIT.Core.Desktop.Snapshots;


namespace MaksIT.ClusterConsole.Shared;


/// <summary>
/// Screenshot tour requested with <c>--screenshots</c>. Null when that flag is absent.
/// </summary>
public sealed class ScreenshotTourOptions {
  public const string WelcomeView = "welcome";

  public static readonly string[] DefaultViews = [WelcomeView];

  public required string Directory { get; init; }

  public string? Context { get; init; }

  public IReadOnlyList<string> Views { get; init; } = DefaultViews;

  public bool AllFeatures { get; init; }

  public int SettleMilliseconds { get; init; } = 600;

  /// <summary>
  /// Reads <c>--screenshots</c>, <c>--context</c>, <c>--views</c>, and <c>--settle-ms</c>.
  /// Returns false when a screenshot flag is present but incomplete.
  /// With no <c>--views</c>, the tour is the welcome screen plus every feature.
  /// </summary>
  public static bool TryParse(IReadOnlyList<string>? args, out ScreenshotTourOptions? options, out string? error) {
    options = null;

    if (!AppViewSnapshotOptions.TryParse(args, out var snapshot, out error))
      return false;

    if (snapshot is null)
      return true;

    string? context = null;

    if (args is not null) {
      for (var i = 0; i < args.Count; i++) {
        if (args[i] != "--context")
          continue;

        if (!TryTake(args, ref i, "--context", out context, out error))
          return false;
      }
    }

    options = new ScreenshotTourOptions {
      Directory = snapshot.Directory,
      Context = string.IsNullOrWhiteSpace(context) ? null : context,
      Views = snapshot.Views.Count == 0 ? DefaultViews : snapshot.Views,
      AllFeatures = snapshot.Views.Count == 0,
      SettleMilliseconds = snapshot.SettleMilliseconds
    };

    return true;
  }

  private static bool TryTake(
    IReadOnlyList<string> args,
    ref int index,
    string flag,
    out string? value,
    out string? error) {
    value = null;
    error = null;
    var next = index + 1;

    if (next >= args.Count || args[next].StartsWith("--", StringComparison.Ordinal)) {
      error = $"Pass a value after {flag}.";

      return false;
    }

    index = next;
    value = args[next];

    return true;
  }
}
