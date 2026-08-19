using System.Text.Json;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public sealed class ConfigurationFileService {
  private static readonly JsonSerializerOptions SerializerOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = null
  };

  private Configuration _current;

  public string FilePath { get; }

  public Configuration Current => _current;

  public ConfigurationFileService(string? configurationPath = null) {
    FilePath = configurationPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    _current = LoadFromDisk();
  }

  public Configuration Reload() {
    _current = LoadFromDisk();
    return _current;
  }

  public void Save(Configuration configuration) {
    ArgumentNullException.ThrowIfNull(configuration);
    configuration.EnsureDefaults();
    JsonObject root;
    if (File.Exists(FilePath)) {
      root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [];
    }
    else {
      root = [];
    }

    root["Configuration"] = JsonSerializer.SerializeToNode(configuration, SerializerOptions);
    File.WriteAllText(FilePath, root.ToJsonString(SerializerOptions));
    _current = configuration;
  }

  private Configuration LoadFromDisk() {
    if (!File.Exists(FilePath))
      return new Configuration();

    using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
    if (!document.RootElement.TryGetProperty("Configuration", out var value))
      return new Configuration();

    var configuration = JsonSerializer.Deserialize<Configuration>(value.GetRawText(), SerializerOptions) ?? new Configuration();
    configuration.EnsureDefaults();
    return configuration;
  }
}
