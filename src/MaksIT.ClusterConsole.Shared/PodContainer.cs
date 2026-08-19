namespace MaksIT.ClusterConsole.Shared;

public sealed record PodContainer(
  string Name,
  string Image,
  string Kind,
  bool Ready,
  int Restarts,
  string State) {
  public string ReadyLabel => Ready ? "Ready" : "Not ready";

  public string StatusLine {
    get {
      var state = string.IsNullOrEmpty(State) ? ReadyLabel : State;
      return Restarts > 0 ? $"{state} · {Restarts} restarts" : state;
    }
  }

  public string ImageLabel =>
    string.IsNullOrWhiteSpace(Image) ? Kind : Image;

  public string Display =>
    string.IsNullOrWhiteSpace(Image) ? $"{Name}  ({Kind})" : $"{Name}  ·  {Image}";
}
