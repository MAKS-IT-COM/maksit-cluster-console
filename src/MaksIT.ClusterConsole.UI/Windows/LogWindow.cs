using Avalonia.Controls;
using MaksIT.ClusterConsole.Shared;
using MaksIT.Core.UI.Logs;
using CoreLog = MaksIT.Core.UI.Logs.LogWindow;


namespace MaksIT.ClusterConsole.UI.Windows;

public static class LogWindow {
  public static Task ShowAsync(Window owner) =>
    CoreLog.ShowAsync(owner, new DesktopLogOptions {
      Directory = AppLog.Directory
    });
}
