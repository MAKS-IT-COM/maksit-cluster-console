using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class HelmDetailsViewModel : ObservableObject {
  private readonly ClusterWorkspace _workspace;
  private readonly Func<ResourceRow, bool> _stillCurrent;
  private readonly Action<string> _setOverview;
  private readonly Action<string> _setEvents;
  private readonly Action<string> _setYaml;
  private bool _syncing;

  public HelmDetailsViewModel(
    ClusterWorkspace workspace,
    Func<ResourceRow, bool> stillCurrent,
    Action<string> setOverview,
    Action<string> setEvents,
    Action<string> setYaml) {
    _workspace = workspace;
    _stillCurrent = stillCurrent;
    _setOverview = setOverview;
    _setEvents = setEvents;
    _setYaml = setYaml;
  }

  public ObservableCollection<HelmRevision> HelmRevisions { get; } = [];

  [ObservableProperty]
  private HelmRevision? selectedHelmRevision;

  [ObservableProperty]
  private HelmRevision? helmDiffFrom;

  [ObservableProperty]
  private string helmValuesText = "";

  [ObservableProperty]
  private string helmManifestText = "";

  [ObservableProperty]
  private string helmDiffText = "";

  public async Task LoadAsync(ResourceRow row) {
    var history = await _workspace.HelmHistoryAsync(row.Name, row.Namespace);

    if (!_stillCurrent(row))
      return;

    if (!history.IsSuccess || history.Value is null) {
      Clear();
      _setOverview(string.Join("; ", history.Messages));
      _setEvents("");
      _setYaml("");

      return;
    }

    _syncing = true;
    HelmRevisions.Clear();

    foreach (var revision in history.Value.OrderByDescending(item => item.Revision).ThenByDescending(item => item.Updated))
      HelmRevisions.Add(revision);

    var latest = HelmRevisions.FirstOrDefault();
    HelmDiffFrom = HelmRevisions.Skip(1).FirstOrDefault() ?? latest;
    SelectedHelmRevision = latest;
    _syncing = false;
    ApplySelection();
  }

  public void Clear() {
    _syncing = true;
    HelmRevisions.Clear();
    HelmDiffFrom = null;
    SelectedHelmRevision = null;
    _syncing = false;
    HelmValuesText = "";
    HelmManifestText = "";
    HelmDiffText = "";
  }

  partial void OnSelectedHelmRevisionChanged(HelmRevision? value) =>
    ApplySelection();

  partial void OnHelmDiffFromChanged(HelmRevision? value) =>
    ApplySelection();

  private void ApplySelection() {
    if (_syncing)
      return;

    var selected = SelectedHelmRevision;

    if (selected is null) {
      HelmValuesText = "";
      HelmManifestText = "";
      HelmDiffText = "";

      return;
    }

    HelmValuesText = selected.ValuesYaml;
    HelmManifestText = selected.Manifest;
    var from = HelmDiffFrom;
    HelmDiffText = from is null
      ? ""
      : "Values\n" + HelmRelease.Diff(from.ValuesYaml, selected.ValuesYaml)
        + "\nManifest\n" + HelmRelease.Diff(from.Manifest, selected.Manifest);

    var overview = new List<string> { $"{selected.Chart} · {selected.Status}" };

    if (!string.IsNullOrWhiteSpace(selected.AppVersion))
      overview.Add("App " + selected.AppVersion);

    if (!string.IsNullOrWhiteSpace(selected.Description))
      overview.Add(selected.Description);

    _setOverview(string.Join('\n', overview));
    _setEvents("");
    _setYaml(selected.Manifest);
  }
}
