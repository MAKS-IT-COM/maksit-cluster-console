using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using MaksIT.ClusterConsole.Shared;
using TextMateSharp.Grammars;


namespace MaksIT.ClusterConsole.UI.Controls;

public class SyntaxTextEditor : TextEditor {
  public static readonly StyledProperty<string> TextProperty =
    AvaloniaProperty.Register<SyntaxTextEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

  public static readonly StyledProperty<string?> FileNameProperty =
    AvaloniaProperty.Register<SyntaxTextEditor, string?>(nameof(FileName));

  public static readonly StyledProperty<bool> PreferYamlProperty =
    AvaloniaProperty.Register<SyntaxTextEditor, bool>(nameof(PreferYaml));

  static readonly RegistryOptions Registry = new(ThemeName.DarkPlus);

  protected override Type StyleKeyOverride => typeof(TextEditor);

  readonly MarkupColorizer _colorizer = new();
  string? _scope;
  bool _updating;

  public SyntaxTextEditor() {
    FontFamily = new FontFamily("Cascadia Mono, Consolas, Ubuntu Mono, monospace");
    ShowLineNumbers = true;
    Options.EnableHyperlinks = false;
    TextArea.TextView.LineTransformers.Add(_colorizer);
    _colorizer.Watch(Document);
    Document.Changed += OnDocumentChanged;
  }

  public new string Text {
    get => GetValue(TextProperty);
    set => SetValue(TextProperty, value);
  }

  public string? FileName {
    get => GetValue(FileNameProperty);
    set => SetValue(FileNameProperty, value);
  }

  public bool PreferYaml {
    get => GetValue(PreferYamlProperty);
    set => SetValue(PreferYamlProperty, value);
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
    base.OnPropertyChanged(change);
    if (change.Property == TextProperty)
      SetDocumentText(change.GetNewValue<string>() ?? "");
    else if (change.Property == FileNameProperty || change.Property == PreferYamlProperty)
      ApplySyntax();
    else if (change.Property == DocumentProperty) {
      if (change.OldValue is TextDocument oldDocument)
        oldDocument.Changed -= OnDocumentChanged;
      if (change.NewValue is TextDocument newDocument) {
        newDocument.Changed += OnDocumentChanged;
        _colorizer.Watch(newDocument);
      }

      ApplySyntax();
    }
  }

  void SetDocumentText(string value) {
    if (Document.Text == value)
      return;

    _updating = true;
    Document.Text = value;
    _updating = false;
  }

  void OnDocumentChanged(object? sender, DocumentChangeEventArgs e) {
    if (_updating)
      return;

    var text = Document.Text;
    if (!string.Equals(Text, text, StringComparison.Ordinal))
      Text = text;
  }

  void ApplySyntax() {
    var syntax = MarkupSyntaxDetector.Detect(FileName, PreferYaml);
    var scope = ScopeFor(syntax);
    if (string.Equals(_scope, scope, StringComparison.Ordinal))
      return;

    _scope = scope;
    _colorizer.SetScope(scope);
    TextArea.TextView.Redraw();
  }

  static string? ScopeFor(MarkupSyntax syntax) {
    var extension = syntax switch {
      MarkupSyntax.Json => ".json",
      MarkupSyntax.Yaml => ".yaml",
      _ => null
    };
    if (extension is null)
      return null;

    var language = Registry.GetLanguageByExtension(extension);
    return language is null ? null : Registry.GetScopeByLanguageId(language.Id);
  }
}
