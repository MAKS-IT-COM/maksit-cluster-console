namespace MaksIT.ClusterConsole.Shared;

public static class VolumeFilesCommands {
  public const string ListScript =
    """
    cd "$1" || exit 1
    ls -1Ap 2>/dev/null || ls -1A 2>/dev/null || ls -1a
    """;

  public static IReadOnlyList<string> List(string directory) =>
    ["sh", "-c", ListScript, "sh", directory];

  public static IReadOnlyList<string> Read(string path) =>
    ["sh", "-c", "cat -- \"$1\"", "sh", path];

  public static IReadOnlyList<string> Write(string path) =>
    ["sh", "-c", "cat > \"$1\"", "sh", path];

  public static IReadOnlyList<string> Identity() =>
    ["sh", "-c", "id -un 2>/dev/null; id -u; id -g"];
}
