using k8s;
using MaksIT.Results;


namespace MaksIT.ClusterConsole.Client;

public sealed record KubeContextInfo(
  string Name,
  string Cluster,
  string User,
  string? Namespace);

public interface IKubeConfigService {
  string GetWritablePath(string? kubeConfigPath = null);

  Result<IReadOnlyList<KubeContextInfo>> ListContexts(string? kubeConfigPath = null);

  Result<IReadOnlyList<KubeContextDetails>> ListContextDetails(string? kubeConfigPath = null);

  Result<string> GetCurrentContext(string? kubeConfigPath = null);

  Result UseContext(string contextName, string? kubeConfigPath = null);

  Result UpsertConnection(KubeConnectionRequest request, string? kubeConfigPath = null);

  Result DeleteContext(string contextName, bool cleanupUnused, string? kubeConfigPath = null);

  Result<KubernetesClientConfiguration> Build(string contextName, string? kubeConfigPath = null);
}
