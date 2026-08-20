using System.Globalization;
using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.Shared;

public sealed record PortForwardRestoreSummary(int Restored, IReadOnlyList<string> Failures) {
  public int Total => Restored + Failures.Count;

  public string Format() {
    if (Failures.Count == 0) {
      if (Restored == 1)
        return "Restored 1 port-forward.";

      return $"Restored {Restored} port-forwards.";
    }

    var failed = string.Join("; ", Failures);
    if (Restored == 0)
      return $"Port-forward restore failed: {failed}";

    return $"Restored {Restored} port-forward(s); {Failures.Count} failed: {failed}";
  }
}

public static class PortForwardRow {
  public static string Uid(int localPort) =>
    $"pf:{localPort.ToString(CultureInfo.InvariantCulture)}";

  public static string StartedMessage(PortForwardHandle handle) =>
    $"Port-forward started: http://127.0.0.1:{handle.LocalPort} → {handle.Namespace}/{handle.PodName}:{handle.RequestedPort}.";

  public static string FailedMessage(IEnumerable<string> messages) =>
    $"Port-forward failed: {string.Join("; ", messages)}";

  public static string ReboundMessage(int previousLocalPort, PortForwardHandle handle) =>
    $"Port-forward rebound: localhost:{previousLocalPort} → http://127.0.0.1:{handle.LocalPort} → {handle.Namespace}/{handle.PodName}:{handle.RequestedPort}.";

  public static string LocalUrl(int localPort) =>
    $"http://127.0.0.1:{localPort.ToString(CultureInfo.InvariantCulture)}/";

  public static bool TryLocalUrl(ResourceRow row, out string url) {
    if (!TryLocalPort(row, out var localPort)) {
      url = "";
      return false;
    }

    url = LocalUrl(localPort);
    return true;
  }

  public static bool TryLocalPort(ResourceRow row, out int localPort) {
    localPort = 0;
    if (row.Document["localPort"] is JsonValue value && value.TryGetValue<int>(out localPort) && localPort > 0)
      return true;

    var uid = row.Uid;
    return uid.StartsWith("pf:", StringComparison.Ordinal)
      && int.TryParse(uid[3..], CultureInfo.InvariantCulture, out localPort)
      && localPort > 0;
  }

  public static ResourceRow From(PortForwardHandle handle, string uid, string status = "Active") =>
    From(
      uid,
      handle.PodName,
      handle.Namespace,
      handle.LocalPort,
      handle.RequestedPort,
      status);

  public static ResourceRow FromPersisted(PersistedPortForward saved, string status) =>
    From(
      Uid(saved.LocalPort),
      saved.PodName,
      saved.Namespace,
      saved.LocalPort,
      saved.RemotePort,
      status);

  private static ResourceRow From(
    string uid,
    string podName,
    string? @namespace,
    int localPort,
    int remotePort,
    string status) {
    var local = localPort.ToString(CultureInfo.InvariantCulture);
    var document = new JsonObject {
      ["kind"] = "PortForward",
      ["metadata"] = new JsonObject {
        ["name"] = $"localhost:{local}",
        ["namespace"] = @namespace,
        ["uid"] = uid
      },
      ["pod"] = podName,
      ["localPort"] = localPort,
      ["containerPort"] = remotePort,
      ["status"] = status
    };
    return ResourceRow.From(document, ResourceCatalog.PortForwardingDescriptor);
  }
}
