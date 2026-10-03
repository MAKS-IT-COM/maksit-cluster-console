using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public sealed partial class DrainConfirmViewModel : ObservableObject {
  private CancellationTokenSource? _adviceCts;

  public DrainConfirmViewModel(DrainPreview preview) {
    Rows = preview.Nodes
      .SelectMany(node => node.Pods.Select(pod => new DrainConfirmRow(node.Node, pod)))
      .ToList();

    var moving = Rows.Count(row => row.WillMove);
    MoveSummary = Count(moving, "move");
    RemainSummary = Count(Rows.Count - moving, "remain");
  }

  public IReadOnlyList<DrainConfirmRow> Rows { get; }

  public string MoveSummary { get; }

  public string RemainSummary { get; }

  public string Notice { get; } =
    "Nothing changes until you press Drain. Cancel leaves the node as it is. Drain cordons the node, then moves only the pods marked Will move. Pods marked Will remain stay, including DaemonSets and any pod a PodDisruptionBudget would refuse. Those pods are not deleted.";

  [ObservableProperty]
  private bool _showAdvice;

  [ObservableProperty]
  private string _advice = "";

  public async Task LoadAdviceAsync(
    Func<CancellationToken, Task<Result<string>>> assess,
    CancellationToken cancellationToken = default) {
    _adviceCts?.Cancel();
    _adviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var token = _adviceCts.Token;
    ShowAdvice = true;
    Advice = "Writing a note about this drain…";
    string text;

    try {
      var result = await assess(token).ConfigureAwait(false);

      if (token.IsCancellationRequested)
        return;

      text = result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value)
        ? result.Value
        : string.Join("; ", result.Messages);
    }
    catch (OperationCanceledException) {
      return;
    }
    catch (Exception ex) {
      text = ex.Message;
    }

    if (string.IsNullOrWhiteSpace(text))
      text = "The assistant did not write a note.";

    await Dispatcher.UIThread.InvokeAsync(() => {
      if (!token.IsCancellationRequested)
        Advice = text;
    });
  }

  public void CancelAdvice() =>
    _adviceCts?.Cancel();

  private static string Count(int count, string verb) =>
    count == 1 ? $"1 pod will {verb}." : $"{count} pods will {verb}.";
}

public sealed class DrainConfirmRow {
  public DrainConfirmRow(string node, DrainPodAction pod) {
    Node = node;
    Namespace = pod.Namespace;
    Name = pod.Name;
    WillMove = pod.Action == DrainPlan.Evict;
    Outcome = WillMove ? "Will move" : "Will remain";
    OutcomeBrush = WillMove ? MoveBrush : RemainBrush;
    Reason = pod.Reason;
  }

  private static readonly IBrush MoveBrush = new SolidColorBrush(Color.Parse("#00a7a0"));

  private static readonly IBrush RemainBrush = new SolidColorBrush(Color.Parse("#ff9f0a"));

  public string Node { get; }

  public string Namespace { get; }

  public string Name { get; }

  public string Outcome { get; }

  public IBrush OutcomeBrush { get; }

  public string Reason { get; }

  public bool WillMove { get; }
}
