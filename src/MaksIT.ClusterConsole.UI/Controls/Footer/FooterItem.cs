using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;


namespace MaksIT.ClusterConsole.UI.Controls.Footer;

public enum FooterEdge {
  Leading,
  Trailing
}

public abstract class FooterItem : ObservableObject {
  public FooterEdge Edge { get; init; } = FooterEdge.Leading;

  public Thickness Margin { get; init; }
}

public sealed partial class FooterButton : FooterItem {
  public FooterButton(string label, ICommand command) {
    Text = label;
    Command = command;
  }

  public ICommand Command { get; }

  public object? CommandParameter { get; init; }

  public string? ToolTip { get; init; }

  public bool IsDefault { get; init; }

  public bool IsCancel { get; init; }

  public Thickness Padding { get; init; } = new(10, 4);

  [ObservableProperty]
  private string text;
}

public sealed partial class FooterLabel : FooterItem {
  public FooterLabel(string label) {
    Text = label;
  }

  [ObservableProperty]
  private string text;

  [ObservableProperty]
  private bool isVisible = true;
}

public sealed partial class FooterCheck : FooterItem {
  public FooterCheck(string text, bool isChecked = false) {
    Text = text;
    IsChecked = isChecked;
  }

  public string Text { get; }

  [ObservableProperty]
  private bool isChecked;
}

public sealed partial class FooterNumberField : ObservableObject {
  public FooterNumberField(string id, decimal initial) {
    Id = id;
    Value = initial;
  }

  public string Id { get; }

  public string? Caption { get; init; }

  public string? CaptionTip { get; init; }

  public string? ToolTip { get; init; }

  public decimal Minimum { get; init; }

  public decimal Maximum { get; init; } = 100;

  public double Width { get; init; } = 90;

  public bool HasCaption =>
    !string.IsNullOrEmpty(Caption);

  [ObservableProperty]
  private decimal value;
}

public sealed partial class FooterTextField : ObservableObject {
  public FooterTextField(string id, string initial) {
    Id = id;
    Text = initial;
  }

  public string Id { get; }

  public string? Placeholder { get; init; }

  public double Width { get; init; } = 90;

  [ObservableProperty]
  private string text;
}

public sealed class FooterNumberGroup : FooterItem {
  public FooterNumberGroup(IReadOnlyList<FooterNumberField> fields, FooterButton button) {
    Fields = fields;
    Button = button;
  }

  public IReadOnlyList<FooterNumberField> Fields { get; }

  public FooterButton Button { get; }
}

public sealed class FooterTextGroup : FooterItem {
  public FooterTextGroup(FooterTextField field, FooterButton button) {
    Field = field;
    Button = button;
  }

  public FooterTextField Field { get; }

  public FooterButton Button { get; }
}
