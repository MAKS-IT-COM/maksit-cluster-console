using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public sealed class PodLogTarget {
  public bool Enabled { get; init; }

  public bool NeedsPodPicker { get; init; }

  public string? PodName { get; init; }

  public string? Namespace { get; init; }

  public string? ContainerName { get; init; }

  public bool NeedsContainer { get; init; }
}

public partial class PodLogsViewModel : ObservableObject {
  private readonly ClusterWorkspace _workspace;
  private readonly Func<PodLogTarget> _target;
  private CancellationTokenSource? _logsCts;
  private int _generation;

  public PodLogsViewModel(ClusterWorkspace workspace, Func<PodLogTarget> target) {
    _workspace = workspace;
    _target = target;
  }

  [ObservableProperty]
  private string logsText = string.Empty;

  [ObservableProperty]
  private bool followLogs;

  public void Cancel() {
    _logsCts?.Cancel();
    _logsCts = null;
  }

  public void Clear() {
    Cancel();
    _generation++;
    LogsText = "";
  }

  partial void OnFollowLogsChanged(bool value) =>
    _ = LoadAsync();

  public async Task LoadAsync() {
    Cancel();
    var generation = ++_generation;
    var target = _target();

    if (!target.Enabled) {
      LogsText = "";

      return;
    }

    if (target.PodName is null || target.Namespace is null || _workspace.Session is null) {
      LogsText = target.NeedsPodPicker ? "Select a pod to read logs." : "";

      return;
    }

    if (target.NeedsContainer) {
      LogsText = "Select a container to read logs.";

      return;
    }

    var pod = target.PodName;
    var ns = target.Namespace;
    var container = target.ContainerName;

    if (FollowLogs) {
      _logsCts = new CancellationTokenSource();
      LogsText = "";
      _ = FollowAsync(pod, ns, container, generation, _logsCts.Token);

      return;
    }

    var logs = await _workspace.Session.GetLogsAsync(pod, ns, container, false, 200);

    if (generation != _generation)
      return;

    LogsText = logs.IsSuccess ? logs.Value ?? "" : string.Join("; ", logs.Messages);
  }

  private async Task FollowAsync(
    string pod,
    string ns,
    string? container,
    int generation,
    CancellationToken cancellationToken) {
    if (_workspace.Session is null)
      return;

    try {
      await foreach (var line in _workspace.Session.FollowLogsAsync(pod, ns, container, cancellationToken)) {
        var text = line;
        Dispatcher.UIThread.Post(() => {
          if (generation != _generation || cancellationToken.IsCancellationRequested)
            return;

          Append(text);
        });
      }
    }
    catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException) {
    }
    catch (Exception ex) {
      if (generation != _generation || cancellationToken.IsCancellationRequested)
        return;

      Dispatcher.UIThread.Post(() => {
        if (generation != _generation || cancellationToken.IsCancellationRequested)
          return;

        LogsText = string.IsNullOrEmpty(LogsText) ? ex.Message : LogsText + Environment.NewLine + ex.Message;
      });
    }
  }

  private void Append(string line) {
    LogsText += line + Environment.NewLine;

    if (LogsText.Length > 200_000)
      LogsText = LogsText[^100_000..];
  }
}
