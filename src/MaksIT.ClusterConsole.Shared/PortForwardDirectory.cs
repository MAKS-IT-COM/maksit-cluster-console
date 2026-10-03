namespace MaksIT.ClusterConsole.Shared;

public sealed class PortForwardDirectory {
  public List<PersistedPortForward> Items { get; set; } = [];

  public void Ensure() {
    Items ??= [];
  }

  public IReadOnlyList<PersistedPortForward> For(string context) =>
    (Items ?? [])
      .Where(item => string.Equals(item.Context, context, StringComparison.Ordinal))
      .ToList();

  public void Upsert(PersistedPortForward forward) {
    ArgumentNullException.ThrowIfNull(forward);
    Ensure();
    Items.RemoveAll(item => Same(item, forward.Context, forward.LocalPort));
    Items.Add(forward);
  }

  public void Remove(string context, int localPort) {
    Items?.RemoveAll(item => Same(item, context, localPort));
  }

  private static bool Same(PersistedPortForward item, string context, int localPort) =>
    string.Equals(item.Context, context, StringComparison.Ordinal) && item.LocalPort == localPort;
}
