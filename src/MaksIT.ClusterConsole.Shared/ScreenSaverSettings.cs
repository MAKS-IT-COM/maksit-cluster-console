namespace MaksIT.ClusterConsole.Shared;

public sealed class ScreenSaverSettings {
  public const int DefaultIdleMinutes = 5;
  public const int MinIdleMinutes = 1;
  public const int MaxIdleMinutes = 120;

  public bool Enabled { get; set; }

  public int IdleMinutes { get; set; } = DefaultIdleMinutes;

  public void Ensure() {
    if (IdleMinutes < MinIdleMinutes || IdleMinutes > MaxIdleMinutes)
      IdleMinutes = DefaultIdleMinutes;
  }
}
