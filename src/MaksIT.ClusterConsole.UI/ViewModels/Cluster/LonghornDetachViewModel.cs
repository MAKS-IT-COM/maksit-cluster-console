using CommunityToolkit.Mvvm.ComponentModel;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class LonghornDetachViewModel : ObservableObject {
  public LonghornDetachViewModel(IReadOnlyList<string> volumeNames, string notice) {
    VolumeNames = string.Join(", ", volumeNames);
    Notice = notice;
  }

  public string VolumeNames { get; }

  public string Notice { get; }

  public bool ShowNotice => !string.IsNullOrWhiteSpace(Notice);

  [ObservableProperty]
  private bool forceDetach;
}
