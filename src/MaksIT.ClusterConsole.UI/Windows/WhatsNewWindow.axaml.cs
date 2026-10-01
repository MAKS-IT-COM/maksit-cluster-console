using Avalonia.Controls;
using Avalonia.Interactivity;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI.Windows;


public partial class WhatsNewWindow : Window {
  public WhatsNewWindow() {
    InitializeComponent();
  }

  public static async Task ShowIfNeededAsync(Window owner, ConfigurationFileService configuration) {
    var current = AppInfo.Version;
    if (string.IsNullOrWhiteSpace(current))
      return;

    if (!configuration.FileExisted) {
      Remember(configuration, current);
      return;
    }

    var notes = ReleaseNotes.AddedSince(ReleaseNotes.Text(), configuration.Current.WhatsNewSeenVersion, current);
    if (notes.Count == 0)
      return;

    var model = new WhatsNewViewModel(notes);
    var window = new WhatsNewWindow {
      DataContext = model,
      WindowStartupLocation = WindowStartupLocation.CenterOwner
    };
    await window.ShowDialog(owner);
    if (model.DoNotShowAgain)
      Remember(configuration, current);
  }

  private static void Remember(ConfigurationFileService configuration, string version) {
    var cfg = configuration.Current;
    if (string.Equals(cfg.WhatsNewSeenVersion, version, StringComparison.Ordinal))
      return;
    cfg.WhatsNewSeenVersion = version;
    configuration.Save(cfg);
  }

  private void OnCloseClick(object? sender, RoutedEventArgs e) =>
    Close();
}
