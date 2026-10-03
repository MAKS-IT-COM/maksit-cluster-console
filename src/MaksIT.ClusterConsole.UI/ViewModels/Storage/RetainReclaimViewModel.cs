using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.UI.Controls.Footer;


namespace MaksIT.ClusterConsole.UI.ViewModels.Storage;

public partial class RetainReclaimViewModel : ObservableObject {
  private readonly IClusterSession _session;
  private readonly string? _storageClassName;
  private readonly IReadOnlyList<string> _volumeNames;

  private RetainReclaimViewModel(IClusterSession session, string? storageClassName, IReadOnlyList<string>? volumeNames) {
    _session = session;
    _storageClassName = storageClassName;
    _volumeNames = volumeNames ?? [];
    FooterItems = [
      new FooterButton("Cancel", CancelCommand) { Edge = FooterEdge.Trailing },
      new FooterButton("Apply", ApplyCommand) { Edge = FooterEdge.Trailing }
    ];
  }

  public IReadOnlyList<FooterItem> FooterItems { get; }

  public static RetainReclaimViewModel ForStorageClass(IClusterSession session, string name) =>
    new(session, name, null);

  public static RetainReclaimViewModel ForVolumes(IClusterSession session, IReadOnlyList<string> names) =>
    new(session, null, names);

  public bool ShowClassOption => _storageClassName is not null;

  public IReadOnlyList<string> Policies { get; } = ReclaimPolicy.Choices;

  public string Title =>
    _storageClassName is null ? "Reclaim policy" : $"Reclaim · {_storageClassName}";

  public string Explanation =>
    _storageClassName is null
      ? "Sets persistentVolumeReclaimPolicy on the selected volumes."
      : "A storage class reclaimPolicy cannot be edited in place. Existing volumes keep their own policy until they are patched. Future volumes pick up the new policy only after this class is deleted and created again with the same name. New claims fail during that short gap. A GitOps controller that owns the class can put the old policy back.";

  [ObservableProperty]
  private bool loaded;

  [ObservableProperty]
  private bool isBusy;

  [ObservableProperty]
  private bool classNeedsChange;

  [ObservableProperty]
  private bool isDefaultClass;

  [ObservableProperty]
  private string selectedPolicy = ReclaimPolicy.Retain;

  [ObservableProperty]
  private bool updateClass;

  [ObservableProperty]
  private bool updateVolumes;

  [ObservableProperty]
  private string classPolicyText = "";

  [ObservableProperty]
  private string volumeOptionText = "";

  [ObservableProperty]
  private string error = "";

  [ObservableProperty]
  private string recoveryYaml = "";

  [ObservableProperty]
  private bool hasRecoveryYaml;

  [ObservableProperty]
  private IReadOnlyList<ReclaimVolume> volumes = [];

  public string StatusText { get; private set; } = "";

  public bool Changed { get; private set; }

  public bool CanCancel => !IsBusy;

  public bool ShowLoading => IsBusy && !Loaded;

  public bool ShowEmptyVolumes => Loaded && ShowClassOption && _sourceVolumes.Count == 0;

  public bool ShowClassMatches =>
    Loaded && ShowClassOption && ReclaimPolicy.Same(_classPolicy, SelectedPolicy);

  public string ClassMatchText =>
    $"This storage class already uses {SelectedPolicy}. New volumes keep that policy.";

  public string ClassOptionText =>
    $"Recreate this storage class with {SelectedPolicy}";

  public string PolicyHint =>
    ReclaimPolicy.Same(SelectedPolicy, ReclaimPolicy.Delete)
      ? "Delete removes the disk when the claim is deleted."
      : "Retain leaves the disk in place after the claim is deleted.";

  public bool HasVolumes => Volumes.Count > 0;

  public bool HasVolumesToChange => Volumes.Any(volume => volume.WillChange);

  public bool ShowVolumeOption => ShowClassOption && HasVolumesToChange;

  private string _classPolicy = "";

  private IReadOnlyList<ReclaimVolume> _sourceVolumes = [];

  private bool _applyingPreview;

  public bool CanApply {
    get {
      if (IsBusy || !Loaded)
        return false;

      var volumeCount = Volumes.Count(volume => volume.WillChange);

      if (!ShowClassOption)
        return volumeCount > 0;

      return (UpdateClass && ClassNeedsChange) || (UpdateVolumes && volumeCount > 0);
    }
  }

  public event Action? CloseRequested;

  partial void OnIsBusyChanged(bool value) {
    OnPropertyChanged(nameof(CanCancel));
    OnPropertyChanged(nameof(ShowLoading));
    ApplyCommand.NotifyCanExecuteChanged();
    CancelCommand.NotifyCanExecuteChanged();
  }

  partial void OnLoadedChanged(bool value) {
    OnPropertyChanged(nameof(ShowLoading));
    OnPropertyChanged(nameof(ShowEmptyVolumes));
    OnPropertyChanged(nameof(ShowClassMatches));
    ApplyCommand.NotifyCanExecuteChanged();
  }

  partial void OnSelectedPolicyChanged(string value) {
    OnPropertyChanged(nameof(PolicyHint));
    OnPropertyChanged(nameof(ClassMatchText));
    OnPropertyChanged(nameof(ClassOptionText));

    if (!_applyingPreview && Loaded)
      ApplySelection();
  }

  partial void OnVolumesChanged(IReadOnlyList<ReclaimVolume> value) {
    OnPropertyChanged(nameof(HasVolumes));
    OnPropertyChanged(nameof(HasVolumesToChange));
    OnPropertyChanged(nameof(ShowVolumeOption));
    OnPropertyChanged(nameof(ShowEmptyVolumes));
    ApplyCommand.NotifyCanExecuteChanged();
  }

  partial void OnUpdateClassChanged(bool value) =>
    ApplyCommand.NotifyCanExecuteChanged();

  partial void OnUpdateVolumesChanged(bool value) =>
    ApplyCommand.NotifyCanExecuteChanged();

  partial void OnErrorChanged(string value) =>
    OnPropertyChanged(nameof(ShowLoading));

  public async Task LoadAsync() {
    IsBusy = true;
    Error = "";

    try {
      var preview = _storageClassName is null
        ? await _session.PreviewPersistentVolumeReclaimAsync(_volumeNames)
        : await _session.PreviewStorageClassReclaimAsync(_storageClassName);

      if (!preview.IsSuccess || preview.Value is null) {
        Error = preview.Messages is { Count: > 0 } ? string.Join(" ", preview.Messages) : "Could not read reclaim policy.";

        return;
      }

      ApplyPreview(preview.Value);
      Loaded = true;
    }
    catch (Exception ex) {
      Error = ex.Message;
    }
    finally {
      IsBusy = false;
    }
  }

  [RelayCommand(CanExecute = nameof(CanApply))]
  private async Task ApplyAsync() {
    IsBusy = true;
    Error = "";

    try {
      var policy = ReclaimPolicy.Normalize(SelectedPolicy) ?? ReclaimPolicy.Retain;
      var result = _storageClassName is null
        ? await _session.ApplyPersistentVolumeReclaimAsync(_volumeNames, policy)
        : await _session.ApplyStorageClassReclaimAsync(
          _storageClassName,
          policy,
          UpdateVolumes && Volumes.Any(volume => volume.WillChange),
          UpdateClass && ClassNeedsChange);
      var outcome = result.Value;

      if (outcome is not null) {
        if (outcome.VolumesPatched > 0 || outcome.ClassRecreated)
          Changed = true;

        StatusText = outcome.Summary;

        if (outcome.RecoveryDocument is not null) {
          RecoveryYaml = YamlFormatter.FromJson(outcome.RecoveryDocument);
          HasRecoveryYaml = RecoveryYaml.Length > 0;
        }
      }

      if (result.IsSuccess && outcome is { Errors.Count: 0 }) {
        CloseRequested?.Invoke();

        return;
      }

      Error = outcome?.Summary ?? (result.Messages is { Count: > 0 } ? string.Join(" ", result.Messages) : "Reclaim policy change failed.");
    }
    catch (Exception ex) {
      Error = ex.Message;
    }
    finally {
      IsBusy = false;
    }
  }

  [RelayCommand(CanExecute = nameof(CanCancel))]
  private void Cancel() =>
    CloseRequested?.Invoke();

  private void ApplyPreview(StorageReclaimPreview preview) {
    _sourceVolumes = preview.Volumes;
    _classPolicy = preview.ClassPolicy;
    IsDefaultClass = preview.IsDefaultClass;
    ClassPolicyText = preview.ClassPolicy.Length == 0 ? "" : $"Current reclaim policy: {preview.ClassPolicy}";
    _applyingPreview = true;

    try {
      SelectedPolicy = DefaultTarget(preview.ClassPolicy);
    }
    finally {
      _applyingPreview = false;
    }

    ApplySelection();
  }

  private void ApplySelection() {
    var policy = ReclaimPolicy.Normalize(SelectedPolicy) ?? ReclaimPolicy.Retain;

    Volumes = _sourceVolumes.Select(volume => volume with {
      WillChange = volume.Phase != "Missing" && !ReclaimPolicy.Same(volume.Policy, policy)
    }).ToList();

    ClassNeedsChange = ShowClassOption && !ReclaimPolicy.Same(_classPolicy, policy);

    OnPropertyChanged(nameof(ShowClassMatches));

    UpdateClass = ClassNeedsChange;

    UpdateVolumes = Volumes.Any(volume => volume.WillChange);

    var changing = Volumes.Count(volume => volume.WillChange);
    VolumeOptionText = changing switch {
      0 => $"No volumes need {policy}",
      1 => $"Set {policy} on 1 volume",
      _ => $"Set {policy} on {changing} volumes"
    };
    OnPropertyChanged(nameof(ShowEmptyVolumes));
    ApplyCommand.NotifyCanExecuteChanged();
  }

  private static string DefaultTarget(string current) =>
    current.Equals(ReclaimPolicy.Retain, StringComparison.OrdinalIgnoreCase)
      ? ReclaimPolicy.Delete
      : ReclaimPolicy.Retain;
}
