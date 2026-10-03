using MaksIT.Results;
using MaksIT.ClusterConsole.Client.Terminal;


namespace MaksIT.ClusterConsole.Client.Cluster;

public sealed partial class ClusterSession {
  public Task<Result<PodTerminalConnection>> OpenTerminalAsync(
    string podName,
    string @namespace,
    string? container,
    string? shell,
    CancellationToken cancellationToken = default) =>
    PodExec.OpenShellAsync(_client, podName, @namespace, container, shell, cancellationToken);
}
