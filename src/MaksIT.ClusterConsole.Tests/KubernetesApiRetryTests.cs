using System.Net;
using MaksIT.ClusterConsole.Client;


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
}
