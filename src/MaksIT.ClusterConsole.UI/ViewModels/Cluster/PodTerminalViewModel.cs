using System.Net.WebSockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SvcSystems.UI.Terminal;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public sealed class PodShellTarget {
  public string? PodName { get; init; }

  public string? Namespace { get; init; }

  public string? ContainerName { get; init; }

  public bool NeedsPodPicker { get; init; }
}

public partial class PodTerminalViewModel : ObservableObject {
  private readonly ClusterWorkspace _workspace;
  private readonly Func<PodShellTarget> _target;
  private readonly Func<bool> _isActive;
  private PodTerminalConnection? _terminal;
  private int _generation;
  private string? _terminalTarget;
  private string? _holdKey;
  private bool _suspended;
  private int _columns = 80;
  private int _rows = 24;

  public PodTerminalViewModel(
    ClusterWorkspace workspace,
    Func<PodShellTarget> target,
    Func<bool> isActive) {
    _workspace = workspace;
    _target = target;
    _isActive = isActive;
    TerminalModel = new TerminalControlModel(new TerminalOptions {
      Scrollback = 5000,
      ConvertEol = false,
      ReflowOnResize = false,
      TermName = "xterm-256color"
    });
    TerminalModel.UserInput += OnInput;
    TerminalModel.SizeChanged += OnSizeChanged;
  }

  public TerminalControlModel TerminalModel { get; }

  [ObservableProperty]
  private string terminalStatus = "Open a pod and this tab starts a shell.";

  [ObservableProperty]
  private string terminalCommand = "/bin/sh";

  public void Close() {
    _generation++;
    _terminalTarget = null;
    DisposeConnection();
  }

  public void Release(string status) {
    Close();
    TerminalStatus = status;
  }

  public void Show(string text) {
    _suspended = true;
    _holdKey = TargetKey(_target());
    Close();
    TerminalModel.Feed("\u001bc");
    Feed(text);
  }

  public async Task EnsureAsync() {
    if (!_isActive())
      return;

    var key = TargetKey(_target());

    if (key is null) {
      Close();
      _suspended = false;
      _holdKey = null;
      TerminalStatus = _target().NeedsPodPicker
        ? "Select a pod in the details pane to open a shell."
        : "Select a pod to open a shell.";

      return;
    }

    if (_suspended && string.Equals(key, _holdKey, StringComparison.Ordinal))
      return;

    _suspended = false;
    _holdKey = null;

    if (string.Equals(key, _terminalTarget, StringComparison.Ordinal) && _terminal is not null)
      return;

    await OpenAsync();
  }

  [RelayCommand]
  private Task ReconnectTerminal() {
    _suspended = false;
    _holdKey = null;
    Close();

    return OpenAsync();
  }

  [RelayCommand]
  private void DisconnectTerminal() {
    _suspended = true;
    _holdKey = TargetKey(_target());
    Close();
    TerminalStatus = "Disconnected.";
  }

  private async Task OpenAsync() {
    var target = _target();
    var key = TargetKey(target);

    if (key is null || _workspace.Session is null) {
      TerminalStatus = key is null ? "Select a pod to open a shell." : "Not connected.";

      return;
    }

    var generation = ++_generation;
    DisposeConnection();
    _terminalTarget = null;

    var pod = target.PodName!;
    var ns = target.Namespace ?? "default";
    var container = target.ContainerName;
    TerminalStatus = string.IsNullOrEmpty(container)
      ? $"Connecting to {pod}…"
      : $"Connecting to {pod}/{container}…";

    var opened = await _workspace.Session.OpenTerminalAsync(pod, ns, container, TerminalCommand);

    if (generation != _generation) {
      if (opened.IsSuccess)
        opened.Value?.Dispose();

      return;
    }

    if (!opened.IsSuccess || opened.Value is null) {
      var message = string.Join("; ", opened.Messages);

      if (string.IsNullOrWhiteSpace(message))
        message = "Could not open a shell.";

      TerminalStatus = message;
      Feed(message);

      return;
    }

    var connection = opened.Value;
    _terminal = connection;
    _terminalTarget = key;
    TerminalModel.Feed("\u001bc");
    var label = string.IsNullOrEmpty(container) ? $"{ns}/{pod}" : $"{ns}/{pod}/{container}";
    TerminalStatus = $"{label}  ·  {TerminalCommand.Trim()}";
    _ = ResizeAsync(connection, _columns, _rows);
    _ = PumpAsync(connection, generation);
  }

  private void OnInput(object? sender, TerminalUserInputEventArgs e) {
    var connection = _terminal;

    if (connection is null || e.Data.Length == 0)
      return;

    var bytes = e.Data.ToArray();
    _ = WriteAsync(connection, bytes);
  }

  private async Task WriteAsync(PodTerminalConnection connection, byte[] bytes) {
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

  private void OnSizeChanged(object? sender, TerminalSizeChangedEventArgs e) {
    if (e.Cols < 1 || e.Rows < 1)
      return;

    _columns = e.Cols;
    _rows = e.Rows;
    var connection = _terminal;

    if (connection is null)
      return;

    _ = ResizeAsync(connection, e.Cols, e.Rows);
  }

  private static async Task ResizeAsync(PodTerminalConnection connection, int columns, int rows) {
    try {
      await connection.ResizeAsync(columns, rows);
    }
    catch (Exception ex) when (ex is ObjectDisposedException or IOException or WebSocketException) {
    }
  }

  private async Task PumpAsync(PodTerminalConnection connection, int generation) {
    var buffer = new byte[8192];
    string? fault = null;

    try {
      while (generation == _generation) {
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
        Feed(copy, copy.Length, generation);
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
      if (generation != _generation)
        return;

      if (!string.IsNullOrWhiteSpace(message))
        Feed(message);
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

  private void Feed(string text) {
    var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    if (Dispatcher.UIThread.CheckAccess())
      TerminalModel.Feed(normalized);
    else
      Dispatcher.UIThread.Post(() => TerminalModel.Feed(normalized));
  }

  private void Feed(byte[] data, int length, int generation) {
    void Apply() {
      if (generation != _generation)
        return;

      TerminalModel.Feed(data, length);
    }

    if (Dispatcher.UIThread.CheckAccess())
      Apply();
    else
      Dispatcher.UIThread.Post(Apply);
  }

  private static string? TargetKey(PodShellTarget target) {
    if (target.PodName is null || target.Namespace is null)
      return null;

    return target.Namespace + "\n" + target.PodName + "\n" + (target.ContainerName ?? "");
  }

  private void DisposeConnection() {
    var connection = _terminal;
    _terminal = null;
    connection?.Dispose();
  }
}
