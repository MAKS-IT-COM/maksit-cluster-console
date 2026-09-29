namespace MaksIT.ClusterConsole.Shared;


public readonly record struct AppContact(string Role, string Address) {
  public string Uri => "mailto:" + Address;
}


public static class AppInfo {
  public const string Brand = "MaksIT";
  public const string ProductName = "Cluster Console";
  public const string AboutTitle = "About " + ProductName;
  public const string Credits = "Maksym Sadovnychyy";
  public static readonly AppContact Security = new("Security", "security@maks-it.com");
  public static readonly AppContact Support = new("Support", "support@maks-it.com");
  public const string Site = "maks-it.com";
  public const string SiteUri = "https://maks-it.com";
  public const string License = "Apache License 2.0";
  public const string Summary = "Desktop console for an existing Kubernetes cluster.";

  public static string Copyright =>
    $"Copyright {DateTime.UtcNow.Year} Maksym Sadovnychyy (MAKS-IT)";
}
