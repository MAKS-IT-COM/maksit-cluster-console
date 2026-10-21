using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.Results;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.S3;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.UI.Controls.Footer;


namespace MaksIT.ClusterConsole.UI.ViewModels.Storage;

public partial class S3BrowserViewModel : ObservableObject {
  private readonly IClusterSession _session;
  private readonly string _namespace;
  private readonly List<S3Entry> _all = [];
  private string _bucketNote = "";
  private CancellationTokenSource? _run;
  private bool _suppress;

  public S3BrowserViewModel(IClusterSession session, string? ns = null) {
    _session = session;
    _namespace = string.IsNullOrWhiteSpace(ns) ? "longhorn-system" : ns;
    FooterItems = [
      new FooterButton("Refresh", RefreshCommand),
      new FooterButton("Up", GoUpCommand),
      new FooterButton("Open", OpenEntryCommand),
      new FooterButton("New folder", NewFolderCommand),
      new FooterButton("Upload", UploadCommand),
      new FooterButton("Upload folder", UploadFolderCommand),
      new FooterButton("Download", DownloadCommand),
      new FooterButton("Delete", DeleteCommand),
      new FooterButton("Rename", RenameCommand),
      new FooterButton("Copy", CopyCommand)
    ];
  }

  public IReadOnlyList<FooterItem> FooterItems { get; }

  public ObservableCollection<S3Target> Targets { get; } = [];

  public ObservableCollection<S3Bucket> Buckets { get; } = [];

  public ObservableCollection<S3Entry> VisibleEntries { get; } = [];

  public Func<S3TargetViewModel, Task<bool>>? EditTarget { get; set; }

  public Func<S3FileViewModel, Task<bool>>? Preview { get; set; }

  public Func<string, string, Task<bool>>? Confirm { get; set; }

  public Func<string, string, string, Task<string?>>? Ask { get; set; }

  public Func<Task<IReadOnlyList<string>?>>? PickFiles { get; set; }

  public Func<string, Task<string?>>? PickFolder { get; set; }

  public Func<string, Task<string?>>? PickSavePath { get; set; }

  [ObservableProperty]
  private S3Target? selectedTarget;

  [ObservableProperty]
  private S3Bucket? selectedBucket;

  [ObservableProperty]
  private S3Entry? selectedEntry;

  [ObservableProperty]
  private string pathText = "";

  [ObservableProperty]
  private string filter = "";

  [ObservableProperty]
  private string status = "Loading targets…";

  [ObservableProperty]
  private bool isBusy;

  public bool InputsEnabled =>
    !IsBusy;

  public string TargetSummary {
    get {
      if (SelectedTarget is null)
        return "No S3 target yet. Add default for the Longhorn default backup target, or another name for a different bucket.";

      var target = SelectedTarget;
      var bucket = SelectedBucket?.Name ?? target.Bucket;
      var path = string.IsNullOrEmpty(PathText) ? bucket : bucket + "/" + S3Layout.Display(PathText);
      var where = string.IsNullOrEmpty(target.Endpoint) ? "Amazon S3 · " + target.Region : target.Endpoint;
      var count = Buckets.Count switch {
        0 => "",
        1 => " · 1 bucket",
        _ => " · " + Buckets.Count + " buckets"
      };

      return path + " · " + where + count;
    }
  }

  public void Cancel() =>
    _run?.Cancel();

  partial void OnSelectedTargetChanged(S3Target? value) {
    OnPropertyChanged(nameof(TargetSummary));
    Notify();

    if (_suppress || IsBusy)
      return;

    _ = OpenTargetAsync();
  }

  partial void OnSelectedBucketChanged(S3Bucket? value) {
    OnPropertyChanged(nameof(TargetSummary));
    Notify();

    if (_suppress || IsBusy)
      return;

    _ = OpenBucketAsync();
  }

  partial void OnSelectedEntryChanged(S3Entry? value) =>
    Notify();

  partial void OnPathTextChanged(string value) {
    OnPropertyChanged(nameof(TargetSummary));
    Notify();
  }

  partial void OnFilterChanged(string value) =>
    ApplyFilter();

  partial void OnIsBusyChanged(bool value) {
    OnPropertyChanged(nameof(InputsEnabled));
    Notify();
  }

  [RelayCommand]
  private Task LoadAsync() =>
    RunAsync(token => LoadTargetsAsync(token, null));

  [RelayCommand(CanExecute = nameof(CanRefresh))]
  private Task RefreshAsync() =>
    RunAsync(async token => {
      var prefer = SelectedBucket?.Name ?? SelectedTarget?.Bucket;
      await LoadBucketsAsync(token, prefer);

      if (prefer is not null && SelectedBucket?.Name != prefer)
        SetPath("");

      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanGoUp))]
  private Task GoUpAsync() =>
    RunAsync(async token => {
      SetPath(S3Layout.Parent(PathText));
      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanRefresh))]
  private Task BucketRootAsync() =>
    RunAsync(async token => {
      SetPath("");
      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanOpen))]
  private Task OpenEntryAsync() =>
    RunAsync(token => OpenAsync(SelectedEntry, token));

  [RelayCommand(CanExecute = nameof(CanRefresh))]
  private Task OpenPathAsync() =>
    RunAsync(ListFolderAsync);

  [RelayCommand(CanExecute = nameof(CanCreateBucket))]
  private Task NewBucketAsync() =>
    RunAsync(async token => {
      var target = SelectedTarget;

      if (target is null)
        return;

      var name = await AskNameAsync(
        "New bucket",
        "Lowercase letters, numbers, dots, and hyphens. 3 to 63 characters.",
        "");

      if (name is null)
        return;

      var created = await S3Storage.CreateBucketAsync(target, name, token);

      if (!created.IsSuccess) {
        Status = Text(created);

        return;
      }

      Status = "Created " + name.Trim() + ".";
      await LoadBucketsAsync(token, name.Trim());
      SetPath("");
      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanDeleteBucket))]
  private Task DeleteBucketAsync() =>
    RunAsync(async token => {
      var target = SelectedTarget;
      var bucket = SelectedBucket;

      if (target is null || bucket is null)
        return;

      if (bucket.Backup) {
        Status = bucket.Name + " is a Longhorn backup bucket.";

        return;
      }

      var accepted = await AskConfirmAsync(
        "Delete bucket",
        "Delete " + bucket.Name + " and everything inside it? This removes the bucket. A Longhorn backup bucket cannot be deleted here.");

      if (!accepted)
        return;

      var removed = await S3Storage.DeleteBucketAsync(target, bucket.Name, Report, token);

      if (!removed.IsSuccess) {
        Status = Text(removed);

        return;
      }

      Status = "Deleted " + bucket.Name + ".";
      await LoadBucketsAsync(token, target.Bucket);
      SetPath(SelectedBucket?.Name == target.Bucket ? target.Prefix : "");
      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanChange))]
  private Task NewFolderAsync() =>
    RunAsync(async token => {
      var target = OpenLocation();
      var prefix = PathText;

      if (target is null)
        return;

      var name = await AskNameAsync("New folder", "Name of the folder in this location.", "");

      if (name is null)
        return;

      if (!S3Layout.IsFolderName(name)) {
        Status = "Enter a folder name without slashes.";

        return;
      }

      var created = await S3Storage.CreateFolderAsync(target, S3Layout.Child(prefix, name), token);
      Status = created.IsSuccess ? "Created " + name + "." : Text(created);

      if (created.IsSuccess)
        await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanChange))]
  private Task UploadAsync() =>
    RunAsync(async token => {
      var target = OpenLocation();
      var prefix = PathText;

      if (target is null)
        return;

      var files = PickFiles is null ? null : await PickFiles();

      if (files is null || files.Count == 0) {
        Status = "Upload cancelled.";

        return;
      }

      foreach (var file in files) {
        var name = Path.GetFileName(file);
        var uploaded = await S3Storage.UploadAsync(target, S3Layout.UploadKey(prefix, name), file, Report, token);

        if (!uploaded.IsSuccess) {
          Status = Text(uploaded);

          return;
        }
      }

      Status = files.Count == 1 ? "Uploaded " + Path.GetFileName(files[0]) + "." : "Uploaded " + files.Count + " files.";
      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanChange))]
  private Task UploadFolderAsync() =>
    RunAsync(async token => {
      var target = OpenLocation();
      var prefix = PathText;

      if (target is null)
        return;

      var folder = PickFolder is null ? null : await PickFolder("Folder to upload");

      if (string.IsNullOrWhiteSpace(folder)) {
        Status = "Upload cancelled.";

        return;
      }

      List<string> files;

      try {
        files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
      }
      catch (Exception ex) {
        Status = ex.Message;

        return;
      }

      var root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

      foreach (var file in files) {
        var relative = Path.GetRelativePath(root, file);
        var uploaded = await S3Storage.UploadAsync(target, S3Layout.UploadKey(prefix, relative), file, Report, token);

        if (!uploaded.IsSuccess) {
          Status = Text(uploaded);

          return;
        }
      }

      Status = "Uploaded " + files.Count + " files.";
      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanOpen))]
  private Task DownloadAsync() =>
    RunAsync(async token => {
      var entry = SelectedEntry;
      var target = OpenLocation();

      if (entry is null || target is null)
        return;

      if (entry.IsDirectory) {
        var folder = PickFolder is null ? null : await PickFolder("Folder to save into");

        if (string.IsNullOrWhiteSpace(folder)) {
          Status = "Download cancelled.";

          return;
        }

        var downloaded = await S3Storage.DownloadPrefixAsync(target, entry.Key, folder, Report, token);
        Status = downloaded.IsSuccess ? "Downloaded " + downloaded.Value + " files." : Text(downloaded);

        return;
      }

      var path = PickSavePath is null ? null : await PickSavePath(entry.Name);

      if (string.IsNullOrWhiteSpace(path)) {
        Status = "Download cancelled.";

        return;
      }

      var saved = await S3Storage.DownloadAsync(target, entry.Key, path, Report, token);
      Status = saved.IsSuccess ? "Downloaded " + entry.Name + "." : Text(saved);
    });

  [RelayCommand(CanExecute = nameof(CanOpen))]
  private Task DeleteAsync() =>
    RunAsync(async token => {
      var entry = SelectedEntry;
      var target = OpenLocation();

      if (entry is null || target is null)
        return;

      var message = entry.IsDirectory
        ? "Delete " + entry.Name + " and everything inside it? This removes objects from the bucket. Longhorn backup records stay as they are."
        : "Delete " + entry.Name + " from the bucket?";

      if (!await AskConfirmAsync("Delete", message))
        return;

      if (entry.IsDirectory) {
        var deleted = await S3Storage.DeletePrefixAsync(target, entry.Key, Report, token);
        Status = deleted.IsSuccess ? "Deleted " + deleted.Value + " objects." : Text(deleted);
      }
      else {
        var deleted = await S3Storage.DeleteObjectAsync(target, entry.Key, token);
        Status = deleted.IsSuccess ? "Deleted " + entry.Name + "." : Text(deleted);
      }

      await ListFolderAsync(token);
    });

  [RelayCommand(CanExecute = nameof(CanOpen))]
  private Task RenameAsync() =>
    RunAsync(token => MoveAsync(token, copy: false));

  [RelayCommand(CanExecute = nameof(CanOpen))]
  private Task CopyAsync() =>
    RunAsync(token => MoveAsync(token, copy: true));

  [RelayCommand(CanExecute = nameof(CanAdd))]
  private Task AddTargetAsync() =>
    RunAsync(token => EditTargetAsync(token, null));

  [RelayCommand(CanExecute = nameof(CanChange))]
  private Task EditTargetAsync() =>
    RunAsync(token => EditTargetAsync(token, SelectedTarget));

  [RelayCommand(CanExecute = nameof(CanChange))]
  private Task RemoveTargetAsync() =>
    RunAsync(async token => {
      var target = SelectedTarget;

      if (target is null)
        return;

      var accepted = await AskConfirmAsync(
        "Remove target",
        "Remove the Longhorn backup target " + target.Name + "? Backups that use it stop. The credential secret stays, and objects in the bucket stay.");

      if (!accepted)
        return;

      var removed = await LonghornS3.DeleteAsync(_session, target, token);
      Status = removed.IsSuccess ? "Removed " + target.Name + "." : Text(removed);

      if (removed.IsSuccess)
        await LoadTargetsAsync(token, null);
    });

  [RelayCommand(CanExecute = nameof(CanAdd))]
  private Task ReloadTargetsAsync() =>
    RunAsync(token => LoadTargetsAsync(token, SelectedTarget?.Name));

  private async Task OpenTargetAsync() {
    await RunAsync(BrowseTargetAsync);
  }

  private async Task OpenBucketAsync() {
    await RunAsync(async token => {
      SetPath("");
      await ListFolderAsync(token);
    });
  }

  private async Task LoadTargetsAsync(CancellationToken token, string? select) {
    var loaded = await LonghornS3.LoadAsync(_session, token);

    if (!loaded.IsSuccess || loaded.Value is null) {
      Status = Text(loaded);

      return;
    }

    var set = loaded.Value;
    var name = select ?? SelectedTarget?.Name;
    _suppress = true;
    Targets.Clear();

    foreach (var target in set.Targets)
      Targets.Add(target);

    SelectedTarget = Targets.FirstOrDefault(item => item.Name == name) ?? Targets.FirstOrDefault();
    _suppress = false;
    OnPropertyChanged(nameof(TargetSummary));
    await BrowseTargetAsync(token);
    var notes = new List<string>();

    if (set.OtherTargets > 0)
      notes.Add(set.OtherTargets + " backup targets are not S3.");

    if (set.Warning.Length > 0)
      notes.Add(set.Warning);

    if (notes.Count > 0)
      Status = string.Join(" ", notes) + " " + Status;
  }

  private async Task ListFolderAsync(CancellationToken token) {
    var target = OpenLocation();

    if (target is null) {
      _all.Clear();
      ApplyFilter();
      Status = "Add an S3 target to browse it.";

      return;
    }

    var listed = await S3Storage.ListAsync(target, PathText, token);

    if (!listed.IsSuccess || listed.Value is null) {
      Status = Text(listed);

      return;
    }

    SelectedEntry = null;
    _all.Clear();
    _all.AddRange(listed.Value);
    ApplyFilter();
    var where = S3Layout.NormalizePrefix(PathText).Length == 0 ? target.Bucket : target.Bucket + "/" + S3Layout.Display(PathText);
    var note = _bucketNote.Length == 0 ? "" : " " + _bucketNote;
    Status = VisibleEntries.Count + " in " + where + note;
  }

  private async Task BrowseTargetAsync(CancellationToken token) {
    var target = SelectedTarget;
    await LoadBucketsAsync(token, target?.Bucket);

    if (target is null) {
      await ListFolderAsync(token);

      return;
    }

    SetPath(SelectedBucket?.Name == target.Bucket ? target.Prefix : "");
    await ListFolderAsync(token);
  }

  private async Task LoadBucketsAsync(CancellationToken token, string? prefer) {
    var target = SelectedTarget;

    if (target is null) {
      _bucketNote = "";
      _suppress = true;
      SelectedBucket = null;
      Buckets.Clear();
      _suppress = false;
      OnPropertyChanged(nameof(TargetSummary));

      return;
    }

    var listed = await S3Storage.ListBucketsAsync(target, token);
    IReadOnlyList<S3Bucket> buckets;

    if (!listed.IsSuccess || listed.Value is null) {
      _bucketNote = "Only the backup bucket is shown. " + Text(listed);
      buckets = S3Layout.MarkBackups(S3Layout.Buckets([], target.Bucket), BackupNames());
    }
    else {
      _bucketNote = "";
      buckets = S3Layout.MarkBackups(S3Layout.Buckets(listed.Value, target.Bucket), BackupNames());
    }

    var pick = string.IsNullOrEmpty(prefer) ? target.Bucket : prefer;
    _suppress = true;
    SelectedBucket = null;
    Buckets.Clear();

    foreach (var bucket in buckets)
      Buckets.Add(bucket);

    SelectedBucket = Buckets.FirstOrDefault(item => item.Name == pick)
      ?? Buckets.FirstOrDefault(item => item.Name == target.Bucket)
      ?? Buckets.FirstOrDefault();
    _suppress = false;
    OnPropertyChanged(nameof(TargetSummary));
  }

  private IEnumerable<string> BackupNames() =>
    Targets.Select(item => item.Bucket);

  private S3Target? OpenLocation() {
    var target = SelectedTarget;
    var name = SelectedBucket?.Name;

    if (target is null || string.IsNullOrEmpty(name))
      return target;

    return target with { Bucket = name };
  }

  private async Task OpenAsync(S3Entry? entry, CancellationToken token) {
    var target = OpenLocation();

    if (entry is null || target is null)
      return;

    if (entry.IsDirectory) {
      SetPath(entry.Key);
      await ListFolderAsync(token);

      return;
    }

    if (entry.Size > S3Layout.PreviewLimit) {
      Status = "This file is too large to preview. Download it.";

      return;
    }

    var read = await S3Storage.ReadTextAsync(target, entry.Key, token);

    if (!read.IsSuccess || read.Value is null) {
      Status = Text(read);

      return;
    }

    if (read.Value.Binary) {
      Status = "This file is not text. Download it to open it.";

      return;
    }

    if (Preview is null) {
      Status = "Preview is not available.";

      return;
    }

    var key = entry.Key;
    var file = new S3FileViewModel(key, read.Value.Text, text => S3Storage.WriteTextAsync(target, key, text, token));
    await Preview(file);

    if (file.Saved)
      await ListFolderAsync(token);
  }

  private async Task MoveAsync(CancellationToken token, bool copy) {
    var entry = SelectedEntry;
    var target = OpenLocation();

    if (entry is null || target is null)
      return;

    var suggested = copy
      ? S3Layout.Sibling(entry.Key, S3Layout.CopyName(entry.Name, entry.IsDirectory), entry.IsDirectory)
      : entry.Key;
    var answer = await AskNameAsync(
      copy ? "Copy" : "Rename",
      "Path in the bucket. The current folder is the start when you leave the path relative.",
      S3Layout.Display(suggested));

    if (answer is null)
      return;

    if (!S3Layout.IsPrefix(answer)) {
      Status = "That path is not valid.";

      return;
    }

    var destination = entry.IsDirectory ? S3Layout.NormalizePrefix(answer) : answer.Trim().Trim('/');
    var source = entry.IsDirectory ? S3Layout.NormalizePrefix(entry.Key) : entry.Key;

    if (source == destination) {
      Status = "Choose a different path.";

      return;
    }

    if (entry.IsDirectory && S3Layout.ContainsPrefix(source, destination)) {
      Status = "The new folder is inside the folder being copied.";

      return;
    }

    Result moved;

    if (entry.IsDirectory)
      moved = await S3Storage.CopyPrefixAsync(target, source, destination, Report, token);
    else
      moved = await S3Storage.CopyObjectAsync(target, source, destination, entry.Size, Report, token);

    if (!moved.IsSuccess) {
      Status = Text(moved);

      return;
    }

    if (!copy) {
      Result deleted = entry.IsDirectory
        ? await S3Storage.DeletePrefixAsync(target, source, Report, token)
        : await S3Storage.DeleteObjectAsync(target, source, token);

      if (!deleted.IsSuccess) {
        Status = "Copied, and the original is still there. " + Text(deleted);

        return;
      }
    }

    Status = copy ? "Copied to " + S3Layout.Display(destination) + "." : "Renamed to " + S3Layout.Display(destination) + ".";
    await ListFolderAsync(token);
  }

  private async Task EditTargetAsync(CancellationToken token, S3Target? existing) {
    var names = Targets.Select(item => item.Name).ToList();
    var ns = existing?.Namespace ?? SelectedTarget?.Namespace ?? Targets.FirstOrDefault()?.Namespace ?? _namespace;
    var dialog = new S3TargetViewModel(ns, names, existing);

    if (EditTarget is null || !await EditTarget(dialog)) {
      Status = "Target was not saved.";

      return;
    }

    var target = dialog.ToTarget();
    var saved = await LonghornS3.SaveAsync(_session, target, token);
    Status = saved.IsSuccess ? "Saved " + target.Name + "." : Text(saved);

    if (saved.IsSuccess)
      await LoadTargetsAsync(token, target.Name);
  }

  private async Task<bool> AskConfirmAsync(string title, string message) {
    if (Confirm is null)
      return false;

    return await Confirm(title, message);
  }

  private async Task<string?> AskNameAsync(string title, string message, string initial) {
    if (Ask is null)
      return null;

    return await Ask(title, message, initial);
  }

  private void SetPath(string prefix) {
    Filter = "";
    PathText = S3Layout.Display(prefix);
  }

  private void ApplyFilter() {
    var filterText = Filter.Trim();
    VisibleEntries.Clear();

    foreach (var entry in _all) {
      if (filterText.Length == 0 || entry.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
        VisibleEntries.Add(entry);
    }

    if (SelectedEntry is not null && !VisibleEntries.Contains(SelectedEntry))
      SelectedEntry = null;
  }

  private async Task RunAsync(Func<CancellationToken, Task> work) {
    if (IsBusy)
      return;

    IsBusy = true;
    using var run = new CancellationTokenSource();
    _run = run;

    try {
      await work(run.Token);
    }
    catch (OperationCanceledException) {
      Status = "Cancelled.";
    }
    finally {
      if (ReferenceEquals(_run, run))
        _run = null;

      IsBusy = false;
    }
  }

  private void Report(string text) {
    if (Dispatcher.UIThread.CheckAccess())
      Status = text;
    else
      Dispatcher.UIThread.Post(() => Status = text);
  }

  private bool CanRefresh() =>
    SelectedTarget is not null && !IsBusy;

  private bool CanGoUp() =>
    !IsBusy && S3Layout.NormalizePrefix(PathText).Length > 0;

  private bool CanOpen() =>
    !IsBusy && SelectedEntry is not null && SelectedTarget is not null;

  private bool CanChange() =>
    !IsBusy && SelectedTarget is not null;

  private bool CanCreateBucket() =>
    !IsBusy && SelectedTarget is not null;

  private bool CanDeleteBucket() =>
    !IsBusy && SelectedBucket is { Backup: false };

  private bool CanAdd() =>
    !IsBusy;

  private void Notify() {
    RefreshCommand.NotifyCanExecuteChanged();
    BucketRootCommand.NotifyCanExecuteChanged();
    GoUpCommand.NotifyCanExecuteChanged();
    OpenEntryCommand.NotifyCanExecuteChanged();
    OpenPathCommand.NotifyCanExecuteChanged();
    NewBucketCommand.NotifyCanExecuteChanged();
    DeleteBucketCommand.NotifyCanExecuteChanged();
    NewFolderCommand.NotifyCanExecuteChanged();
    UploadCommand.NotifyCanExecuteChanged();
    UploadFolderCommand.NotifyCanExecuteChanged();
    DownloadCommand.NotifyCanExecuteChanged();
    DeleteCommand.NotifyCanExecuteChanged();
    RenameCommand.NotifyCanExecuteChanged();
    CopyCommand.NotifyCanExecuteChanged();
    AddTargetCommand.NotifyCanExecuteChanged();
    EditTargetCommand.NotifyCanExecuteChanged();
    RemoveTargetCommand.NotifyCanExecuteChanged();
    ReloadTargetsCommand.NotifyCanExecuteChanged();
  }

  private static string Text(Result result) =>
    result.Messages.Count == 0 ? "The request failed." : string.Join("; ", result.Messages);
}
