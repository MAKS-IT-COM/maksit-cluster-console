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
  public void Truncated_settings_reload_is_not_a_crash() {
    var parse = new FormatException("Could not parse the JSON file.");
    var invalid = new InvalidDataException(
      "Failed to load configuration from file 'C:\\Users\\maksym\\AppData\\Roaming\\MaksIT\\Cluster Console\\settings.json'.",
      parse);
    var aggregate = new AggregateException(
      "A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property.",
      invalid);

    Assert.True(ErrorReport.IsConfigurationReloadFailure(aggregate));
    Assert.False(ErrorReport.IsConfigurationReloadFailure(new InvalidOperationException("boom")));
  }

  [Fact]
  public void Other_unobserved_exceptions_stay_reportable() {
    var aggregate = new AggregateException(new InvalidOperationException("boom"));

    Assert.False(ErrorReport.IsAbandonedTransportRead(aggregate));
  }

  [Fact]
  public void Format_includes_the_product_and_the_exception_message() {
    var text = ErrorReport.Format(new InvalidOperationException("boom"));

    Assert.Contains("boom", text, StringComparison.Ordinal);
    Assert.Contains(AppInfo.ProductName, text, StringComparison.Ordinal);
  }

  [Fact]
  public void Repeated_inner_exceptions_are_walked_once() {
    var inner = new InvalidOperationException("inner");
    var aggregate = new AggregateException(inner, inner);

    Assert.False(ErrorReport.IsAbandonedTransportRead(aggregate));
  }
}
