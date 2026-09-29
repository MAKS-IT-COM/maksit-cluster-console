using System.Reflection;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels;


public sealed partial class AboutViewModel {
  public string Brand => AppInfo.Brand;

  public string ProductName => AppInfo.ProductName;

  public string Summary => AppInfo.Summary;

  public string Version => DisplayVersion();

  public string Credits => AppInfo.Credits;

  public AppContact SecurityContact => AppInfo.Security;

  public AppContact SupportContact => AppInfo.Support;

  public string License => AppInfo.License;

  public string Copyright => AppInfo.Copyright;

  public string Site => AppInfo.Site;

  [RelayCommand]
  private void OpenContact(string? uri) {
    if (string.IsNullOrWhiteSpace(uri))
      return;
    OpenUrl(uri);
  }

  [RelayCommand]
  private void OpenSite() =>
    OpenUrl(AppInfo.SiteUri);

  private static string DisplayVersion() {
    var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
    var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (!string.IsNullOrWhiteSpace(informational)) {
      var plus = informational.IndexOf('+', StringComparison.Ordinal);
      return plus >= 0 ? informational[..plus] : informational;
    }

    var version = assembly.GetName().Version;
    if (version is null)
      return "";
    if (version.Build < 0)
      return $"{version.Major}.{version.Minor}";
    return $"{version.Major}.{version.Minor}.{version.Build}";
  }

  private static void OpenUrl(string url) {
    try {
      Process.Start(new ProcessStartInfo {
        FileName = url,
        UseShellExecute = true
      });
    }
    catch {
    }
  }
}
