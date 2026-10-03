using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Threading;


namespace MaksIT.ClusterConsole.UI.Controls.Details;

public static class DetailPane {
  public static readonly AttachedProperty<string?> SelectedTabProperty =
    AvaloniaProperty.RegisterAttached<TabControl, string?>(
      "SelectedTab",
      typeof(DetailPane),
      defaultBindingMode: BindingMode.TwoWay);

  public static readonly AttachedProperty<bool> FocusWhenSelectedProperty =
    AvaloniaProperty.RegisterAttached<Control, bool>("FocusWhenSelected", typeof(DetailPane));

  public static readonly AttachedProperty<bool> FollowTailProperty =
    AvaloniaProperty.RegisterAttached<TextBox, bool>("FollowTail", typeof(DetailPane));

  private static readonly AttachedProperty<bool> WatchingProperty =
    AvaloniaProperty.RegisterAttached<Control, bool>("Watching", typeof(DetailPane));

  private static readonly AttachedProperty<bool> SyncingProperty =
    AvaloniaProperty.RegisterAttached<TabControl, bool>("Syncing", typeof(DetailPane));

  static DetailPane() {
    SelectedTabProperty.Changed.AddClassHandler<TabControl>(OnSelectedTabChanged);
    FocusWhenSelectedProperty.Changed.AddClassHandler<Control>(OnFocusWhenSelectedChanged);
    FollowTailProperty.Changed.AddClassHandler<TextBox>(OnFollowTailChanged);
  }

  public static string? GetSelectedTab(TabControl tabs) =>
    tabs.GetValue(SelectedTabProperty);

  public static void SetSelectedTab(TabControl tabs, string? value) =>
    tabs.SetValue(SelectedTabProperty, value);

  public static bool GetFocusWhenSelected(Control control) =>
    control.GetValue(FocusWhenSelectedProperty);

  public static void SetFocusWhenSelected(Control control, bool value) =>
    control.SetValue(FocusWhenSelectedProperty, value);

  public static bool GetFollowTail(TextBox box) =>
    box.GetValue(FollowTailProperty);

  public static void SetFollowTail(TextBox box, bool value) =>
    box.SetValue(FollowTailProperty, value);

  private static void OnSelectedTabChanged(TabControl tabs, AvaloniaPropertyChangedEventArgs e) {
    Watch(tabs);
    Apply(tabs, e.GetNewValue<string?>());
  }

  private static void OnFocusWhenSelectedChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
    if (!e.GetNewValue<bool>())
      return;

    control.AttachedToLogicalTree += (_, _) => WatchFocus(control);
    WatchFocus(control);
  }

  private static void OnFollowTailChanged(TextBox box, AvaloniaPropertyChangedEventArgs _) {
    if (box.GetValue(WatchingProperty))
      return;

    box.SetValue(WatchingProperty, true);
    box.PropertyChanged += (_, change) => {
      if (change.Property != TextBox.TextProperty || !box.GetValue(FollowTailProperty))
        return;

      box.CaretIndex = box.Text?.Length ?? 0;
    };
  }

  private static void Watch(TabControl tabs) {
    if (tabs.GetValue(WatchingProperty))
      return;

    tabs.SetValue(WatchingProperty, true);
    tabs.SelectionChanged += (_, e) => {
      if (tabs.GetValue(SyncingProperty) || !ReferenceEquals(e.Source, tabs))
        return;

      if (tabs.SelectedItem is not TabItem { Header: string header, IsVisible: true })
        return;

      if (tabs.GetValue(SelectedTabProperty) == header)
        Focus(tabs.SelectedItem as TabItem);
      else
        tabs.SetValue(SelectedTabProperty, header);
    };
    WatchItems(tabs);
  }

  private static void WatchItems(TabControl tabs) {
    foreach (var item in tabs.Items) {
      if (item is not TabItem tab || tab.GetValue(WatchingProperty))
        continue;

      tab.SetValue(WatchingProperty, true);
      tab.PropertyChanged += (_, e) => {
        if (e.Property == Visual.IsVisibleProperty)
          EnsureVisibleSelection(tabs);
      };
    }
  }

  private static void Apply(TabControl tabs, string? header) {
    if (tabs.GetValue(SyncingProperty) || string.IsNullOrEmpty(header))
      return;

    foreach (var item in tabs.Items) {
      if (item is not TabItem { Header: string title, IsVisible: true } tab)
        continue;

      if (!string.Equals(title, header, StringComparison.Ordinal))
        continue;

      if (!ReferenceEquals(tabs.SelectedItem, tab)) {
        tabs.SetValue(SyncingProperty, true);
        tabs.SelectedItem = tab;
        tabs.SetValue(SyncingProperty, false);
      }

      Focus(tab);

      return;
    }
  }

  private static void EnsureVisibleSelection(TabControl tabs) {
    if (tabs.SelectedItem is TabItem { IsVisible: true })
      return;

    foreach (var item in tabs.Items) {
      if (item is not TabItem { Header: string header, IsVisible: true })
        continue;

      tabs.SetValue(SelectedTabProperty, header);

      return;
    }
  }

  private static void WatchFocus(Control control) {
    if (control.GetValue(WatchingProperty))
      return;

    if (TabOf(control) is not TabItem tab)
      return;

    control.SetValue(WatchingProperty, true);
    tab.PropertyChanged += (_, e) => {
      if (e.Property == TabItem.IsSelectedProperty)
        TryFocus(control);
    };
    TryFocus(control);
  }

  private static void Focus(TabItem? tab) {
    if (tab is null)
      return;

    foreach (var child in tab.GetLogicalDescendants().OfType<Control>()) {
      if (child.GetValue(FocusWhenSelectedProperty))
        TryFocus(child);
    }
  }

  private static void TryFocus(Control control) {
    if (!control.GetValue(FocusWhenSelectedProperty))
      return;

    if (TabOf(control) is not { IsSelected: true })
      return;

    Dispatcher.UIThread.Post(() => control.Focus());
  }

  private static TabItem? TabOf(StyledElement control) {
    for (StyledElement? node = control; node is not null; node = node.Parent) {
      if (node is TabItem tab)
        return tab;
    }

    return null;
  }
}
