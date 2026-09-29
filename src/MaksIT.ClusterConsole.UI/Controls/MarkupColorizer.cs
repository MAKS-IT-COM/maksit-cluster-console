using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using MediaFontStyle = Avalonia.Media.FontStyle;
using ThemeFontStyle = TextMateSharp.Themes.FontStyle;


namespace MaksIT.ClusterConsole.UI.Controls;

// TextMate's editor model tokenizes on a background thread, stops after 10_000
// characters on a line, and only repaints lines it still considers visible.
// Kubernetes YAML often has a longer string than that, and a missed repaint
// leaves the rest of the document in the default color. This colorizer
// tokenizes each full line before drawing it and keeps grammar state from the
// lines above.
sealed class MarkupColorizer : DocumentColorizingTransformer {
  static readonly TimeSpan TokenLimit = TimeSpan.FromSeconds(2);
  static readonly RegistryOptions Options = new(ThemeName.DarkPlus);
  static readonly Registry GrammarRegistry = new(Options);
  static readonly Theme Theme;
  static readonly Dictionary<int, IBrush> Brushes;
  static readonly Dictionary<string, IGrammar?> Grammars = new(StringComparer.Ordinal);

  readonly List<LineSpan[]> _lines = [];
  readonly List<IStateStack?> _states = [];

  TextDocument? _document;
  IGrammar? _grammar;
  string? _scope;

  static MarkupColorizer() {
    GrammarRegistry.SetTheme(Options.GetDefaultTheme());
    Theme = GrammarRegistry.GetTheme();
    Brushes = [];
    foreach (var color in Theme.GetColorMap()) {
      if (string.IsNullOrEmpty(color))
        continue;

      try {
        Brushes[Theme.GetColorId(color)] = new ImmutableSolidColorBrush(Color.Parse(NormalizeColor(color)));
      }
      catch (FormatException) {
      }
    }
  }

  public void Watch(TextDocument document) {
    if (ReferenceEquals(_document, document))
      return;

    if (_document is not null)
      _document.Changed -= OnDocumentChanged;

    _document = document;
    Clear();
    document.Changed += OnDocumentChanged;
  }

  public void SetScope(string? scope) {
    if (string.Equals(_scope, scope, StringComparison.Ordinal))
      return;

    _scope = scope;
    _grammar = scope is null ? null : GrammarFor(scope);
    Clear();
  }

  protected override void ColorizeLine(DocumentLine line) {
    if (_grammar is null || !ReferenceEquals(CurrentContext.Document, _document))
      return;

    try {
      EnsureThrough(line.LineNumber);
    }
    catch (ArgumentOutOfRangeException) {
      Clear();
      return;
    }

    var index = line.LineNumber - 1;
    if (index < 0 || index >= _lines.Count)
      return;

    foreach (var span in _lines[index]) {
      var start = line.Offset + span.Start;
      var end = line.Offset + span.End;
      if (start >= end || start < line.Offset || end > line.EndOffset)
        continue;

      ChangeLinePart(start, end, element => Apply(element, span));
    }
  }

  void EnsureThrough(int lineNumber) {
    var document = _document;
    var grammar = _grammar;
    if (document is null || grammar is null)
      return;

    while (_lines.Count < lineNumber) {
      var line = document.GetLineByNumber(_lines.Count + 1);
      var text = document.GetText(line);
      var previous = _states.Count == 0 ? null : _states[^1];
      ITokenizeLineResult result;
      try {
        result = grammar.TokenizeLine(text, previous, TokenLimit);
      }
      catch (Exception) {
        _states.Add(previous);
        _lines.Add([]);
        continue;
      }

      _states.Add(result.RuleStack);
      _lines.Add(Spans(text, result));
    }
  }

  void OnDocumentChanged(object? sender, DocumentChangeEventArgs e) {
    var document = _document;
    if (document is null)
      return;

    var offset = Math.Clamp(e.Offset, 0, document.TextLength);
    var line = document.GetLineByOffset(offset).LineNumber;
    Truncate(Math.Max(0, line - 2));
  }

  void Truncate(int lineIndex) {
    if (lineIndex >= _lines.Count)
      return;

    _lines.RemoveRange(lineIndex, _lines.Count - lineIndex);
    _states.RemoveRange(lineIndex, _states.Count - lineIndex);
  }

  void Clear() {
    _lines.Clear();
    _states.Clear();
  }

  static IGrammar? GrammarFor(string scope) {
    if (Grammars.TryGetValue(scope, out var cached))
      return cached;

    try {
      cached = GrammarRegistry.LoadGrammar(scope);
    }
    catch (Exception) {
      cached = null;
    }

    Grammars[scope] = cached;
    return cached;
  }

  static LineSpan[] Spans(string text, ITokenizeLineResult result) {
    var tokens = result.Tokens;
    if (tokens is null || tokens.Length == 0 || text.Length == 0)
      return [];

    var spans = new List<LineSpan>(tokens.Length);
    for (var i = 0; i < tokens.Length; i++) {
      var start = tokens[i].StartIndex;
      var end = i + 1 < tokens.Length ? tokens[i + 1].StartIndex : text.Length;
      if (start >= text.Length)
        break;
      if (end > text.Length)
        end = text.Length;
      if (start >= end)
        continue;

      var style = StyleFor(tokens[i].Scopes);
      if (style is null)
        continue;

      if (spans.Count > 0 && spans[^1].End == start && spans[^1].Style.Equals(style.Value)) {
        var previous = spans[^1];
        spans[^1] = previous with { End = end };
        continue;
      }

      spans.Add(new LineSpan(start, end, style.Value));
    }

    return spans.Count == 0 ? [] : spans.ToArray();
  }

  static LineStyle? StyleFor(IEnumerable<string>? scopes) {
    if (scopes is null)
      return null;

    List<string>? list = null;
    foreach (var scope in scopes) {
      list ??= [];
      list.Add(scope);
    }

    if (list is null || list.Count == 0)
      return null;

    var foreground = 0;
    var background = 0;
    var fontStyle = ThemeFontStyle.NotSet;
    foreach (var rule in Theme.Match(list)) {
      if (foreground == 0 && rule.foreground > 0)
        foreground = rule.foreground;
      if (background == 0 && rule.background > 0)
        background = rule.background;
      if (fontStyle == ThemeFontStyle.NotSet && rule.fontStyle > 0)
        fontStyle = rule.fontStyle;
    }

    if (foreground == 0 && background == 0 && fontStyle == ThemeFontStyle.NotSet)
      return null;

    Brushes.TryGetValue(foreground, out var foregroundBrush);
    Brushes.TryGetValue(background, out var backgroundBrush);
    if (foregroundBrush is null && backgroundBrush is null && fontStyle == ThemeFontStyle.NotSet)
      return null;

    return new LineStyle(foregroundBrush, backgroundBrush, fontStyle);
  }

  static void Apply(VisualLineElement element, LineSpan span) {
    if (span.Style.Foreground is not null)
      element.TextRunProperties.SetForegroundBrush(span.Style.Foreground);
    if (span.Style.Background is not null)
      element.TextRunProperties.SetBackgroundBrush(span.Style.Background);

    var italic = span.Style.Font != ThemeFontStyle.NotSet && (span.Style.Font & ThemeFontStyle.Italic) != 0;
    var bold = span.Style.Font != ThemeFontStyle.NotSet && (span.Style.Font & ThemeFontStyle.Bold) != 0;
    var underline = span.Style.Font != ThemeFontStyle.NotSet && (span.Style.Font & ThemeFontStyle.Underline) != 0;
    if (underline)
      element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);

    if (!italic && !bold)
      return;

    var typeface = element.TextRunProperties.Typeface;
    element.TextRunProperties.SetTypeface(new Typeface(
      typeface.FontFamily,
      italic ? MediaFontStyle.Italic : typeface.Style,
      bold ? FontWeight.Bold : typeface.Weight));
  }

  static string NormalizeColor(string color) {
    if (color.Length != 9)
      return color;

    return string.Create(9, color, static (span, value) => {
      span[0] = '#';
      span[1] = value[7];
      span[2] = value[8];
      span[3] = value[1];
      span[4] = value[2];
      span[5] = value[3];
      span[6] = value[4];
      span[7] = value[5];
      span[8] = value[6];
    });
  }

  readonly record struct LineSpan(int Start, int End, LineStyle Style);

  readonly record struct LineStyle(IBrush? Foreground, IBrush? Background, ThemeFontStyle Font);
}
