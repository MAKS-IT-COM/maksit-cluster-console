using System.Text.Json;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public sealed class ConfigurationFileService {
  public const string ProductFolder = "Cluster Console";
  public const string SeedFileName = "appsettings.json";

  private static readonly JsonSerializerOptions SerializerOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = null
  };

  private readonly string? _seedPath;
  private Configuration _current;

  public string FilePath { get; }

  public Configuration Current => _current;

  public ConfigurationFileService(string? configurationPath = null, string? seedPath = null) {
    if (!string.IsNullOrWhiteSpace(configurationPath))
      FilePath = configurationPath;
    else
      FilePath = UserSettingsPath.Get(ProductFolder);

    if (!string.IsNullOrWhiteSpace(seedPath))
      _seedPath = seedPath;
    else if (string.IsNullOrWhiteSpace(configurationPath))
      _seedPath = Path.Combine(AppContext.BaseDirectory, SeedFileName);
    else
      _seedPath = null;

    _current = LoadFromDisk();
    CopySeedIfNeeded();
  }

  public Configuration Reload() {
    _current = LoadFromDisk();
    return _current;
  }

  public void Save(Configuration configuration) {
    ArgumentNullException.ThrowIfNull(configuration);
    configuration.EnsureDefaults();

    var dir = Path.GetDirectoryName(FilePath);
    if (!string.IsNullOrEmpty(dir))
      Directory.CreateDirectory(dir);

    var root = ReadRoot(File.Exists(FilePath) ? FilePath : null) ?? [];
    root["Configuration"] = JsonSerializer.SerializeToNode(configuration, SerializerOptions);
    File.WriteAllText(FilePath, root.ToJsonString(SerializerOptions));
    _current = configuration;
  }

  private Configuration LoadFromDisk() {
    var path = ResolveReadPath();
    if (path is null)
      return new Configuration();

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (!document.RootElement.TryGetProperty("Configuration", out var value))
      return new Configuration();

    var configuration = JsonSerializer.Deserialize<Configuration>(value.GetRawText(), SerializerOptions) ?? new Configuration();
    configuration.EnsureDefaults();
    return configuration;
  }

  private static JsonObject? ReadRoot(string? path) {
    if (path is null || !File.Exists(path))
      return null;

    return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
  }

  private void CopySeedIfNeeded() {
    if (File.Exists(FilePath) || _seedPath is null || !File.Exists(_seedPath) || !HasConfiguration(_seedPath))
      return;

    Save(_current);
  }

  private static bool HasConfiguration(string path) {
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.TryGetProperty("Configuration", out _);
  }

  private string? ResolveReadPath() {
    if (File.Exists(FilePath))
      return FilePath;
    if (_seedPath is not null && File.Exists(_seedPath))
      return _seedPath;
    return null;
  }
}
