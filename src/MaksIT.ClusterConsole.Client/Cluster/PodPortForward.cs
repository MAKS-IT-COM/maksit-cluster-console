using System.Net;
using System.Net.Sockets;
using k8s;
using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Internal;


namespace MaksIT.ClusterConsole.Client.Cluster;

internal static class PodPortForward {
  public static async Task<Result<PortForwardHandle>> StartAsync(
    IKubernetes client,
    string podName,
    string @namespace,
    int containerPort,
    int localPort,
    int requestedPort = 0,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget = null,
    CancellationToken cancellationToken = default) {
    try {
      var listeners = BindLoopback(localPort);
      var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      var handle = new PortForwardHandle(
        podName,
        @namespace,
        containerPort,
        localPort,
        cts,
        () => {
          cts.Cancel();

          foreach (var listener in listeners)
            listener.Stop();
        },
        requestedPort);

      foreach (var listener in listeners)
        _ = AcceptAsync(client, listener, handle, resolveTarget, cts.Token);

      return Result<PortForwardHandle>.Ok(handle);
    }
    catch (Exception ex) {
      return KubernetesResult.Map<PortForwardHandle>(ex);
    }
  }

  private static List<TcpListener> BindLoopback(int port) {
    SocketException? last = null;
    var listeners = new List<TcpListener>(2);

    foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }) {
      try {
        var listener = new TcpListener(address, port);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
          listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);

        listener.Start();
        listeners.Add(listener);
      }
      catch (SocketException ex) {
        last = ex;
      }
    }

    if (listeners.Count == 0)
      throw last ?? new SocketException((int)SocketError.AddressNotAvailable);

    return listeners;
  }

  private static async Task AcceptAsync(
    IKubernetes client,
    TcpListener listener,
    PortForwardHandle handle,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget,
    CancellationToken cancellationToken) {
    try {
      while (!cancellationToken.IsCancellationRequested) {
        var tcp = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        tcp.NoDelay = true;
        _ = PumpConnectionAsync(client, tcp, handle, resolveTarget, cancellationToken);
      }
    }
    catch (OperationCanceledException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (SocketException) {
    }
  }

  private static async Task PumpConnectionAsync(
    IKubernetes client,
    TcpClient tcp,
    PortForwardHandle handle,
    Func<CancellationToken, Task<Result<PortForwardEndpoint>>>? resolveTarget,
    CancellationToken cancellationToken) {
    StreamDemuxer? demux = null;

    try {
      var podName = handle.PodName;
      var @namespace = handle.Namespace;
      var containerPort = handle.ContainerPort;

      if (resolveTarget is not null) {
        var resolved = await resolveTarget(cancellationToken).ConfigureAwait(false);

        if (!resolved.IsSuccess || resolved.Value is null) {
          tcp.Dispose();

          return;
        }

        podName = resolved.Value.PodName;
        @namespace = resolved.Value.Namespace;
        containerPort = resolved.Value.ContainerPort;
        handle.Retarget(podName, @namespace, containerPort);
      }

      var webSocket = await client.WebSocketNamespacedPodPortForwardAsync(
        podName,
        @namespace,
        [containerPort],
        WebSocketProtocol.V4BinaryWebsocketProtocol,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      demux = new StreamDemuxer(webSocket, StreamType.PortForward, ownsSocket: true);
      var stream = demux.GetStream((byte?)0, (byte?)0);
      var errors = demux.GetStream((byte?)1, null);
      demux.Start();
      _ = Task.Run(() => Drain(errors), cancellationToken);
      var socket = tcp.Client;
      using var copyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      await Task.WhenAny(
        Task.Run(() => CopySocketToStream(socket, stream, copyCts.Token), copyCts.Token),
        Task.Run(() => CopyStreamToSocket(stream, socket, copyCts.Token), copyCts.Token)).ConfigureAwait(false);
      copyCts.Cancel();
    }
    catch (OperationCanceledException) {
    }
    catch {
    }
    finally {
      tcp.Dispose();
      demux?.Dispose();
    }
  }

  private static void CopySocketToStream(Socket socket, Stream stream, CancellationToken cancellationToken) {
    var buffer = new byte[16 * 1024];

    try {
      while (!cancellationToken.IsCancellationRequested && socket.Connected) {
        var read = socket.Receive(buffer);

        if (read == 0)
          break;

        stream.Write(buffer, 0, read);
      }
    }
    catch (SocketException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
  }

  private static void CopyStreamToSocket(Stream stream, Socket socket, CancellationToken cancellationToken) {
    var buffer = new byte[16 * 1024];

    try {
      while (!cancellationToken.IsCancellationRequested && socket.Connected) {
        var read = stream.Read(buffer, 0, buffer.Length);

        if (read == 0)
          break;

        var sent = 0;

        while (sent < read)
          sent += socket.Send(buffer, sent, read - sent, SocketFlags.None);
      }
    }
    catch (SocketException) {
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
  }

  private static void Drain(Stream stream) {
    var buffer = new byte[256];

    try {
      while (stream.Read(buffer, 0, buffer.Length) > 0) {
      }
    }
    catch (ObjectDisposedException) {
    }
    catch (IOException) {
    }
  }
}
