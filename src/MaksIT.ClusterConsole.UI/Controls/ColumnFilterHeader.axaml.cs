using Avalonia.Controls;
using Avalonia.Input;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI.Controls;

public partial class ColumnFilterHeader : UserControl {
  public ColumnFilterHeader() {
    InitializeComponent();
  }

  private void OnFilterPointerPressed(object? sender, PointerPressedEventArgs e) {
    if (sender is Button { Flyout: Flyout flyout } button) {
      if (flyout.Content is Control content)
        content.DataContext = DataContext;
      flyout.ShowAt(button);
    }

    e.Handled = true;
  }

  private void OnValuePointerPressed(object? sender, PointerPressedEventArgs e) {
    if (sender is not Control { DataContext: ColumnFilterValueViewModel item })
      return;
    if (DataContext is not ColumnFilterViewModel filter)
      return;
    if (!e.GetCurrentPoint((Control)sender).Properties.IsLeftButtonPressed)
      return;

    if (e.ClickCount >= 2)
      filter.SelectOnly(item.Value);
    else
      item.IsIncluded = !item.IsIncluded;

    e.Handled = true;
  }
}
