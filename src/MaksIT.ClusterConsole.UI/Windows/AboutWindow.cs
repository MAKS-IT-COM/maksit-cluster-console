using Avalonia.Controls;
using MaksIT.ClusterConsole.Shared;
using CoreAbout = MaksIT.Core.UI.About.AboutWindow;
using CoreContact = MaksIT.Core.UI.About.AppContact;
using CoreInfo = MaksIT.Core.UI.About.DesktopAppInfo;


namespace MaksIT.ClusterConsole.UI.Windows;

public static class AboutWindow {
  public static Task ShowAsync(Window owner) =>
    CoreAbout.ShowAsync(owner, new CoreInfo {
      Brand = AppInfo.Brand,
      ProductName = AppInfo.ProductName,
      Summary = AppInfo.Summary,
      Version = AppInfo.Version,
      Credits = AppInfo.Credits,
      License = AppInfo.License,
      Copyright = AppInfo.Copyright,
      Site = AppInfo.Site,
      SiteUri = AppInfo.SiteUri,
      Title = AppInfo.AboutTitle,
      Contacts = [
        new CoreContact(AppInfo.Security.Role, AppInfo.Security.Address),
        new CoreContact(AppInfo.Support.Role, AppInfo.Support.Address)
      ]
    });
}
