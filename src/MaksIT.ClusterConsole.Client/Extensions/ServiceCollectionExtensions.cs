using Microsoft.Extensions.DependencyInjection;


namespace MaksIT.ClusterConsole.Client;

/// <summary>
/// Registers the local Ollama chat client used by the cluster assistant.
/// </summary>
public static class ServiceCollectionExtensions {
  /// <summary>
  /// Adds <see cref="IOllamaChatClient"/> as a typed <see cref="HttpClient"/>.
  /// </summary>
  public static IServiceCollection AddOllamaChatClient(this IServiceCollection services) {
    services.AddHttpClient<IOllamaChatClient, OllamaChatClient>(client => {
      client.Timeout = TimeSpan.FromMinutes(4);
    });
    return services;
  }
}
