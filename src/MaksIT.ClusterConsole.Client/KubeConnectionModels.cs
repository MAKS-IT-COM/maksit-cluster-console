namespace MaksIT.ClusterConsole.Client;

public sealed record KubeContextDetails(
  string Name,
  string Cluster,
  string User,
  string? Namespace,
  string Server,
  bool SkipTlsVerify,
  string CaSummary,
  string AuthSummary,
  bool IsCurrent);

public sealed class KubeConnectionRequest {
  public required string ContextName { get; init; }

  public string? ClusterName { get; init; }

  public string? UserName { get; init; }

  public string? Namespace { get; init; }

  public required string Server { get; init; }

  public string? CaFile { get; init; }

  public string? CaData { get; init; }

  public bool EmbedClusterCa { get; init; }

  public bool InsecureSkipTlsVerify { get; init; }

  public KubeAuthKind AuthKind { get; init; } = KubeAuthKind.Token;

  public string? Token { get; init; }

  public string? ClientCertFile { get; init; }

  public string? ClientKeyFile { get; init; }

  public string? ClientCertData { get; init; }

  public string? ClientKeyData { get; init; }

  public bool EmbedClientCerts { get; init; }

  public string? BasicUser { get; init; }

  public string? BasicPassword { get; init; }

  public bool UseAfterAdd { get; init; } = true;
}
