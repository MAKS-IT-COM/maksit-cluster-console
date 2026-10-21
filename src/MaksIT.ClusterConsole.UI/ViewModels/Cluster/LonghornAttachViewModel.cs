using CommunityToolkit.Mvvm.ComponentModel;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class LonghornAttachViewModel : ObservableObject {
  public LonghornAttachViewModel(
    IReadOnlyList<string> volumeNames,
    IReadOnlyList<string> nodes,
    bool disableFrontend,
    string notice) {
    VolumeNames = string.Join(", ", volumeNames);
    Nodes = nodes;
    DisableFrontend = disableFrontend;
    Notice = notice;
    SelectedNode = nodes.Count > 0 ? nodes[0] : null;
  }

  public string VolumeNames { get; }

  public IReadOnlyList<string> Nodes { get; }

  public string Notice { get; }

  public bool ShowNotice => !string.IsNullOrWhiteSpace(Notice);

  [ObservableProperty]
  private string? selectedNode;

  [ObservableProperty]
  private bool disableFrontend;
}
