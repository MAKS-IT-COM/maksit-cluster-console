namespace MaksIT.ClusterConsole.Shared;

public sealed record ColumnSpec(string Header, string Path);

public sealed record ResourceActions(
  bool CanDelete = true,
  bool CanApply = true,
  bool CanScale = false,
  bool CanRestart = false,
  bool CanLogs = false,
  bool CanExec = false,
  bool CanPortForward = false,
  bool CanCordon = false,
  bool CanDrain = false,
  bool CanTrigger = false);

public sealed record ResourceDescriptor(
  string Id,
  string Title,
  string Section,
  string Group,
  string Version,
  string Plural,
  string Kind,
  bool Namespaced,
  IReadOnlyList<ColumnSpec> Columns,
  ResourceActions Actions,
  IReadOnlyList<string> DetailTabs) {
  public Client.ResourceRef ToRef() =>
    new(Group, Version, Plural, Kind, Namespaced);
}
