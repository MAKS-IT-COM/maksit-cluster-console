using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Services;
using MaksIT.ClusterConsole.UI.Converters;
using MaksIT.ClusterConsole.UI.ViewModels.Cluster;


namespace MaksIT.ClusterConsole.UI.Controls.Grid;

public static class ResourceTable {
  public static readonly AttachedProperty<ClusterPageViewModel?> PageProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, ClusterPageViewModel?>("Page", typeof(ResourceTable));

  public static readonly AttachedProperty<ResourceDescriptor?> DescriptorProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, ResourceDescriptor?>("Descriptor", typeof(ResourceTable));

  private static readonly AttachedProperty<bool> WiredProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, bool>("Wired", typeof(ResourceTable));

  private static readonly AttachedProperty<PageHook?> HookProperty =
    AvaloniaProperty.RegisterAttached<DataGrid, PageHook?>("Hook", typeof(ResourceTable));

  private static readonly string[] FallbackHeaders = ["Name", "Namespace", "Age"];

  static ResourceTable() {
    PageProperty.Changed.AddClassHandler<DataGrid>(OnPageChanged);
    DescriptorProperty.Changed.AddClassHandler<DataGrid>(OnDescriptorChanged);
  }

  public static ClusterPageViewModel? GetPage(DataGrid grid) =>
    grid.GetValue(PageProperty);

  public static void SetPage(DataGrid grid, ClusterPageViewModel? value) =>
    grid.SetValue(PageProperty, value);

  public static ResourceDescriptor? GetDescriptor(DataGrid grid) =>
    grid.GetValue(DescriptorProperty);

  public static void SetDescriptor(DataGrid grid, ResourceDescriptor? value) =>
    grid.SetValue(DescriptorProperty, value);

  private static void OnPageChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e) {
    Wire(grid);
    var hook = grid.GetValue(HookProperty) ?? new PageHook();

    if (hook.Page is not null && hook.Handler is not null)
      hook.Page.SelectedRowsRestored -= hook.Handler;

    hook.Page = e.GetNewValue<ClusterPageViewModel?>();
    hook.Handler = null;

    if (hook.Page is { } page) {
      hook.Handler = rows => ApplySelection(grid, rows, page.SelectedRow);
      page.SelectedRowsRestored += hook.Handler;
    }

    grid.SetValue(HookProperty, hook);
    Rebuild(grid);
  }

  private static void OnDescriptorChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e) {
    Wire(grid);
    Rebuild(grid);
  }

  private static void Wire(DataGrid grid) {
    if (grid.GetValue(WiredProperty))
      return;

    grid.SetValue(WiredProperty, true);
    grid.SelectionChanged += OnSelectionChanged;
    grid.DoubleTapped += OnDoubleTapped;
    grid.CopyingRowClipboardContent += OnCopying;
  }

  private static void Rebuild(DataGrid grid) {
    using (LayoutMemento.SuspendSave(grid)) {
      var page = grid.GetValue(PageProperty);
      page?.ReloadColumnFilters();
      grid.Columns.Clear();
      var headers = grid.GetValue(DescriptorProperty)?.Columns.Select(column => column.Header).ToList()
        ?? FallbackHeaders.ToList();

      foreach (var header in headers)
        grid.Columns.Add(CreateColumn(header, page));

      LayoutMemento.RestoreTables(grid);
    }
  }

  private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) {
    if (sender is not DataGrid grid)
      return;

    if (grid.GetValue(PageProperty) is not { } page || page.SyncingSelection)
      return;

    page.ReplaceSelectedRows(grid.SelectedItems.OfType<ResourceRow>());
  }

  private static void OnDoubleTapped(object? sender, TappedEventArgs e) {
    if (e.Source is not Control { DataContext: ResourceRow })
      return;

    if (sender is not DataGrid grid || grid.GetValue(PageProperty) is not { } page)
      return;

    if (page.IsPortForwardingView) {
      page.OpenSelectedPortForwardCommand.Execute(null);

      return;
    }

    if (page.BrowseFilesCommand.CanExecute(null))
      page.BrowseFilesCommand.Execute(null);
  }

  private static void OnCopying(object? sender, DataGridRowClipboardEventArgs e) {
    if (!e.IsColumnHeadersRow)
      return;

    for (var i = 0; i < e.ClipboardRowContent.Count; i++) {
      var cell = e.ClipboardRowContent[i];
      e.ClipboardRowContent[i] = new DataGridClipboardCellContent(
        cell.Item,
        cell.Column,
        ColumnHeaderText(cell.Column));
    }
  }

  private static void ApplySelection(DataGrid grid, IReadOnlyList<ResourceRow> rows, ResourceRow? current) {
    var wanted = rows.ToHashSet();

    for (var i = grid.SelectedItems.Count - 1; i >= 0; i--) {
      if (grid.SelectedItems[i] is not ResourceRow row || !wanted.Contains(row))
        grid.SelectedItems.RemoveAt(i);
    }

    foreach (var row in rows) {
      if (!grid.SelectedItems.Contains(row))
        grid.SelectedItems.Add(row);
    }

    if (current is not null && !ReferenceEquals(grid.SelectedItem, current))
      grid.SelectedItem = current;
  }

  private static DataGridColumn CreateColumn(string header, ClusterPageViewModel? page) {
    var comparer = new ResourceRowComparer(header);
    object columnHeader = page is null
      ? header
      : new ColumnFilterHeader { DataContext = page.FilterFor(header) };

    if (header == "Status") {
      return new DataGridTemplateColumn {
        Header = columnHeader,
        Tag = header,
        CanUserSort = true,
        CustomSortComparer = comparer,
        CellTemplate = StatusCellTemplate(),
        ClipboardContentBinding = new Binding(nameof(ResourceRow.Status)) { Mode = BindingMode.OneWay },
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = 72
      };
    }

    return new DataGridTemplateColumn {
      Header = columnHeader,
      Tag = header,
      CanUserSort = true,
      CustomSortComparer = comparer,
      CellTemplate = TextCellTemplate(header),
      ClipboardContentBinding = CellsBinding(header),
      Width = new DataGridLength(1, DataGridLengthUnitType.Star),
      MinWidth = 72
    };
  }

  private static string ColumnHeaderText(DataGridColumn column) {
    if (column.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
      return tag;

    if (column.Header is string header)
      return header;

    if (column.Header is Control { DataContext: ColumnFilterViewModel filter })
      return filter.Header;

    return column.Header?.ToString() ?? "";
  }

  private static FuncDataTemplate<ResourceRow> TextCellTemplate(string header) =>
    new((_, _) => {
      var text = new TextBlock {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0)
      };
      text.Bind(TextBlock.TextProperty, CellsBinding(header));
      text.Bind(ToolTip.TipProperty, new Binding(nameof(ResourceRow.CellTips)) {
        Mode = BindingMode.OneWay,
        Converter = new DictionaryKeyConverter(header)
      });

      return text;
    }, true);

  private static FuncDataTemplate<ResourceRow> StatusCellTemplate() =>
    new((_, _) => {
      var text = new TextBlock {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0),
        FontWeight = FontWeight.SemiBold
      };
      text.Bind(TextBlock.TextProperty, new Binding(nameof(ResourceRow.Status)));
      text.Bind(TextBlock.ForegroundProperty, new Binding(nameof(ResourceRow.Status)) {
        Converter = StatusBrushConverter.Instance
      });

      return text;
    }, true);

  private static Binding CellsBinding(string header) =>
    new(nameof(ResourceRow.Cells)) {
      Mode = BindingMode.OneWay,
      Converter = new DictionaryKeyConverter(header)
    };

  private sealed class PageHook {
    public ClusterPageViewModel? Page { get; set; }

    public Action<IReadOnlyList<ResourceRow>>? Handler { get; set; }
  }

  private sealed class DictionaryKeyConverter(string key) : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
      if (value is IReadOnlyDictionary<string, string> cells && cells.TryGetValue(key, out var text))
        return text;

      return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
      BindingOperations.DoNothing;
  }
}
