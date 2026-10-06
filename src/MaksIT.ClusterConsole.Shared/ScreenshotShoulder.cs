namespace MaksIT.ClusterConsole.Shared;

/// <summary>
/// Right-shoulder tabs for the screenshot tour.
/// The page PNG is the Overview tab after the first row is selected.
/// Further shots walk every visible tab. Chat is never saved.
/// Add a navigator id to <see cref="SkipByView"/> to leave more tabs out,
/// and to <see cref="BlurByView"/> when that tab's text can be a secret.
/// Data and Helm values are always blurred.
/// </summary>
public static class ScreenshotShoulder {
  public static bool Skip(string viewId, string tab) {
    if (tab is DetailTab.Overview or DetailTab.Chat)
      return true;

    return SkipByView.TryGetValue(viewId, out var tabs) && tabs.Contains(tab);
  }

  public static bool Blur(string viewId, string tab) {
    if (AlwaysBlur.Contains(tab))
      return true;

    return BlurByView.TryGetValue(viewId, out var tabs) && tabs.Contains(tab);
  }

  /// <summary>
  /// Tabs left out for one navigator id, besides Overview and Chat.
  /// </summary>
  private static readonly Dictionary<string, HashSet<string>> SkipByView = new(StringComparer.Ordinal);

  private static readonly HashSet<string> AlwaysBlur = new(StringComparer.Ordinal) {
    DetailTab.Data,
    DetailTab.Values
  };

  /// <summary>
  /// Extra tabs whose body is blurred for one navigator id.
  /// </summary>
  private static readonly Dictionary<string, HashSet<string>> BlurByView = new(StringComparer.Ordinal) {
    ["secrets"] = new(StringComparer.Ordinal) { DetailTab.Yaml },
    ["configmaps"] = new(StringComparer.Ordinal) { DetailTab.Yaml },
    ["components"] = new(StringComparer.Ordinal) { DetailTab.Yaml },
    [ResourceCatalog.HelmReleasesId] = new(StringComparer.Ordinal) { DetailTab.Manifest }
  };
}
