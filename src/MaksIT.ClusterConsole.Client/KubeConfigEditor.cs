using System.Text;
using k8s;
using k8s.KubeConfigModels;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;


namespace MaksIT.ClusterConsole.Client;

internal static class KubeConfigEditor {
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

    if (File.Exists(path)) {
      var backup = path + ".bak." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
      if (File.Exists(backup))
        backup += "-" + Guid.NewGuid().ToString("N")[..6];
      File.Copy(path, backup, overwrite: false);
    }

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
    File.WriteAllText(path, serializer.Serialize(config));
  }

  public static string EffectiveClusterName(KubeConnectionRequest request) =>
    string.IsNullOrWhiteSpace(request.ClusterName)
      ? request.ContextName + "-cluster"
      : request.ClusterName.Trim();

  public static string EffectiveUserName(KubeConnectionRequest request) =>
    string.IsNullOrWhiteSpace(request.UserName)
      ? request.ContextName + "-user"
      : request.UserName.Trim();

  public static Cluster UpsertCluster(K8SConfiguration config, KubeConnectionRequest request) {
    var name = EffectiveClusterName(request);
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
    var name = EffectiveUserName(request);
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
