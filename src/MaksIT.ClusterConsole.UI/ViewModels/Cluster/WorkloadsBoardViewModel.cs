using System.Collections.ObjectModel;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public sealed class WorkloadsBoardViewModel {
  private readonly ClusterWorkspace _workspace;
  private readonly Func<string> _selectedNamespace;
  private readonly Func<string> _contextName;
  private readonly Action<string> _setOverview;
  private readonly Action<string> _setStatus;
  private readonly Action<string> _openKind;

  public WorkloadsBoardViewModel(
    ClusterWorkspace workspace,
    Func<string> selectedNamespace,
    Func<string> contextName,
    Action<string> setOverview,
    Action<string> setStatus,
    Action<string> openKind) {
    _workspace = workspace;
    _selectedNamespace = selectedNamespace;
    _contextName = contextName;
    _setOverview = setOverview;
    _setStatus = setStatus;
    _openKind = openKind;
  }

  public ObservableCollection<WorkloadKindCount> WorkloadCounts { get; } = [];

  public async Task LoadAsync() {
    WorkloadCounts.Clear();

    if (_workspace.Session is null)
      return;

    var kinds = new[] {
      "pods", "deployments", "statefulsets", "daemonsets",
      "replicasets", "jobs", "cronjobs", "replicationcontrollers"
    };

    foreach (var id in kinds) {
      var listed = await _workspace.ListAsync(id, _selectedNamespace(), null);
      var count = listed.IsSuccess ? listed.Value?.Count ?? 0 : 0;
      WorkloadCounts.Add(new WorkloadKindCount {
        Id = id,
        Title = ResourceCatalog.Find(id)?.Title ?? id,
        Count = count,
        Open = _openKind
      });
    }

    var ns = _selectedNamespace();
    _setOverview(ns == Configuration.AllNamespaces
      ? "Counts for all namespaces."
      : $"Counts in namespace {ns}.");
    _setStatus($"Workloads · {_contextName()}");
  }
}
