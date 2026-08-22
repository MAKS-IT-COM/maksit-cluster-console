using System.Net;


namespace MaksIT.ClusterConsole.Client;

internal static class KubernetesApiRetry {
  private const int MaxAttempts = 3;

  public static bool IsTransient(Exception ex) {
    if (ex is OperationCanceledException)
      return false;

    for (var current = ex; current is not null; current = current.InnerException) {
      if (current is HttpIOException or HttpRequestException)
        return true;

      var message = current.Message;
      if (message.Contains("ResponseEnded", StringComparison.Ordinal)
          || message.Contains("prematurely", StringComparison.OrdinalIgnoreCase)
          || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
          || message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }

  public static async Task<T> ExecuteAsync<T>(
    Func<CancellationToken, Task<T>> action,
    CancellationToken cancellationToken) {
    Exception? last = null;
    for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
      try {
        return await action(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex)) {
        last = ex;
        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
      }
    }

    throw last ?? new InvalidOperationException("Kubernetes API retry failed without an exception.");
  }
}
