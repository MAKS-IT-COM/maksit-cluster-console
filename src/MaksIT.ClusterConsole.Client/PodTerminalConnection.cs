using System.Text;
using System.Net.WebSockets;
using k8s;


namespace MaksIT.ClusterConsole.Client;

public sealed class PodTerminalConnection : IDisposable {
  private readonly StreamDemuxer _demux;
  private readonly Stream _stdin;
  private readonly Stream _stdout;
  private readonly Stream _resize;
  private readonly CancellationTokenSource _cts = new();
  private readonly SemaphoreSlim _write = new(1, 1);
  private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private int _exitCode = -1;
  private string? _statusMessage;
  private bool _disposed;

  internal PodTerminalConnection(StreamDemuxer demux) {
    _demux = demux;
    _stdout = demux.GetStream(ChannelIndex.StdOut, null);
    _stdin = demux.GetStream(null, ChannelIndex.StdIn);
    _resize = demux.GetStream(null, ChannelIndex.Resize);
    var error = demux.GetStream(ChannelIndex.Error, null);
    _ = ReadStatusAsync(error);
  }

  public int ExitCode => _exitCode;

  public string? StatusMessage => _statusMessage;

  public Task Completed => _completed.Task;

  public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
    return await _stdout.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
  }

  public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) {
    if (data.IsEmpty || _disposed)
      return;

    await _write.WaitAsync(cancellationToken).ConfigureAwait(false);
    try {
      await _stdin.WriteAsync(data, cancellationToken).ConfigureAwait(false);
      await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    finally {
      _write.Release();
    }
  }

  public async ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) {
    if (_disposed || columns < 1 || rows < 1)
      return;

    var payload = PodShell.ResizePayload(columns, rows);
    await _write.WaitAsync(cancellationToken).ConfigureAwait(false);
    try {
      await _resize.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
      await _resize.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    finally {
      _write.Release();
    }
  }

  public void Dispose() {
    if (_disposed)
      return;

    _disposed = true;
    _cts.Cancel();
    try {
      _demux.Dispose();
    }
    catch (WebSocketException) {
    }
    catch (ObjectDisposedException) {
    }

    _write.Dispose();
    _cts.Dispose();
    _completed.TrySetResult();
  }

  private async Task ReadStatusAsync(Stream error) {
    try {
      using var buffer = new MemoryStream();
      await error.CopyToAsync(buffer, _cts.Token).ConfigureAwait(false);
      var status = PodShell.ReadStatus(Encoding.UTF8.GetString(buffer.ToArray()));
      _exitCode = status.Code;
      _statusMessage = status.Message;
    }
    catch (OperationCanceledException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
    catch (WebSocketException) {
    }
    finally {
      try {
        _stdout.Close();
      }
      catch (ObjectDisposedException) {
      }

      _completed.TrySetResult();
    }
  }
}
