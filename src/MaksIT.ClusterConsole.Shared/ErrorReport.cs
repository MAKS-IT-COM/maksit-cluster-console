using System.Net.Sockets;
using System.Reflection;
using MaksIT.Core.Desktop;


namespace MaksIT.ClusterConsole.Shared;

/// <summary>Formats unhandled exceptions for a copyable dialog and a crash log file.</summary>
public static class ErrorReport {
  public static string Capture(Exception exception) =>
    CrashReport.Capture(
      exception,
      AppInfo.ProductName + " " + Version(),
      AppInfo.Brand,
      AppLog.Directory,
      AppLog.Write);

  public static string Format(Exception exception) =>
    CrashReport.Format(exception, AppInfo.ProductName + " " + Version(), AppInfo.Brand);

  /// <summary>
  /// A cancelled Kubernetes watch aborts the socket while <c>ReadLineAsync</c> is still running.
  /// That task is not observed by KubernetesClient, so the finalizer rethrows it.
  /// </summary>
  public static bool IsAbandonedTransportRead(Exception exception) {
    ArgumentNullException.ThrowIfNull(exception);

    foreach (var current in Walk(exception)) {
      if (current is SocketException { SocketErrorCode: SocketError.OperationAborted })
        return true;

      if (current.Message.Contains("I/O operation has been aborted", StringComparison.Ordinal))
        return true;
    }

    return false;
  }

  private static string Version() {
    var version = Assembly.GetEntryAssembly()?.GetName().Version;

    return version is null ? "" : version.ToString();
  }

  private static IEnumerable<Exception> Walk(Exception exception) {
    var pending = new Stack<Exception>();
    var seen = new HashSet<Exception>();
    pending.Push(exception);

    while (pending.Count > 0) {
      var current = pending.Pop();

      if (!seen.Add(current))
        continue;

      yield return current;

      if (current is AggregateException aggregate) {
        for (var i = aggregate.InnerExceptions.Count - 1; i >= 0; i--) {
          var inner = aggregate.InnerExceptions[i];

          if (inner is not null)
            pending.Push(inner);
        }
      }

      if (current.InnerException is not null)
        pending.Push(current.InnerException);
    }
  }
}
