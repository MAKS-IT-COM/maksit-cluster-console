using System.Text.Json;
using System.Globalization;
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

  public bool FileExisted { get; }

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

    FileExisted = File.Exists(FilePath);
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

    QuarantineIfCorrupt(FilePath);
    var root = ReadRoot(File.Exists(FilePath) ? FilePath : null) ?? [];
    root["Configuration"] = JsonSerializer.SerializeToNode(configuration, SerializerOptions);
    ReplaceFile(FilePath, root.ToJsonString(SerializerOptions));
    _current = configuration;
  }

  /// <summary>
  /// Moves an unreadable settings file aside so a truncated write cannot crash startup or a file watcher.
  /// </summary>
  public static bool QuarantineIfCorrupt(string path) {
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      return false;

    try {
      using var document = JsonDocument.Parse(File.ReadAllText(path));
      _ = document.RootElement;

      return false;
    }
    catch (JsonException) {
      var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
      var dest = path + ".corrupt-" + stamp;
      var n = 0;

      while (File.Exists(dest)) {
        n++;
        dest = path + ".corrupt-" + stamp + "-" + n.ToString(CultureInfo.InvariantCulture);
      }

      File.Move(path, dest);

      return true;
    }
  }

  private Configuration LoadFromDisk() {
    var path = ResolveReadPath();

    if (path is null)
      return new Configuration();

    string text;

    try {
      text = File.ReadAllText(path);
    }
    catch (IOException) {
      return new Configuration();
    }

    JsonDocument document;

    try {
      document = JsonDocument.Parse(text);
    }
    catch (JsonException) {
      if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase))
        QuarantineIfCorrupt(path);

      return new Configuration();
    }

    using (document) {
      if (!document.RootElement.TryGetProperty("Configuration", out var value))
        return new Configuration();

      var configuration = JsonSerializer.Deserialize<Configuration>(value.GetRawText(), SerializerOptions) ?? new Configuration();
      configuration.EnsureDefaults();

      return configuration;
    }
  }

  private static void ReplaceFile(string path, string text) {
    var temp = path + ".tmp";
    File.WriteAllText(temp, text);

    if (File.Exists(path))
      File.Replace(temp, path, null);
    else
      File.Move(temp, path);
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
