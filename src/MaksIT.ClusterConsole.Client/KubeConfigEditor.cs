using System.Text;
using System.Text.RegularExpressions;
using k8s;
using k8s.KubeConfigModels;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;


namespace MaksIT.ClusterConsole.Client;

internal static class KubeConfigEditor {
  private static readonly Regex CurrentContextLine = new(
    @"^[ \t]*current-context:[ \t]*.*$",
    RegexOptions.Multiline | RegexOptions.CultureInvariant);

  public static K8SConfiguration LoadOrCreate(string path) {
    if (!File.Exists(path)) {
      return new K8SConfiguration {
        ApiVersion = "v1",
        Kind = "Config",
        Clusters = [],
        Contexts = [],
        Users = []
      };
    }

    return KubernetesClientConfiguration.LoadKubeConfig(path);
  }

  public static void Save(string path, K8SConfiguration config) {
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    if (string.IsNullOrWhiteSpace(config.ApiVersion))
      config.ApiVersion = "v1";
    if (string.IsNullOrWhiteSpace(config.Kind))
      config.Kind = "Config";
    Sanitize(config);

    var serializer = new SerializerBuilder()
      .WithNamingConvention(NullNamingConvention.Instance)
      .ConfigureDefaultValuesHandling(
        DefaultValuesHandling.OmitNull
        | DefaultValuesHandling.OmitDefaults
        | DefaultValuesHandling.OmitEmptyCollections)
      .Build();

    // Write in place — never create config.bak.* beside kubeconfig. Tools like Lens
    // treat those sibling files as extra kubeconfigs / duplicate connections.
    var temp = path + ".tmp." + Guid.NewGuid().ToString("N")[..8];
    File.WriteAllText(temp, serializer.Serialize(config));
    File.Move(temp, path, overwrite: true);
  }

  /// <summary>
  /// Updates only the <c>current-context</c> line without rewriting clusters/users or creating a backup.
  /// </summary>
  public static bool TrySetCurrentContext(string path, string contextName) {
    if (!File.Exists(path) || string.IsNullOrWhiteSpace(contextName))
      return false;

    var text = File.ReadAllText(path);
    var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var line = "current-context: " + contextName.Trim();
    string updated;
    var match = CurrentContextLine.Match(text);
    if (match.Success) {
      var replacement = match.Value.EndsWith('\r') ? line + "\r" : line;
      updated = CurrentContextLine.Replace(text, replacement, 1);
    }
    else {
      var insertAt = text.IndexOf("contexts:", StringComparison.Ordinal);
      if (insertAt < 0)
        insertAt = text.IndexOf("clusters:", StringComparison.Ordinal);
      if (insertAt < 0)
        return false;
      updated = text.Insert(insertAt, line + newline);
    }

    if (string.Equals(updated, text, StringComparison.Ordinal))
      return true;

    var temp = path + ".tmp." + Guid.NewGuid().ToString("N")[..8];
    File.WriteAllText(temp, updated);
    File.Move(temp, path, overwrite: true);
    return true;
  }

  public static string EffectiveClusterName(KubeConnectionRequest request, K8SConfiguration? config = null) {
    if (!string.IsNullOrWhiteSpace(request.ClusterName))
      return request.ClusterName.Trim();

    var existing = config?.Contexts?.FirstOrDefault(c => c.Name == request.ContextName);
    if (existing?.ContextDetails?.Cluster is { Length: > 0 } cluster)
      return cluster;

    return request.ContextName.Trim();
  }

  public static string EffectiveUserName(KubeConnectionRequest request, K8SConfiguration? config = null) {
    if (!string.IsNullOrWhiteSpace(request.UserName))
      return request.UserName.Trim();

    var existing = config?.Contexts?.FirstOrDefault(c => c.Name == request.ContextName);
    if (existing?.ContextDetails?.User is { Length: > 0 } user)
      return user;

    return request.ContextName.Trim();
  }

  public static Cluster UpsertCluster(K8SConfiguration config, KubeConnectionRequest request) {
    var name = EffectiveClusterName(request, config);
    var clusters = config.Clusters?.ToList() ?? [];
    var cluster = clusters.FirstOrDefault(c => c.Name == name);
    if (cluster is null) {
      cluster = new Cluster { Name = name, ClusterEndpoint = new ClusterEndpoint() };
      clusters.Add(cluster);
    }

    cluster.ClusterEndpoint ??= new ClusterEndpoint();
    cluster.ClusterEndpoint.Server = request.Server.Trim();
    cluster.ClusterEndpoint.SkipTlsVerify = request.InsecureSkipTlsVerify;
    ApplyCertificateAuthority(cluster.ClusterEndpoint, request);
    config.Clusters = clusters;
    return cluster;
  }

  public static User UpsertUser(K8SConfiguration config, KubeConnectionRequest request) {
    var name = EffectiveUserName(request, config);
    var users = config.Users?.ToList() ?? [];
    var user = users.FirstOrDefault(u => u.Name == name);
    if (user is null) {
      user = new User { Name = name, UserCredentials = new UserCredentials() };
      users.Add(user);
    }

    user.UserCredentials ??= new UserCredentials();
    ApplyCredentials(user.UserCredentials, request);
    config.Users = users;
    return user;
  }

  public static void PruneUnreferenced(K8SConfiguration config) {
    var contexts = config.Contexts ?? [];
    var usedClusters = contexts
      .Select(c => c.ContextDetails?.Cluster)
      .Where(n => !string.IsNullOrEmpty(n))
      .ToHashSet(StringComparer.Ordinal);
    var usedUsers = contexts
      .Select(c => c.ContextDetails?.User)
      .Where(n => !string.IsNullOrEmpty(n))
      .ToHashSet(StringComparer.Ordinal);

    if (config.Clusters is not null)
      config.Clusters = config.Clusters.Where(c => usedClusters.Contains(c.Name)).ToList();
    if (config.Users is not null)
      config.Users = config.Users.Where(u => usedUsers.Contains(u.Name)).ToList();
  }

  public static Context UpsertContext(
    K8SConfiguration config,
    KubeConnectionRequest request,
    string clusterName,
    string userName) {
    var contexts = config.Contexts?.ToList() ?? [];
    var context = contexts.FirstOrDefault(c => c.Name == request.ContextName);
    if (context is null) {
      context = new Context { Name = request.ContextName, ContextDetails = new ContextDetails() };
      contexts.Add(context);
    }

    context.ContextDetails ??= new ContextDetails();
    context.ContextDetails.Cluster = clusterName;
    context.ContextDetails.User = userName;
    context.ContextDetails.Namespace = string.IsNullOrWhiteSpace(request.Namespace)
      ? null
      : request.Namespace.Trim();
    config.Contexts = contexts;
    if (request.UseAfterAdd)
      config.CurrentContext = request.ContextName;
    return context;
  }

  public static string? DeleteContext(K8SConfiguration config, string name, bool cleanupUnused) {
    var contexts = config.Contexts?.ToList() ?? [];
    var context = contexts.FirstOrDefault(c => c.Name == name);
    if (context is null)
      return "context not found";

    var clusterName = context.ContextDetails?.Cluster;
    var userName = context.ContextDetails?.User;
    contexts.Remove(context);
    config.Contexts = contexts;
    if (string.Equals(config.CurrentContext, name, StringComparison.Ordinal))
      config.CurrentContext = contexts.FirstOrDefault()?.Name;

    if (!cleanupUnused)
      return null;

    if (!string.IsNullOrEmpty(clusterName)
        && contexts.All(c => c.ContextDetails?.Cluster != clusterName)) {
      var clusters = config.Clusters?.ToList() ?? [];
      clusters.RemoveAll(c => c.Name == clusterName);
      config.Clusters = clusters;
    }

    if (!string.IsNullOrEmpty(userName)
        && contexts.All(c => c.ContextDetails?.User != userName)) {
      var users = config.Users?.ToList() ?? [];
      users.RemoveAll(u => u.Name == userName);
      config.Users = users;
    }

    return null;
  }

  public static KubeContextDetails ToDetails(Context context, K8SConfiguration config) {
    var cluster = config.Clusters?.FirstOrDefault(c => c.Name == context.ContextDetails?.Cluster);
    var user = config.Users?.FirstOrDefault(u => u.Name == context.ContextDetails?.User);
    var endpoint = cluster?.ClusterEndpoint;
    var creds = user?.UserCredentials;
    return new KubeContextDetails(
      context.Name,
      context.ContextDetails?.Cluster ?? "",
      context.ContextDetails?.User ?? "",
      context.ContextDetails?.Namespace,
      endpoint?.Server ?? "",
      endpoint?.SkipTlsVerify == true,
      CaSummary(endpoint),
      AuthSummary(creds),
      string.Equals(config.CurrentContext, context.Name, StringComparison.Ordinal));
  }

  private static void ApplyCertificateAuthority(ClusterEndpoint endpoint, KubeConnectionRequest request) {
    if (!string.IsNullOrWhiteSpace(request.CaData)) {
      endpoint.CertificateAuthorityData = NormalizeData(request.CaData);
      endpoint.CertificateAuthority = null;
      return;
    }

    if (string.IsNullOrWhiteSpace(request.CaFile))
      return;

    if (request.EmbedClusterCa) {
      endpoint.CertificateAuthorityData = Convert.ToBase64String(File.ReadAllBytes(request.CaFile));
      endpoint.CertificateAuthority = null;
      return;
    }

    endpoint.CertificateAuthority = request.CaFile;
    endpoint.CertificateAuthorityData = null;
  }

  private static void ApplyCredentials(UserCredentials creds, KubeConnectionRequest request) {
    if (request.AuthKind == KubeAuthKind.Cert) {
      ApplyClientCert(creds, request);
      return;
    }

    if (request.AuthKind == KubeAuthKind.K3sData) {
      creds.ClientCertificateData = NormalizeData(request.ClientCertData ?? "");
      creds.ClientKeyData = NormalizeData(request.ClientKeyData ?? "");
      creds.ClientCertificate = null;
      creds.ClientKey = null;
      creds.Token = null;
      return;
    }

    if (request.AuthKind == KubeAuthKind.Basic) {
      creds.UserName = request.BasicUser;
      creds.Password = request.BasicPassword;
      creds.Token = null;
      return;
    }

    creds.Token = request.Token;
  }

  private static void ApplyClientCert(UserCredentials creds, KubeConnectionRequest request) {
    if (request.EmbedClientCerts) {
      if (!string.IsNullOrWhiteSpace(request.ClientCertFile))
        creds.ClientCertificateData = Convert.ToBase64String(File.ReadAllBytes(request.ClientCertFile));
      if (!string.IsNullOrWhiteSpace(request.ClientKeyFile))
        creds.ClientKeyData = Convert.ToBase64String(File.ReadAllBytes(request.ClientKeyFile));
      creds.ClientCertificate = null;
      creds.ClientKey = null;
      return;
    }

    creds.ClientCertificate = request.ClientCertFile;
    creds.ClientKey = request.ClientKeyFile;
    creds.ClientCertificateData = null;
    creds.ClientKeyData = null;
  }

  private static string NormalizeData(string raw) {
    var text = raw.Trim().Replace("\r", "");
    if (text.Contains("BEGIN", StringComparison.Ordinal))
      return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    return text.Replace("\n", "");
  }

  private static void Sanitize(K8SConfiguration config) {
    foreach (var user in config.Users ?? []) {
      if (user.UserCredentials is not { } creds)
        continue;
      if (creds.ImpersonateGroups is not null && !creds.ImpersonateGroups.Any())
        creds.ImpersonateGroups = null!;
      if (creds.ImpersonateUserExtra is { Count: 0 })
        creds.ImpersonateUserExtra = null!;
    }
  }

  private static string CaSummary(ClusterEndpoint? endpoint) {
    if (endpoint is null)
      return "CA none";
    if (!string.IsNullOrWhiteSpace(endpoint.CertificateAuthorityData))
      return "CA data: present";
    if (!string.IsNullOrWhiteSpace(endpoint.CertificateAuthority))
      return "CA file: " + endpoint.CertificateAuthority;
    if (endpoint.SkipTlsVerify)
      return "CA none (insecure)";
    return "CA none";
  }

  private static string AuthSummary(UserCredentials? creds) {
    if (creds is null)
      return "Auth: unknown";
    if (!string.IsNullOrWhiteSpace(creds.Token))
      return "Auth: token present";
    if (!string.IsNullOrWhiteSpace(creds.ClientCertificateData)
        || !string.IsNullOrWhiteSpace(creds.ClientCertificate)) {
      if (string.IsNullOrWhiteSpace(creds.ClientKeyData)
          && string.IsNullOrWhiteSpace(creds.ClientKey))
        return "Auth: client certificate (missing key)";
      return "Auth: client certificate + key";
    }
    if (!string.IsNullOrWhiteSpace(creds.UserName))
      return "Auth: basic (username: " + creds.UserName + ")";
    if (creds.ExternalExecution is not null)
      return "Auth: exec plugin";
    if (creds.AuthProvider is not null)
      return "Auth: provider";
    return "Auth: unknown";
  }
}
