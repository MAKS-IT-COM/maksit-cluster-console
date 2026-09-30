using k8s;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Internal;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.Client.Cluster;

public sealed partial class ClusterSession {
  public async Task<Result<PodTerminalConnection>> OpenTerminalAsync(
    string podName,
    string @namespace,
    string? container,
    string? shell,
    CancellationToken cancellationToken = default) {
    try {
      var webSocket = await _client.WebSocketNamespacedPodExecAsync(
        podName,
        @namespace,
        PodShell.BuildCommand(shell),
        container,
        stderr: false,
        stdin: true,
        stdout: true,
        tty: true,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      var demux = new StreamDemuxer(webSocket, ownsSocket: true);
      try {
        demux.Start();
        return Result<PodTerminalConnection>.Ok(new PodTerminalConnection(demux));
      }
      catch {
        demux.Dispose();
        throw;
      }
    }
    catch (Exception ex) {
      return KubernetesResult.Map<PodTerminalConnection>(ex);
    }
  }
}
