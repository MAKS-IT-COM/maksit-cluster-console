using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;


namespace MaksIT.ClusterConsole.Shared;


/// <summary>Formats unhandled exceptions for a copyable dialog and a crash log file.</summary>
public static class ErrorReport {
  public static string Capture(Exception exception) {
    ArgumentNullException.ThrowIfNull(exception);
    var body = Format(exception);
    AppLog.Write(body);
    var path = TryWrite(body);

    if (string.IsNullOrWhiteSpace(path))
      return body;

    return body + Environment.NewLine + Environment.NewLine + "Log: " + path;
  }

  public static string Format(Exception exception) {
    ArgumentNullException.ThrowIfNull(exception);
    var text = new StringBuilder();
    text.AppendLine(AppInfo.ProductName + " " + Version());
    text.AppendLine(AppInfo.Brand);
    text.AppendLine(DateTimeOffset.UtcNow.ToString("u"));
    text.AppendLine(RuntimeInformation.OSDescription);
    text.AppendLine(RuntimeInformation.FrameworkDescription);
    text.AppendLine((Environment.Is64BitProcess ? "64-bit" : "32-bit") + " process");
    text.AppendLine();
    AppendException(text, exception);

    return text.ToString().TrimEnd();
  }

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

  public static string? TryWrite(string report) {
    try {
      var dir = AppLog.Directory();
      System.IO.Directory.CreateDirectory(dir);
      var name = "crash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId + ".txt";
      var path = Path.Combine(dir, name);
      File.WriteAllText(path, report ?? "");

      return path;
    }
    catch {
      return null;
    }
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

  private static void AppendException(StringBuilder text, Exception exception) {
    var seen = new HashSet<Exception>();
    var current = exception;
    var depth = 0;

    while (current is not null && seen.Add(current)) {
      if (depth > 0)
        text.AppendLine().AppendLine("--- inner ---");

      text.AppendLine(current.GetType().FullName);
      text.AppendLine(current.Message);

      if (!string.IsNullOrWhiteSpace(current.StackTrace))
        text.AppendLine(current.StackTrace);

      if (current is AggregateException aggregate) {
        foreach (var inner in aggregate.InnerExceptions) {
          if (inner is null || ReferenceEquals(inner, current.InnerException) || !seen.Add(inner))
            continue;

          text.AppendLine().AppendLine("--- aggregate ---");
          text.AppendLine(inner.ToString());
        }
      }

      current = current.InnerException;
      depth++;
    }
  }
}
