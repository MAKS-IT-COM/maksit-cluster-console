using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class LimitRowViewModel : ObservableObject {
  public LimitRowViewModel(WorkloadContainerLimit source) {
    Source = source;
    cpuLimit = source.CpuLimit;
    memoryLimit = source.MemoryLimit;
  }

  public WorkloadContainerLimit Source { get; private set; }

  public void SyncFrom(WorkloadContainerLimit source) {
    var keepCpu = CpuLimit;
    var keepMemory = MemoryLimit;
    var wasDirty = IsDirty;
    Source = source;
    CpuLimit = wasDirty ? keepCpu : source.CpuLimit;
    MemoryLimit = wasDirty ? keepMemory : source.MemoryLimit;
    OnPropertyChanged(nameof(Workload));
    OnPropertyChanged(nameof(Namespace));
    OnPropertyChanged(nameof(Container));
    OnPropertyChanged(nameof(Pods));
    OnPropertyChanged(nameof(CpuRequest));
    OnPropertyChanged(nameof(MemoryRequest));
    OnPropertyChanged(nameof(CpuShare));
    OnPropertyChanged(nameof(MemoryShare));
    OnPropertyChanged(nameof(IsDirty));
  }

  public string Workload => Source.Workload;

  public string Namespace => Source.Namespace;

  public string Container => Source.ContainerLabel;

  public int Pods => Source.Pods;

  public string CpuRequest => Source.CpuRequest;

  public string MemoryRequest => Source.MemoryRequest;

  public string CpuShare => KubeQuantity.FormatCoresFixed(Source.CpuContribution);

  public string MemoryShare => KubeQuantity.FormatBytesCompact((long)Math.Round(Source.MemoryContribution));

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsDirty))]
  private string cpuLimit;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsDirty))]
  private string memoryLimit;

  public bool IsDirty =>
    !string.Equals(CpuLimit, Source.CpuLimit, StringComparison.Ordinal)
    || !string.Equals(MemoryLimit, Source.MemoryLimit, StringComparison.Ordinal);
}
