using System.Net;
using MaksIT.ClusterConsole.Client.Internal;


namespace MaksIT.ClusterConsole.Tests;

public class KubernetesApiRetryTests {
  [Fact]
  public void IsTransient_detects_http_response_ended() {
    var ex = new HttpRequestException("The response ended prematurely while waiting for the next frame from the server. (ResponseEnded)");
    Assert.True(KubernetesApiRetry.IsTransient(ex));
  }

  [Fact]
  public async Task ExecuteAsync_retries_transient_failures() {
    var attempts = 0;
    var result = await KubernetesApiRetry.ExecuteAsync(_ => {
      attempts++;

      if (attempts < 2)
        throw new HttpRequestException("ResponseEnded");

      return Task.FromResult(42);
    }, CancellationToken.None);

    Assert.Equal(42, result);
    Assert.Equal(2, attempts);
  }

  [Fact]
  public void IsTransient_detects_storage_initializing() {
    var ex = StorageInitializing();

    Assert.True(KubernetesApiRetry.IsTransient(ex));
    Assert.True(KubernetesApiRetry.IsRateLimited(ex));
    Assert.Equal(TimeSpan.FromSeconds(1), KubernetesApiRetry.Delay(ex, 1));
  }

  [Fact]
  public async Task ExecuteAsync_retries_storage_initializing() {
    var attempts = 0;
    var result = await KubernetesApiRetry.ExecuteAsync(_ => {
      attempts++;

      if (attempts < 2)
        throw StorageInitializing();

      return Task.FromResult(7);
    }, CancellationToken.None);

    Assert.Equal(7, result);
    Assert.Equal(2, attempts);
  }

  private static HttpRequestException StorageInitializing() =>
    new("Operation returned an invalid status code 'TooManyRequests', response body {\"message\":\"storage is (re)initializing\",\"reason\":\"TooManyRequests\",\"details\":{\"retryAfterSeconds\":1},\"code\":429}");
}
