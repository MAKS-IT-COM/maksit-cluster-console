using System.IO;
using System.Text;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public sealed record LocalFilePick(string Name, byte[] Bytes);

public partial class VolumeFilesViewModel : ObservableObject {
  private readonly ClusterWorkspace _workspace;
  private readonly ResourceRow _row;
  private byte[]? _fileBytes;
  private bool _loadingEditor;
  private bool _listFailed;

  public VolumeFilesViewModel(ClusterWorkspace workspace, ResourceRow row, string? kind = null) {
    _workspace = workspace;
    _row = row;
    if (!string.IsNullOrEmpty(kind) && row.Document["kind"] is null)
      row.Document["kind"] = kind;
  }

  public ObservableCollection<VolumeMountTarget> Mounts { get; } = [];

  public ObservableCollection<VolumeEntry> Entries { get; } = [];

  public string WindowTitle =>
    "Volume files · " + _row.Name;

  [ObservableProperty]
  private VolumeMountTarget? selectedMount;

  [ObservableProperty]
  private VolumeEntry? selectedEntry;

  [ObservableProperty]
  private string currentPath = "";

  [ObservableProperty]
  private string status = "Resolving PVC…";

  [ObservableProperty]
  private string identity = "";

  [ObservableProperty]
  private string editorText = "";

  [ObservableProperty]
  private string editorHint = "Select a file.";

  [ObservableProperty]
  private bool canEdit;

  [ObservableProperty]
  private bool isDirty;

  [ObservableProperty]
  private string? openFilePath;

  public bool EditorReadOnly =>
    !CanEdit;

  public bool CanGoUp =>
    !string.IsNullOrEmpty(CurrentPath);

  public bool CanDownload =>
    SelectedEntry is { IsDirectory: false } || OpenFilePath is not null;

  public bool CanUpload =>
    SelectedMount is not null;

  public bool CanSave =>
    CanEdit && IsDirty && OpenFilePath is not null;

  public event Func<string, Task<string?>>? PickSavePath;

  public event Func<Task<LocalFilePick?>>? PickOpenFile;

  partial void OnSelectedMountChanged(VolumeMountTarget? value) {
    CurrentPath = "";
    ClearEditor();
    _ = LoadEntriesAsync();
    _ = LoadIdentityAsync();
  }

  partial void OnCurrentPathChanged(string value) {
    OnPropertyChanged(nameof(CanGoUp));
    GoUpCommand.NotifyCanExecuteChanged();
  }

  partial void OnSelectedEntryChanged(VolumeEntry? value) =>
    OnPropertyChanged(nameof(CanDownload));

  partial void OnOpenFilePathChanged(string? value) {
    OnPropertyChanged(nameof(CanDownload));
    OnPropertyChanged(nameof(CanSave));
  }

  partial void OnCanEditChanged(bool value) {
    OnPropertyChanged(nameof(CanSave));
    OnPropertyChanged(nameof(EditorReadOnly));
  }

  partial void OnIsDirtyChanged(bool value) =>
    OnPropertyChanged(nameof(CanSave));

  partial void OnEditorTextChanged(string value) {
    if (_loadingEditor)
      return;

    IsDirty = true;
  }

  [RelayCommand]
  private async Task LoadAsync() {
    Mounts.Clear();
    var listed = await _workspace.ListVolumeMountsAsync(_row.Document);
    if (!listed.IsSuccess) {
      Status = string.Join("; ", listed.Messages);
      return;
    }

    foreach (var mount in listed.Value ?? [])
      Mounts.Add(mount);

    SelectedMount = Mounts.FirstOrDefault();
    if (SelectedMount is null)
      Status = "No running pod is mounting this PVC. Attach a workload first.";
  }

  [RelayCommand]
  private async Task LoadEntriesAsync() {
    if (SelectedMount is null)
      return;

    var listed = await _workspace.ListVolumeEntriesAsync(SelectedMount, CurrentPath);
    Entries.Clear();
    if (!listed.IsSuccess) {
      _listFailed = true;
      Status = string.Join("; ", listed.Messages);
      return;
    }

    _listFailed = false;
    foreach (var entry in listed.Value ?? [])
      Entries.Add(entry);

    Status = StatusLine(null);
    OnPropertyChanged(nameof(CanUpload));
  }

  [RelayCommand]
  private async Task RefreshAsync() =>
    await LoadEntriesAsync();

  [RelayCommand(CanExecute = nameof(CanGoUp))]
  private async Task GoUpAsync() {
    CurrentPath = VolumePath.ParentRelative(CurrentPath);
    ClearEditor();
    GoUpCommand.NotifyCanExecuteChanged();
    await LoadEntriesAsync();
  }

  [RelayCommand]
  private async Task OpenEntryAsync(VolumeEntry? entry) {
    entry ??= SelectedEntry;
    if (entry is null || SelectedMount is null)
      return;

    if (entry.IsDirectory) {
      CurrentPath = VolumePath.CombineRelative(CurrentPath, entry.Name);
      ClearEditor();
      GoUpCommand.NotifyCanExecuteChanged();
      await LoadEntriesAsync();
      return;
    }

    var relative = VolumePath.CombineRelative(CurrentPath, entry.Name);
    var read = await _workspace.ReadVolumeFileAsync(SelectedMount, relative);
    if (!read.IsSuccess || read.Value is null) {
      Status = string.Join("; ", read.Messages);
      return;
    }

    _fileBytes = read.Value;
    OpenFilePath = relative;
    _loadingEditor = true;
    if (VolumeText.CanEdit(read.Value)) {
      CanEdit = true;
      EditorHint = relative;
      EditorText = Encoding.UTF8.GetString(read.Value);
    }
    else {
      CanEdit = false;
      EditorText = "";
      EditorHint = relative + (read.Value.Length > VolumeText.MaxEditBytes
        ? " — too large to edit (use Download)."
        : " — binary file (use Download).");
    }

    _loadingEditor = false;
    IsDirty = false;
    Status = StatusLine(relative);
  }

  [RelayCommand(CanExecute = nameof(CanSave))]
  private async Task SaveAsync() {
    if (SelectedMount is null || OpenFilePath is null || !CanEdit)
      return;

    var bytes = Encoding.UTF8.GetBytes(EditorText);
    var written = await _workspace.WriteVolumeFileAsync(SelectedMount, OpenFilePath, bytes);
    if (!written.IsSuccess) {
      Status = string.Join("; ", written.Messages);
      return;
    }

    _fileBytes = bytes;
    IsDirty = false;
    Status = StatusLine(OpenFilePath) + "  ·  saved";
    await LoadEntriesAsync();
  }

  [RelayCommand(CanExecute = nameof(CanDownload))]
  private async Task DownloadAsync() {
    if (PickSavePath is null || SelectedMount is null)
      return;

    var relative = OpenFilePath;
    if (relative is null && SelectedEntry is { IsDirectory: false } file)
      relative = VolumePath.CombineRelative(CurrentPath, file.Name);
    if (relative is null)
      return;

    var suggested = relative[(relative.LastIndexOf('/') + 1)..];
    var path = await PickSavePath(suggested);
    if (string.IsNullOrWhiteSpace(path))
      return;

    var bytes = _fileBytes;
    if (bytes is null || !string.Equals(OpenFilePath, relative, StringComparison.Ordinal)) {
      var read = await _workspace.ReadVolumeFileAsync(SelectedMount, relative);
      if (!read.IsSuccess || read.Value is null) {
        Status = string.Join("; ", read.Messages);
        return;
      }

      bytes = read.Value;
    }

    await File.WriteAllBytesAsync(path, bytes);
    Status = StatusLine(relative) + "  ·  downloaded";
  }

  [RelayCommand(CanExecute = nameof(CanUpload))]
  private async Task UploadAsync() {
    if (PickOpenFile is null || SelectedMount is null)
      return;

    var pick = await PickOpenFile();
    if (pick is null)
      return;

    var relative = VolumePath.CombineRelative(CurrentPath, pick.Name);
    var written = await _workspace.WriteVolumeFileAsync(SelectedMount, relative, pick.Bytes);
    if (!written.IsSuccess) {
      Status = string.Join("; ", written.Messages);
      return;
    }

    Status = StatusLine(relative) + "  ·  uploaded";
    await LoadEntriesAsync();
  }

  private async Task LoadIdentityAsync() {
    if (SelectedMount is null)
      return;

    var result = await _workspace.GetVolumeIdentityAsync(SelectedMount);
    Identity = result.IsSuccess ? result.Value ?? "" : "";
    if (!_listFailed)
      Status = StatusLine(OpenFilePath);
  }

  private void ClearEditor() {
    _loadingEditor = true;
    OpenFilePath = null;
    _fileBytes = null;
    CanEdit = false;
    EditorText = "";
    EditorHint = "Select a file.";
    IsDirty = false;
    _loadingEditor = false;
  }

  private string StatusLine(string? file) {
    if (SelectedMount is null)
      return Status;

    var parts = new List<string> {
      SelectedMount.Namespace + "/" + SelectedMount.PodName,
      SelectedMount.Container
    };
    if (!string.IsNullOrEmpty(Identity))
      parts.Add(Identity);

    parts.Add(SelectedMount.Root + (string.IsNullOrEmpty(CurrentPath) ? "" : "/" + CurrentPath));
    if (!string.IsNullOrEmpty(file))
      parts.Add(file);

    return string.Join("  ·  ", parts);
  }
}
