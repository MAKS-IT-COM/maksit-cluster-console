using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.ViewModels;
using MaksIT.ClusterConsole.UI.Controls.Footer;


namespace MaksIT.ClusterConsole.UI.Windows;


public partial class WhatsNewWindow : Window {
  private bool _footerReady;

  public WhatsNewWindow() {
    InitializeComponent();
    DataContextChanged += (_, _) => WireFooter();
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

  private void WireFooter() {
    if (_footerReady || DataContext is not WhatsNewViewModel model)
      return;

    _footerReady = true;
    var remember = new FooterCheck("Do not show again");
    remember.PropertyChanged += (_, e) => {
      if (e.PropertyName == nameof(FooterCheck.IsChecked))
        model.DoNotShowAgain = remember.IsChecked;
    };
    Actions.Items = [
      remember,
      new FooterButton("Close", new RelayCommand(Close)) {
        Edge = FooterEdge.Trailing,
        IsDefault = true,
        IsCancel = true,
        Padding = new Thickness(12, 7)
      }
    ];
  }
}
