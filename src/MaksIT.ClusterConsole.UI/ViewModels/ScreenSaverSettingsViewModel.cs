using Avalonia;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Controls.Footer;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class ScreenSaverSettingsViewModel : ObservableObject {
  private readonly Action<bool, int> _apply;

  public ScreenSaverSettingsViewModel(bool enabled, int idleMinutes, Action<bool, int> apply) {
    _apply = apply;
    this.enabled = enabled;
    this.idleMinutes = idleMinutes;
    FooterItems = [
      new FooterButton("Cancel", CancelCommand) {
        Edge = FooterEdge.Trailing,
        IsCancel = true,
        Padding = new Thickness(12, 7)
      },
      new FooterButton("Save", SaveCommand) {
        Edge = FooterEdge.Trailing,
        IsDefault = true,
        Padding = new Thickness(12, 7)
      }
    ];
  }

  [ObservableProperty]
  private bool enabled;

  [ObservableProperty]
  private int idleMinutes;

  public event Action<bool>? CloseRequested;

  public IReadOnlyList<FooterItem> FooterItems { get; }

  [RelayCommand]
  private void Save() {
    var minutes = IdleMinutes;

    if (minutes < ScreenSaverSettings.MinIdleMinutes || minutes > ScreenSaverSettings.MaxIdleMinutes)
      minutes = ScreenSaverSettings.DefaultIdleMinutes;

    _apply(Enabled, minutes);
    CloseRequested?.Invoke(true);
  }

  [RelayCommand]
  private void Cancel() =>
    CloseRequested?.Invoke(false);
}
