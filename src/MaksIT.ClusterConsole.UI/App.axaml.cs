using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MaksIT.ClusterConsole.Client;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace MaksIT.ClusterConsole.UI;

public partial class App : Application {
  private IHost? _host;

  public override void Initialize() =>
    AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted() {
    _host = Host.CreateDefaultBuilder()
      .ConfigureAppConfiguration(builder => {
        builder.SetBasePath(AppContext.BaseDirectory);
        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
      })
      .ConfigureServices((_, services) => {
        services.AddSingleton(_ => new ConfigurationFileService());
        services.AddSingleton<IKubeConfigService, KubeConfigService>();
        services.AddSingleton<IClusterSessionFactory, ClusterSessionFactory>();
        services.AddOllamaChatClient();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
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
}
