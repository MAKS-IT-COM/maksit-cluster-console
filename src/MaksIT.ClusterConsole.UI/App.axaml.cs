using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Windows;
using MaksIT.ClusterConsole.Client.Ollama;
using MaksIT.ClusterConsole.Client.Cluster;
using MaksIT.ClusterConsole.Client.Extensions;
using MaksIT.ClusterConsole.Client.KubeConfig;
using MaksIT.ClusterConsole.UI.ViewModels.Shell;


namespace MaksIT.ClusterConsole.UI;

public partial class App : Application {
  private IHost? _host;

  public override void Initialize() =>
    AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted() {
    ScreenshotTourOptions? tour = null;

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime launch
        && !ScreenshotTourOptions.TryParse(launch.Args, out tour, out var tourError))
      FailScreenshotTour(tourError);

    _host = Host.CreateDefaultBuilder()
      .ConfigureAppConfiguration(builder => {
        builder.SetBasePath(AppContext.BaseDirectory);
        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        builder.AddJsonFile(
          UserSettingsPath.Get(ConfigurationFileService.ProductFolder),
          optional: true,
          reloadOnChange: true);
      })
      .ConfigureServices((_, services) => {
        services.AddSingleton(_ => new ConfigurationFileService());
        services.AddSingleton<IKubeConfigService, KubeConfigService>();
        services.AddSingleton<IClusterSessionFactory, ClusterSessionFactory>();
        services.AddOllamaChatClient();
        services.AddSingleton(sp => new MainViewModel(
          sp.GetRequiredService<IKubeConfigService>(),
          sp.GetRequiredService<IClusterSessionFactory>(),
          sp.GetRequiredService<ConfigurationFileService>(),
          sp.GetRequiredService<IOllamaChatClient>(),
          tour));
        services.AddSingleton(sp => new MainWindow(
          sp.GetRequiredService<MainViewModel>(),
          sp.GetRequiredService<ConfigurationFileService>(),
          tour));
      })
      .Build();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
      desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
      desktop.ShutdownRequested += async (_, _) => {
        if (_host is null)
          return;

        _host.Services.GetRequiredService<MainViewModel>().Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
      };
    }

    base.OnFrameworkInitializationCompleted();
  }

  private static void FailScreenshotTour(string? error) {
    try {
      if (OperatingSystem.IsWindows())
        AttachConsole(uint.MaxValue);

      Console.Error.WriteLine(error ?? "Invalid screenshot arguments.");
    }
    catch (Exception) {
      // A windowed process may have no console.
    }

    Environment.Exit(2);
  }

  [System.Runtime.InteropServices.DllImport("kernel32.dll")]
  private static extern bool AttachConsole(uint processId);
}
