using Avalonia.Controls;
using MaksIT.ClusterConsole.Shared;
using CoreWhatsNew = MaksIT.Core.UI.WhatsNew.WhatsNewWindow;


namespace MaksIT.ClusterConsole.UI.Windows;

public static class WhatsNewWindow {
  public static Task ShowIfNeededAsync(Window owner, ConfigurationFileService configuration) {
    var cfg = configuration.Current;

    return CoreWhatsNew.ShowIfNeededAsync(
      owner,
      ReleaseNotes.Text(),
      AppInfo.Version,
      cfg.WhatsNewSeenVersion,
      configuration.FileExisted,
      version => Remember(configuration, version));
  }

  private static void Remember(ConfigurationFileService configuration, string version) {
    var cfg = configuration.Current;

    if (string.Equals(cfg.WhatsNewSeenVersion, version, StringComparison.Ordinal))
      return;

    cfg.WhatsNewSeenVersion = version;
    configuration.Save(cfg);
  }
}
