using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Controls;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;


namespace MaksIT.ClusterConsole.UI.Services;

internal sealed class ScreenSaverController : IDisposable {
  private readonly Window _window;
  private readonly MainViewModel _viewModel;
  private readonly ConfigurationFileService _configuration;
  private readonly BubblesScreenSaverControl _overlay;
  private readonly DispatcherTimer _idleTimer;
  private bool _attached;
  private bool _active;

  public ScreenSaverController(
    Window window,
    MainViewModel viewModel,
    ConfigurationFileService configuration,
    BubblesScreenSaverControl overlay) {
    _window = window;
    _viewModel = viewModel;
    _configuration = configuration;
    _overlay = overlay;
    _idleTimer = new DispatcherTimer();
    _idleTimer.Tick += (_, _) => Activate();
  }

  public bool IsActive => _active;

  public void Attach() {
    if (_attached)
      return;

    _attached = true;
    _window.PropertyChanged += OnWindowPropertyChanged;
    _window.AddHandler(InputElement.PointerMovedEvent, OnPointerActivity, RoutingStrategies.Tunnel);
    _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    _window.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
    _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    _viewModel.ScreenSaverSettingsChanged += OnSettingsChanged;
    SyncFromWindowState();
  }

  public void Dispose() {
    if (!_attached)
      return;

    _attached = false;
    _idleTimer.Stop();
    _overlay.Stop();
    _window.ClearValue(InputElement.CursorProperty);
    _window.PropertyChanged -= OnWindowPropertyChanged;
    _window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerActivity);
    _window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
    _window.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheel);
    _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
    _viewModel.ScreenSaverSettingsChanged -= OnSettingsChanged;
  }

  public bool TryDismiss() {
    if (!_active)
      return false;

    Dismiss();

    return true;
  }

  private void OnSettingsChanged() =>
    SyncFromWindowState();

  private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e) {
    if (e.Property == Window.WindowStateProperty)
      SyncFromWindowState();
  }

  private void SyncFromWindowState() {
    if (_window.WindowState != WindowState.FullScreen) {
      StopIdle();
      Dismiss();

      return;
    }

    RestartIdleIfNeeded();
  }

  private void OnPointerActivity(object? sender, PointerEventArgs e) =>
    HandleActivity(e);

  private void OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
    HandleActivity(e);

  private void OnPointerWheel(object? sender, PointerWheelEventArgs e) =>
    HandleActivity(e);

  private void HandleActivity(RoutedEventArgs e) {
    if (_window.WindowState != WindowState.FullScreen)
      return;

    if (_active) {
      Dismiss();
      e.Handled = true;

      return;
    }

    RestartIdleIfNeeded();
  }

  private void OnKeyDown(object? sender, KeyEventArgs e) {
    if (_window.WindowState != WindowState.FullScreen)
      return;

    if (_active) {
      Dismiss();
      e.Handled = true;

      return;
    }

    if (e.Key == Key.Escape)
      return;

    RestartIdleIfNeeded();
  }

  private void RestartIdleIfNeeded() {
    _idleTimer.Stop();

    var settings = _configuration.Current.ScreenSaver;
    settings.Ensure();

    if (!settings.Enabled || _window.WindowState != WindowState.FullScreen)
      return;

    _idleTimer.Interval = TimeSpan.FromMinutes(settings.IdleMinutes);
    _idleTimer.Start();
  }

  private void StopIdle() =>
    _idleTimer.Stop();

  private void Activate() {
    _idleTimer.Stop();

    if (!_configuration.Current.ScreenSaver.Enabled || _window.WindowState != WindowState.FullScreen)
      return;

    _active = true;
    _overlay.Start();
    _window.Cursor = new Cursor(StandardCursorType.None);
  }

  private void Dismiss() {
    if (!_active && !_overlay.IsVisible)
      return;

    _active = false;
    _overlay.Stop();
    _window.ClearValue(InputElement.CursorProperty);
    RestartIdleIfNeeded();
  }
}
