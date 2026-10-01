using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.Services;

/// <summary>
/// Originator in the Memento pattern. It applies and captures its own slice of <see cref="LayoutSettings"/>.
/// The caretaker stores that memento and does not know the control.
/// </summary>
internal interface ILayoutOriginator {
  bool DeferSave { get; }

  void Attach(Action changed);

  void Apply(LayoutSettings layout);

  void Capture(LayoutSettings layout);
}
