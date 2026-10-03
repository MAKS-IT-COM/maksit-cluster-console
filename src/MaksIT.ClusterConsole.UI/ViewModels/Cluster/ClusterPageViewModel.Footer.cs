using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.UI.Controls.Footer;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class ClusterPageViewModel {
  private bool _syncingFooter;
  private string _footerShape = "";

  [ObservableProperty]
  private IReadOnlyList<FooterItem> footerItems = [];

  partial void OnScaleReplicasChanged(int value) =>
    PushNumber("scale", value);

  partial void OnResizeStorageChanged(string value) =>
    PushText("resize", value);

  partial void OnDebugImageChanged(string value) =>
    PushText("debug", value);

  partial void OnForwardLocalPortChanged(int value) =>
    PushNumber("forward-local", value);

  partial void OnForwardContainerPortChanged(int value) =>
    PushNumber("forward-remote", value);

  partial void OnRebindLocalPortChanged(int value) =>
    PushNumber("rebind", value);

  private void SyncFooter() {
    var next = BuildFooter();
    var shape = string.Join('|', next.Select(ShapeOf));

    if (shape == _footerShape)
      return;

    _footerShape = shape;
    FooterItems = next;
  }

  private List<FooterItem> BuildFooter() {
    var items = new List<FooterItem>();

    if (CanDelete)
      items.Add(Action("Delete", DeleteCommand));

    if (CanStopPortForward) {
      items.Add(Action("Stop", StopSelectedPortForwardCommand));
      items.Add(RebindGroup());
    }

    if (CanForceDelete)
      items.Add(Action("Force delete", ForceDeleteCommand));

    if (CanDeleteNamespace)
      items.Add(Action("Force delete namespace", DeleteNamespaceCommand));

    if (CanScale)
      items.Add(ScaleGroup());

    if (CanRestart)
      items.Add(Action("Restart", RestartCommand));

    if (CanCordon)
      items.Add(Action("Cordon", CordonCommand));

    if (CanUncordon)
      items.Add(Action("Uncordon", UncordonCommand));

    if (CanDrain)
      items.Add(Action("Drain", DrainCommand));

    if (CanTrigger)
      items.Add(Action("Trigger", TriggerCronCommand));

    if (CanResize)
      items.Add(ResizeGroup());

    if (CanRetain)
      items.Add(Action("Reclaim", RetainCommand, "Change reclaim policy"));

    if (CanRollout) {
      items.Add(Action("Pause", PauseRolloutCommand));
      items.Add(Action("Resume", ResumeRolloutCommand));
      items.Add(Action("History", RolloutHistoryCommand));
      items.Add(Action("Undo", UndoRolloutCommand));
    }

    if (CanApprove) {
      items.Add(Action("Approve", ApproveCertificateCommand));
      items.Add(Action("Deny", DenyCertificateCommand));
    }

    if (CanToken)
      items.Add(Action("Token", CreateTokenCommand));

    if (CanAttach)
      items.Add(Action("Attach", AttachCommand));

    if (CanDebug)
      items.Add(DebugGroup());

    if (CanPortForward)
      items.Add(PortForwardGroup());

    return items;
  }

  private FooterNumberGroup ScaleGroup() =>
    new(
      [
        Number("scale", ScaleReplicas, value => ScaleReplicas = value, minimum: 0, maximum: 100, width: 80)
      ],
      Action("Scale", ScaleCommand));

  private FooterNumberGroup RebindGroup() =>
    new(
      [
        Number(
          "rebind",
          RebindLocalPort,
          value => RebindLocalPort = value,
          caption: "Local",
          captionTip: "Host port on this machine",
          fieldTip: "Listen on this host port",
          minimum: 1,
          maximum: 65535,
          width: 90)
      ],
      Action("Rebind", RebindSelectedPortForwardCommand));

  private FooterTextGroup ResizeGroup() =>
    new(
      Text("resize", ResizeStorage, value => ResizeStorage = value, "1Gi", 90),
      Action("Resize", ResizeCommand));

  private FooterTextGroup DebugGroup() =>
    new(
      Text("debug", DebugImage, value => DebugImage = value, "image", 140),
      Action("Debug", DebugCommand));

  private FooterNumberGroup PortForwardGroup() =>
    new(
      [
        Number(
          "forward-local",
          ForwardLocalPort,
          value => ForwardLocalPort = value,
          caption: "Local",
          captionTip: "Port on this machine",
          fieldTip: "Local port on this machine",
          minimum: 1,
          maximum: 65535,
          width: 90),
        Number(
          "forward-remote",
          ForwardContainerPort,
          value => ForwardContainerPort = value,
          caption: "Remote",
          captionTip: "Service or container port in the cluster",
          fieldTip: "Remote port on the Service or Pod",
          minimum: 1,
          maximum: 65535,
          width: 90)
      ],
      Action("Port-forward", StartPortForwardCommand));

  private FooterNumberField Number(
    string id,
    int value,
    Action<int> set,
    string? caption = null,
    string? captionTip = null,
    string? fieldTip = null,
    decimal minimum = 0,
    decimal maximum = 100,
    double width = 90) {
    var field = new FooterNumberField(id, value) {
      Caption = caption,
      CaptionTip = captionTip,
      ToolTip = fieldTip,
      Minimum = minimum,
      Maximum = maximum,
      Width = width
    };
    field.PropertyChanged += (_, e) => {
      if (e.PropertyName != nameof(FooterNumberField.Value) || _syncingFooter)
        return;

      _syncingFooter = true;

      try {
        set((int)field.Value);
      }
      finally {
        _syncingFooter = false;
      }
    };

    return field;
  }

  private FooterTextField Text(string id, string value, Action<string> set, string placeholder, double width) {
    var field = new FooterTextField(id, value) {
      Placeholder = placeholder,
      Width = width
    };
    field.PropertyChanged += (_, e) => {
      if (e.PropertyName != nameof(FooterTextField.Text) || _syncingFooter)
        return;

      _syncingFooter = true;

      try {
        set(field.Text);
      }
      finally {
        _syncingFooter = false;
      }
    };

    return field;
  }

  private void PushNumber(string id, int value) {
    var field = FindNumber(id);

    if (field is null || field.Value == value)
      return;

    _syncingFooter = true;
    field.Value = value;
    _syncingFooter = false;
  }

  private void PushText(string id, string value) {
    var field = FindText(id);

    if (field is null || field.Text == value)
      return;

    _syncingFooter = true;
    field.Text = value;
    _syncingFooter = false;
  }

  private FooterNumberField? FindNumber(string id) {
    foreach (var item in FooterItems) {
      if (item is not FooterNumberGroup group)
        continue;

      foreach (var field in group.Fields) {
        if (field.Id == id)
          return field;
      }
    }

    return null;
  }

  private FooterTextField? FindText(string id) {
    foreach (var item in FooterItems) {
      if (item is FooterTextGroup group && group.Field.Id == id)
        return group.Field;
    }

    return null;
  }

  private static FooterButton Action(string text, ICommand command, string? toolTip = null) =>
    new(text, command) { ToolTip = toolTip };

  private static string ShapeOf(FooterItem item) =>
    item switch {
      FooterButton button => "button:" + button.Text,
      FooterNumberGroup group => "numbers:" + group.Button.Text,
      FooterTextGroup group => "text:" + group.Button.Text,
      FooterLabel label => "label:" + label.Text,
      FooterCheck check => "check:" + check.Text,
      _ => item.GetType().Name
    };
}
