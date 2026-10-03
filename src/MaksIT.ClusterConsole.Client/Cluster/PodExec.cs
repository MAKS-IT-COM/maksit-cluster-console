using System.Text;
using k8s;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Internal;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.Client.Cluster;

internal static class PodExec {
  public static async Task<Result<ExecBytesResult>> RunAsync(
    IKubernetes client,
    string podName,
    string @namespace,
    string? container,
    IReadOnlyList<string> command,
    byte[]? stdin = null,
    CancellationToken cancellationToken = default) {
    try {
      var cmd = command.Count == 0 ? new[] { "sh", "-c", "echo ok" } : command.ToArray();
      var webSocket = await client.WebSocketNamespacedPodExecAsync(
        podName,
        @namespace,
        command: cmd,
        container: container,
        stderr: true,
        stdin: stdin is not null,
        stdout: true,
        tty: false,
        cancellationToken: cancellationToken).ConfigureAwait(false);

      using var demux = new StreamDemuxer(webSocket);
      demux.Start();
      using var stdout = demux.GetStream(ChannelIndex.StdOut, null);
      using var stderr = demux.GetStream(ChannelIndex.StdErr, null);
      using var error = demux.GetStream(ChannelIndex.Error, null);
      var stdoutTask = ReadAllAsync(stdout, cancellationToken);
      var stderrTask = ReadAllAsync(stderr, cancellationToken);
      var errorTask = ReadAllAsync(error, cancellationToken);

      if (stdin is not null) {
        using (var stdinStream = demux.GetStream(null, ChannelIndex.StdIn)) {
          if (stdin.Length > 0)
            await stdinStream.WriteAsync(stdin, cancellationToken).ConfigureAwait(false);

          await stdinStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
      }

      await Task.WhenAll(stdoutTask, stderrTask, errorTask).ConfigureAwait(false);
      var err = Encoding.UTF8.GetString(stderrTask.Result).TrimEnd();
      var status = Encoding.UTF8.GetString(errorTask.Result).TrimEnd();

      if (string.IsNullOrEmpty(err))
        err = status;

      return Result<ExecBytesResult>.Ok(new ExecBytesResult(stdoutTask.Result, err));
    }
    catch (Exception ex) {
      return KubernetesResult.Map<ExecBytesResult>(ex);
    }
  }

  public static async Task<Result<PodTerminalConnection>> OpenShellAsync(
    IKubernetes client,
    string podName,
    string @namespace,
    string? container,
    string? shell,
    CancellationToken cancellationToken = default) {
    try {
      var webSocket = await client.WebSocketNamespacedPodExecAsync(
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

  private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken) {
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

    return buffer.ToArray();
  }
}
