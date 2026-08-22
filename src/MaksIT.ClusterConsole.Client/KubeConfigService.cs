using k8s;
using k8s.Exceptions;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

public sealed class KubeConfigService : IKubeConfigService {
  public string GetWritablePath(string? kubeConfigPath = null) =>
    ResolveWritablePath(kubeConfigPath);

  public Result<IReadOnlyList<KubeContextInfo>> ListContexts(string? kubeConfigPath = null) {
    try {
      var path = ResolvePath(kubeConfigPath);
      if (path is null)
        return Result<IReadOnlyList<KubeContextInfo>>.Ok([]);

      var config = KubernetesClientConfiguration.LoadKubeConfig(path);
      var items = (config.Contexts ?? [])
        .Select(c => new KubeContextInfo(
          c.Name,
          c.ContextDetails?.Cluster ?? string.Empty,
          c.ContextDetails?.User ?? string.Empty,
          c.ContextDetails?.Namespace))
        .ToList();

      return Result<IReadOnlyList<KubeContextInfo>>.Ok(items);
    }
    catch (Exception ex) {
      return Result<IReadOnlyList<KubeContextInfo>>.InternalServerError(null, ex.Message);
    }
  }

  public Result<IReadOnlyList<KubeContextDetails>> ListContextDetails(string? kubeConfigPath = null) {
    try {
      var path = ResolvePath(kubeConfigPath);
      if (path is null)
        return Result<IReadOnlyList<KubeContextDetails>>.Ok([]);

      var config = KubernetesClientConfiguration.LoadKubeConfig(path);
      var items = (config.Contexts ?? [])
        .Select(c => KubeConfigEditor.ToDetails(c, config))
        .ToList();
      return Result<IReadOnlyList<KubeContextDetails>>.Ok(items);
    }
    catch (Exception ex) {
      return Result<IReadOnlyList<KubeContextDetails>>.InternalServerError(null, ex.Message);
    }
  }

  public Result<string> GetCurrentContext(string? kubeConfigPath = null) {
    try {
      var path = ResolvePath(kubeConfigPath);
      if (path is null)
        return Result<string>.NotFound(null, "kubeconfig not found");

      var config = KubernetesClientConfiguration.LoadKubeConfig(path);
      var current = config.CurrentContext;
      if (string.IsNullOrWhiteSpace(current))
        return Result<string>.NotFound(null, "kubeconfig has no current-context");

      return Result<string>.Ok(current);
    }
    catch (Exception ex) {
      return Result<string>.InternalServerError(null, ex.Message);
    }
  }

  public Result UseContext(string contextName, string? kubeConfigPath = null) {
    if (string.IsNullOrWhiteSpace(contextName))
      return Result.BadRequest("ContextName is required.");

    try {
      var path = ResolvePath(kubeConfigPath);
      if (path is null)
        return Result.NotFound("kubeconfig not found");

      var config = KubernetesClientConfiguration.LoadKubeConfig(path);
      if (config.Contexts is null || config.Contexts.All(c => c.Name != contextName))
        return Result.NotFound("Context not found: " + contextName);

      if (string.Equals(config.CurrentContext, contextName, StringComparison.Ordinal))
        return Result.Ok("Already using context: " + contextName);

      // Only patch current-context — never rewrite or create sibling .bak files
      // (Lens and similar tools treat config.bak.* as extra kubeconfigs).
      if (!KubeConfigEditor.TrySetCurrentContext(path, contextName))
        return Result.InternalServerError("Could not update current-context in kubeconfig.");

      return Result.Ok("Switched to context: " + contextName);
    }
    catch (Exception ex) {
      return Result.InternalServerError(ex.Message);
    }
  }

  public Result UpsertConnection(KubeConnectionRequest request, string? kubeConfigPath = null) {
    var invalid = Validate(request);
    if (invalid is not null)
      return Result.BadRequest(invalid);

    try {
      var path = ResolveWritablePath(kubeConfigPath);
      var config = KubeConfigEditor.LoadOrCreate(path);
      var cluster = KubeConfigEditor.UpsertCluster(config, request);
      var user = KubeConfigEditor.UpsertUser(config, request);
      KubeConfigEditor.UpsertContext(config, request, cluster.Name, user.Name);
      KubeConfigEditor.PruneUnreferenced(config);
      KubeConfigEditor.Save(path, config);
      return Result.Ok(
        "Added/updated context: " + request.ContextName
        + " (cluster: " + cluster.Name + ", user: " + user.Name + ")");
    }
    catch (Exception ex) {
      return Result.InternalServerError(ex.Message);
    }
  }

  public Result DeleteContext(string contextName, bool cleanupUnused, string? kubeConfigPath = null) {
    if (string.IsNullOrWhiteSpace(contextName))
      return Result.BadRequest("ContextName is required.");

    try {
      var path = ResolvePath(kubeConfigPath);
      if (path is null)
        return Result.NotFound("kubeconfig not found");

      var config = KubernetesClientConfiguration.LoadKubeConfig(path);
      var error = KubeConfigEditor.DeleteContext(config, contextName, cleanupUnused);
      if (error is not null)
        return Result.NotFound(error);

      KubeConfigEditor.Save(path, config);
      return Result.Ok("Deleted context: " + contextName);
    }
    catch (Exception ex) {
      return Result.InternalServerError(ex.Message);
    }
  }

  public Result<KubernetesClientConfiguration> Build(string contextName, string? kubeConfigPath = null) {
    try {
      var path = ResolvePath(kubeConfigPath);
      if (path is null)
        return Result<KubernetesClientConfiguration>.NotFound(null, "kubeconfig not found");

      var cfg = KubernetesClientConfiguration.BuildConfigFromConfigFile(path, contextName);
      cfg.DisableHttp2 = true;
      return Result<KubernetesClientConfiguration>.Ok(cfg);
    }
    catch (KubeConfigException ex) {
      return Result<KubernetesClientConfiguration>.BadRequest(null, ex.Message);
    }
    catch (Exception ex) {
      return Result<KubernetesClientConfiguration>.InternalServerError(null, ex.Message);
    }
  }

  public static string? ResolvePath(string? kubeConfigPath) {
    if (!string.IsNullOrWhiteSpace(kubeConfigPath))
      return File.Exists(kubeConfigPath) ? kubeConfigPath : null;

    var fromEnv = Environment.GetEnvironmentVariable("KUBECONFIG");
    if (!string.IsNullOrWhiteSpace(fromEnv)) {
      var first = fromEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(File.Exists);
      if (first is not null)
        return first;
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var fallback = Path.Combine(home, ".kube", "config");
    return File.Exists(fallback) ? fallback : null;
  }

  public static string ResolveWritablePath(string? kubeConfigPath) {
    if (!string.IsNullOrWhiteSpace(kubeConfigPath))
      return kubeConfigPath;

    var fromEnv = Environment.GetEnvironmentVariable("KUBECONFIG");
    if (!string.IsNullOrWhiteSpace(fromEnv)) {
      var first = fromEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();
      if (!string.IsNullOrWhiteSpace(first))
        return first;
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".kube", "config");
  }

  private static string? Validate(KubeConnectionRequest request) {
    if (string.IsNullOrWhiteSpace(request.ContextName))
      return "ContextName is required.";
    if (string.IsNullOrWhiteSpace(request.Server))
      return "Server URL is required.";
    if (!string.IsNullOrWhiteSpace(request.CaFile) && !File.Exists(request.CaFile))
      return "CA file not found.";

    if (request.AuthKind == KubeAuthKind.Token) {
      if (string.IsNullOrWhiteSpace(request.Token))
        return "Token is required.";
      return null;
    }

    if (request.AuthKind == KubeAuthKind.Cert) {
      if (string.IsNullOrWhiteSpace(request.ClientCertFile) || string.IsNullOrWhiteSpace(request.ClientKeyFile))
        return "ClientCertFile and ClientKeyFile are required.";
      if (!File.Exists(request.ClientCertFile))
        return "Client certificate file not found.";
      if (!File.Exists(request.ClientKeyFile))
        return "Client key file not found.";
      return null;
    }

    if (request.AuthKind == KubeAuthKind.K3sData) {
      if (string.IsNullOrWhiteSpace(request.CaData)
          || string.IsNullOrWhiteSpace(request.ClientCertData)
          || string.IsNullOrWhiteSpace(request.ClientKeyData))
        return "certificate-authority-data, client-certificate-data and client-key-data are required.";
      return null;
    }

    if (request.AuthKind == KubeAuthKind.Basic) {
      if (string.IsNullOrWhiteSpace(request.BasicUser) || string.IsNullOrWhiteSpace(request.BasicPassword))
        return "Username and Password are required.";
      return null;
    }

    return "Unknown auth type.";
  }
}
