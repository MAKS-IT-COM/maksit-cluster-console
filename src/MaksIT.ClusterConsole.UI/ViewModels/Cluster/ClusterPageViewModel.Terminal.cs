using System.Net.WebSockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvcSystems.UI.Terminal;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class ClusterPageViewModel {
  private PodTerminalConnection? _terminal;
  private int _terminalGeneration;
  private string? _terminalTarget;
  private string? _terminalHoldKey;
  private bool _terminalSuspended;
  private int _terminalColumns = 80;
  private int _terminalRows = 24;

  public TerminalControlModel TerminalModel { get; private set; } = null!;

  [ObservableProperty]
  private string terminalStatus = "Open a pod and this tab starts a shell.";

  partial void OnSelectedTabChanged(string value) {
    if (value == "Terminal")
      _ = EnsureTerminalAsync();
  }

  [RelayCommand]
  private Task ReconnectTerminal() {
    _terminalSuspended = false;
    _terminalHoldKey = null;
    CloseTerminalSession();
    return OpenTerminalAsync();
  }

  [RelayCommand]
  private void DisconnectTerminal() {
    _terminalSuspended = true;
    _terminalHoldKey = TerminalTargetKey();
    CloseTerminalSession();
    TerminalStatus = "Disconnected.";
  }

  private void WireTerminal() {
    TerminalModel = new TerminalControlModel(new TerminalOptions {
      Scrollback = 5000,
      ConvertEol = false,
      ReflowOnResize = false,
      TermName = "xterm-256color"
    });
    TerminalModel.UserInput += OnTerminalInput;
    TerminalModel.SizeChanged += OnTerminalSizeChanged;
  }

  private void OnTerminalInput(object? sender, TerminalUserInputEventArgs e) {
    var connection = _terminal;
    if (connection is null || e.Data.Length == 0)
      return;

    var bytes = e.Data.ToArray();
    _ = WriteTerminalAsync(connection, bytes);
  }

  private async Task WriteTerminalAsync(PodTerminalConnection connection, byte[] bytes) {
    try {
      await connection.WriteAsync(bytes);
    }
    catch (Exception ex) when (ex is ObjectDisposedException or IOException or WebSocketException) {
      await Dispatcher.UIThread.InvokeAsync(() => {
        if (ReferenceEquals(_terminal, connection))
          TerminalStatus = "The shell is no longer accepting input.";
      });
    }
  }

  private void OnTerminalSizeChanged(object? sender, TerminalSizeChangedEventArgs e) {
    if (e.Cols < 1 || e.Rows < 1)
      return;

    _terminalColumns = e.Cols;
    _terminalRows = e.Rows;
    var connection = _terminal;
    if (connection is null)
      return;

    _ = ResizeTerminalAsync(connection, e.Cols, e.Rows);
  }

  private static async Task ResizeTerminalAsync(PodTerminalConnection connection, int columns, int rows) {
    try {
      await connection.ResizeAsync(columns, rows);
    }
    catch (Exception ex) when (ex is ObjectDisposedException or IOException or WebSocketException) {
    }
  }

  private async Task EnsureTerminalAsync() {
    if (SelectedTab != "Terminal")
      return;

    var key = TerminalTargetKey();
    if (key is null) {
      CloseTerminalSession();
      _terminalSuspended = false;
      _terminalHoldKey = null;
      TerminalStatus = ShowPodsTab
        ? "Select a pod in the details pane to open a shell."
        : "Select a pod to open a shell.";
      return;
    }

    if (_terminalSuspended && string.Equals(key, _terminalHoldKey, StringComparison.Ordinal))
      return;

    _terminalSuspended = false;
    _terminalHoldKey = null;
    if (string.Equals(key, _terminalTarget, StringComparison.Ordinal) && _terminal is not null)
      return;

    await OpenTerminalAsync();
  }

  private async Task OpenTerminalAsync() {
    var key = TerminalTargetKey();
    if (key is null || _workspace.Session is null) {
      TerminalStatus = key is null ? "Select a pod to open a shell." : "Not connected.";
      return;
    }

    var generation = ++_terminalGeneration;
    DisposeTerminal();
    _terminalTarget = null;

    var pod = TargetPodName!;
    var ns = TargetPodNamespace ?? "default";
    var container = SelectedContainer?.Name;
    TerminalStatus = string.IsNullOrEmpty(container)
      ? $"Connecting to {pod}…"
      : $"Connecting to {pod}/{container}…";

    var opened = await _workspace.Session.OpenTerminalAsync(pod, ns, container, TerminalCommand);
    if (generation != _terminalGeneration) {
      if (opened.IsSuccess)
        opened.Value?.Dispose();
      return;
    }

    if (!opened.IsSuccess || opened.Value is null) {
      var message = string.Join("; ", opened.Messages);
      if (string.IsNullOrWhiteSpace(message))
        message = "Could not open a shell.";
      TerminalStatus = message;
      FeedTerminal(message);
      return;
    }

    var connection = opened.Value;
    _terminal = connection;
    _terminalTarget = key;
    TerminalModel.Feed("\u001bc");
    var label = string.IsNullOrEmpty(container) ? $"{ns}/{pod}" : $"{ns}/{pod}/{container}";
    TerminalStatus = $"{label}  ·  {TerminalCommand.Trim()}";
    _ = ResizeTerminalAsync(connection, _terminalColumns, _terminalRows);
    _ = PumpTerminalAsync(connection, generation);
  }

  private async Task PumpTerminalAsync(PodTerminalConnection connection, int generation) {
    var buffer = new byte[8192];
    string? fault = null;
    try {
      while (generation == _terminalGeneration) {
        int read;
        try {
          read = await connection.ReadAsync(buffer);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or WebSocketException) {
          break;
        }
        catch (Exception ex) {
          fault = ex.Message;
          break;
        }

        if (read == 0)
          break;

        var copy = new byte[read];
        buffer.AsSpan(0, read).CopyTo(copy);
        FeedTerminal(copy, copy.Length, generation);
      }
    }
    catch (Exception ex) {
      fault = ex.Message;
    }

    try {
      await connection.Completed.WaitAsync(TimeSpan.FromSeconds(1));
    }
    catch (TimeoutException) {
    }

    var message = fault ?? connection.StatusMessage;
    var code = connection.ExitCode;
    await Dispatcher.UIThread.InvokeAsync(() => {
      if (generation != _terminalGeneration)
        return;

      if (!string.IsNullOrWhiteSpace(message))
        FeedTerminal(message);
      else if (code >= 0)
        TerminalModel.Feed($"\r\n[process exited with code {code}]\r\n");
      else
        TerminalModel.Feed("\r\n[session closed]\r\n");

      TerminalStatus = !string.IsNullOrWhiteSpace(message)
        ? message
        : code >= 0 ? $"Process exited with code {code}." : "Session closed.";

      if (ReferenceEquals(_terminal, connection)) {
        _terminal = null;
        _terminalTarget = null;
      }

      connection.Dispose();
    });
  }

  private void ShowInTerminal(string text) {
    _terminalSuspended = true;
    _terminalHoldKey = TerminalTargetKey();
    CloseTerminalSession();
    TerminalModel.Feed("\u001bc");
    FeedTerminal(text);
  }

  private void FeedTerminal(string text) {
    var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
    if (Dispatcher.UIThread.CheckAccess())
      TerminalModel.Feed(normalized);
    else
      Dispatcher.UIThread.Post(() => TerminalModel.Feed(normalized));
  }

  private void FeedTerminal(byte[] data, int length, int generation) {
    void Apply() {
      if (generation != _terminalGeneration)
        return;
      TerminalModel.Feed(data, length);
    }

    if (Dispatcher.UIThread.CheckAccess())
      Apply();
    else
      Dispatcher.UIThread.Post(Apply);
  }

  private string? TerminalTargetKey() {
    var pod = TargetPodName;
    var ns = TargetPodNamespace;
    if (pod is null || ns is null)
      return null;

    return ns + "\n" + pod + "\n" + (SelectedContainer?.Name ?? "");
  }

  private void CloseTerminalSession() {
    _terminalGeneration++;
    _terminalTarget = null;
    DisposeTerminal();
  }

  private void DisposeTerminal() {
    var connection = _terminal;
    _terminal = null;
    connection?.Dispose();
  }
}
