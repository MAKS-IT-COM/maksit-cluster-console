using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.Converters;

public sealed class IssueStateBrushConverter : IValueConverter {
  public static IssueStateBrushConverter Instance { get; } = new();

  private static readonly IBrush Resolved = new SolidColorBrush(Color.Parse("#9aa0a6"));
  private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#ff9f0a"));
  private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#ff4d6d"));

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
    if (value is string state && state.Equals(ClusterIssues.Resolved, StringComparison.OrdinalIgnoreCase))
      return Resolved;

    if (parameter is string kind && kind.Equals("Error", StringComparison.OrdinalIgnoreCase))
      return Error;

    return Warning;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    BindingOperations.DoNothing;
}
