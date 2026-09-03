namespace MaksIT.ClusterConsole.Shared;


/// <summary>
/// User-writable operator settings under AppData. The product folder must match
/// the WiX <c>installFolderName</c> (or <c>Get-DesktopInstallFolderName</c> from
/// <c>appName</c> + <c>manufacturer</c>), e.g. <c>%AppData%/MaksIT/Cluster Console</c>.
/// Host logging stays in shipped <c>appsettings.json</c> next to the exe.
/// </summary>
public static class UserSettingsPath {
  public static string Get(string product, string fileName = "settings.json") =>
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "MaksIT",
      product,
      fileName);
}
