using System.Net;
using k8s.Autorest;


namespace MaksIT.ClusterConsole.Client.Internal;

internal static class KubernetesApiRetry {
  private const int MaxAttempts = 4;

  public static bool IsTransient(Exception ex) {
    if (ex is OperationCanceledException)
      return false;

    if (IsRateLimited(ex))
      return true;

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

  public static bool IsRateLimited(Exception ex) {
    if (ex is OperationCanceledException)
      return false;

    for (var current = ex; current is not null; current = current.InnerException) {
      if (IsTooManyRequests(current))
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
        await Task.Delay(Delay(ex, attempt), cancellationToken).ConfigureAwait(false);
      }
    }

    throw last ?? new InvalidOperationException("Kubernetes API retry failed without an exception.");
  }

  internal static TimeSpan Delay(Exception ex, int attempt) {
    var seconds = RetryAfterSeconds(ex);

    if (seconds is not null)
      return TimeSpan.FromSeconds(Math.Clamp(seconds.Value, 1, 5));

    if (IsRateLimited(ex))
      return TimeSpan.FromSeconds(1);

    return TimeSpan.FromMilliseconds(200 * attempt);
  }

  private static bool IsTooManyRequests(Exception ex) {
    if (ex is HttpOperationException http && http.Response?.StatusCode == HttpStatusCode.TooManyRequests)
      return true;

    var message = Describe(ex);

    return message.Contains("TooManyRequests", StringComparison.Ordinal)
      || message.Contains("storage is (re)initializing", StringComparison.Ordinal);
  }

  private static int? RetryAfterSeconds(Exception ex) {
    for (var current = ex; current is not null; current = current.InnerException) {
      var seconds = ParseRetryAfterSeconds(Describe(current));

      if (seconds is not null)
        return seconds;
    }

    return null;
  }

  private static string Describe(Exception ex) {
    if (ex is HttpOperationException http && !string.IsNullOrEmpty(http.Response?.Content))
      return ex.Message + " " + http.Response.Content;

    return ex.Message;
  }

  private static int? ParseRetryAfterSeconds(string message) {
    const string key = "\"retryAfterSeconds\"";
    var index = message.IndexOf(key, StringComparison.Ordinal);

    if (index < 0)
      return null;

    var start = index + key.Length;

    while (start < message.Length && message[start] is ':' or ' ')
      start++;

    var end = start;

    while (end < message.Length && char.IsDigit(message[end]))
      end++;

    if (end == start || !int.TryParse(message[start..end], out var seconds))
      return null;

    return seconds;
  }
}
