using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.Converters;

public sealed class StatusBrushConverter : IValueConverter {
  public static StatusBrushConverter Instance { get; } = new();

  private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#e6e6e6"));
  private static readonly IBrush Healthy = new SolidColorBrush(Color.Parse("#3ddc84"));
  private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#ff9f0a"));
  private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#ff4d6d"));
  private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#6ea8fe"));

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
    return ResourceStatusPaint.Tone(value as string) switch {
      ResourceStatusTone.Healthy => Healthy,
      ResourceStatusTone.Warning => Warning,
      ResourceStatusTone.Error => Error,
      ResourceStatusTone.Info => Info,
      _ => Neutral
    };
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    BindingOperations.DoNothing;
}
