using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class ConnectionItemViewModel : ObservableObject {
  public required KubeContextDetails Details { get; init; }

  public string Name => Details.Name;

  public string Title => (Details.IsCurrent ? "* " : "  ") + Details.Name;

  public string Subtitle => Details.Cluster + " · " + Details.Server;
}

public partial class ConnectionsViewModel : ObservableObject {
  private readonly IKubeConfigService _kubeConfig;

  public ConnectionsViewModel(IKubeConfigService kubeConfig) {
    _kubeConfig = kubeConfig;
    KubeConfigPath = kubeConfig.GetWritablePath();
    Reload();
  }

  public ObservableCollection<ConnectionItemViewModel> Items { get; } = [];

  public string KubeConfigPath { get; }

  [ObservableProperty]
  private ConnectionItemViewModel? selected;

  [ObservableProperty]
  private string status = "";

  [ObservableProperty]
  private bool cleanupUnused = true;

  public string DetailsText {
    get {
      if (Selected is null)
        return "Select a context.";

      var d = Selected.Details;
      var mark = d.IsCurrent ? "*" : " ";
      return
        $"{mark} {d.Name}\n"
        + $"  Namespace: {d.Namespace ?? "(none)"}\n"
        + $"  Cluster: {d.Cluster}\n"
        + $"    Server: {d.Server}\n"
        + $"    InsecureSkipTLSVerify: {d.SkipTlsVerify}\n"
        + $"    CA Summary: {d.CaSummary}\n"
        + $"  User: {d.User}\n"
        + $"    Auth Summary: {d.AuthSummary}";
    }
  }

  public bool HasSelection => Selected is not null;

  public event Action<string?>? CloseRequested;

  public event Func<Task>? AddRequested;

  partial void OnSelectedChanged(ConnectionItemViewModel? value) {
    OnPropertyChanged(nameof(DetailsText));
    OnPropertyChanged(nameof(HasSelection));
  }

  public void Reload() {
    var listed = _kubeConfig.ListContextDetails();
    if (!listed.IsSuccess) {
      Status = string.Join("; ", listed.Messages);
      return;
    }

    var selectedName = Selected?.Name;
    Items.Clear();
    foreach (var details in listed.Value ?? [])
      Items.Add(new ConnectionItemViewModel { Details = details });

    Selected = Items.FirstOrDefault(i => i.Name == selectedName)
      ?? Items.FirstOrDefault(i => i.Details.IsCurrent)
      ?? Items.FirstOrDefault();
    Status = Items.Count == 0
      ? "No contexts in kubeconfig. Add a connection."
      : Items.Count + " context(s). * is kubectl current-context.";
  }

  [RelayCommand]
  private void UseForKubectl() {
    if (Selected is null)
      return;

    var used = _kubeConfig.UseContext(Selected.Name);
    Status = string.Join("; ", used.Messages);
    if (used.IsSuccess)
      Reload();
  }

  [RelayCommand]
  private void Delete() {
    if (Selected is null)
      return;

    var deleted = _kubeConfig.DeleteContext(Selected.Name, CleanupUnused);
    Status = string.Join("; ", deleted.Messages);
    if (deleted.IsSuccess) {
      Selected = null;
      Reload();
    }
  }

  [RelayCommand]
  private void Connect() =>
    CloseRequested?.Invoke(Selected?.Name);

  [RelayCommand]
  private void Close() =>
    CloseRequested?.Invoke(null);

  public bool TryAdd(KubeConnectionRequest request) {
    var added = _kubeConfig.UpsertConnection(request);
    Status = string.Join("; ", added.Messages);
    if (!added.IsSuccess)
      return false;

    Reload();
    Selected = Items.FirstOrDefault(i => i.Name == request.ContextName) ?? Selected;
    return true;
  }

  [RelayCommand]
  private async Task Add() {
    if (AddRequested is not null)
      await AddRequested();
  }
}
