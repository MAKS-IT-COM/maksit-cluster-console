using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Client;

public static class PodShell {
  public static string[] BuildCommand(string? shell) {
    var parts = Split(shell);
    var command = new string[parts.Length + 3];
    command[0] = "/bin/sh";
    command[1] = "-c";
    command[2] = "export TERM=xterm-256color; exec \"$0\" \"$@\"";
    parts.CopyTo(command, 3);
    return command;
  }

  public static byte[] ResizePayload(int columns, int rows) {
    columns = Math.Clamp(columns, 1, 1000);
    rows = Math.Clamp(rows, 1, 1000);
    return Encoding.UTF8.GetBytes(FormattableString.Invariant($"{{\"Width\":{columns},\"Height\":{rows}}}\n"));
  }

  public static ShellStatus ReadStatus(string? status) {
    if (string.IsNullOrWhiteSpace(status))
      return ShellStatus.Unknown;

    try {
      var node = JsonNode.Parse(status);
      if (node is null)
        return ShellStatus.Unknown;

      var message = node["message"]?.GetValue<string>();
      if (string.Equals(node["status"]?.GetValue<string>(), "Success", StringComparison.OrdinalIgnoreCase))
        return new ShellStatus(0, message);

      if (node["details"]?["causes"] is JsonArray causes) {
        foreach (var cause in causes) {
          if (!string.Equals(cause?["reason"]?.GetValue<string>(), "ExitCode", StringComparison.Ordinal))
            continue;
          if (int.TryParse(cause?["message"]?.GetValue<string>(), out var code))
            return new ShellStatus(code, message);
        }
      }

      return new ShellStatus(-1, message);
    }
    catch (JsonException) {
      return ShellStatus.Unknown;
    }
  }

  private static string[] Split(string? shell) {
    if (string.IsNullOrWhiteSpace(shell))
      return ["/bin/sh"];

    var parts = shell.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return parts.Length == 0 ? ["/bin/sh"] : parts;
  }
}

public readonly record struct ShellStatus(int Code, string? Message) {
  public static ShellStatus Unknown { get; } = new(-1, null);
}
