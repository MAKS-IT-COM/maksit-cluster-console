namespace MaksIT.ClusterConsole.Client;

public sealed record ResourceRef(
  string Group,
  string Version,
  string Plural,
  string Kind,
  bool Namespaced);
