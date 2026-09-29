using System.Net.Sockets;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ErrorReportTests {
  [Fact]
  public void Abandoned_watch_abort_is_not_a_crash() {
    var socket = new SocketException((int)SocketError.OperationAborted);
    var io = new IOException(
      "Unable to read data from the transport connection: " + socket.Message,
      socket);
    var aggregate = new AggregateException(
      "A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property.",
      io);

    Assert.True(ErrorReport.IsAbandonedTransportRead(aggregate));
  }

  [Fact]
  public void Abort_message_without_socket_error_is_not_a_crash() {
    var io = new IOException(
      "Unable to read data from the transport connection: The I/O operation has been aborted because of either a thread exit or an application request.");

    Assert.True(ErrorReport.IsAbandonedTransportRead(io));
  }

  [Fact]
  public void Connection_reset_stays_reportable() {
    var socket = new SocketException((int)SocketError.ConnectionReset);
    var io = new IOException("Unable to read data from the transport connection: " + socket.Message, socket);

    Assert.False(ErrorReport.IsAbandonedTransportRead(io));
  }

  [Fact]
  public void Other_unobserved_exceptions_stay_reportable() {
    var aggregate = new AggregateException(new InvalidOperationException("boom"));

    Assert.False(ErrorReport.IsAbandonedTransportRead(aggregate));
  }
}
