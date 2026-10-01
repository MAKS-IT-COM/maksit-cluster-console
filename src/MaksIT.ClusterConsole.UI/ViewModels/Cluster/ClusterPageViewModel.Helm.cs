using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class ClusterPageViewModel {
  private bool _syncingHelm;

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

  private async Task LoadHelmDetailsAsync(ResourceRow row) {
    var history = await _workspace.HelmHistoryAsync(row.Name, row.Namespace);
    if (!DetailsStillCurrent(row))
      return;

    if (!history.IsSuccess || history.Value is null) {
      ClearHelmDetails();
      OverviewText = string.Join("; ", history.Messages);
      EventsText = "";
      SetYaml("");
      return;
    }

    _syncingHelm = true;
    HelmRevisions.Clear();
    foreach (var revision in history.Value.OrderByDescending(item => item.Revision).ThenByDescending(item => item.Updated))
      HelmRevisions.Add(revision);
    var latest = HelmRevisions.FirstOrDefault();
    HelmDiffFrom = HelmRevisions.Skip(1).FirstOrDefault() ?? latest;
    SelectedHelmRevision = latest;
    _syncingHelm = false;
    ApplyHelmSelection();
  }

  private void ClearHelmDetails() {
    _syncingHelm = true;
    HelmRevisions.Clear();
    HelmDiffFrom = null;
    SelectedHelmRevision = null;
    _syncingHelm = false;
    HelmValuesText = "";
    HelmManifestText = "";
    HelmDiffText = "";
  }

  partial void OnSelectedHelmRevisionChanged(HelmRevision? value) =>
    ApplyHelmSelection();

  partial void OnHelmDiffFromChanged(HelmRevision? value) =>
    ApplyHelmSelection();

  private void ApplyHelmSelection() {
    if (_syncingHelm)
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
    OverviewText = string.Join('\n', overview);
    EventsText = "";
    SetYaml(selected.Manifest);
  }
}
