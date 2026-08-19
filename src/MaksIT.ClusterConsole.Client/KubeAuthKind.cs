using System.Diagnostics.CodeAnalysis;
using MaksIT.Core.Abstractions;


namespace MaksIT.ClusterConsole.Client;

public sealed class KubeAuthKind : Enumeration {
  public static readonly KubeAuthKind Token = new(1, "token");
  public static readonly KubeAuthKind Cert = new(2, "cert");
  public static readonly KubeAuthKind K3sData = new(3, "k3sdata");
  public static readonly KubeAuthKind Basic = new(4, "basic");

  private KubeAuthKind(int id, string name) : base(id, name) { }

  public static bool TryParse(string? value, [NotNullWhen(true)] out KubeAuthKind? kind) {
    kind = GetAll<KubeAuthKind>().FirstOrDefault(item =>
      string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
    return kind is not null;
  }
}
