namespace MaksIT.ClusterConsole.Shared;


public static class AppLog {
  private static readonly Lock Gate = new();

  public static string Directory() =>
    UserSettingsPath.LogsDirectory(ConfigurationFileService.ProductFolder);

  public static string FilePath() =>
    Path.Combine(Directory(), "app.log");

  public static void Write(string message) {
    try {
      System.IO.Directory.CreateDirectory(Directory());
      var line = DateTimeOffset.UtcNow.ToString("u") + " " + (message ?? "").TrimEnd();
      lock (Gate)
        File.AppendAllText(FilePath(), line + Environment.NewLine);
    }
    catch {
    }
  }

  public static void Write(Exception exception) {
    if (exception is null)
      return;

    Write(ErrorReport.Format(exception));
  }
}
