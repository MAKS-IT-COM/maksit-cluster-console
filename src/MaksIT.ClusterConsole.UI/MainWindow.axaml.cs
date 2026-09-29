using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SvcSystems.UI.Terminal;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Controls;
using MaksIT.ClusterConsole.UI.Converters;
using MaksIT.ClusterConsole.UI.ViewModels;
using MaksIT.ClusterConsole.UI.Windows;


namespace MaksIT.ClusterConsole.UI;

public partial class MainWindow : Window {
  private LayoutPersistence? _layout;
  private ClusterPageViewModel? _activePage;
  private bool _syncingDetailsTab;

  public MainWindow() {
    InitializeComponent();
  }

  public MainWindow(MainViewModel viewModel, ConfigurationFileService configuration) : this() {
    DataContext = viewModel;
    _layout = new LayoutPersistence(
      this,
      configuration,
      () => viewModel.ActivePage?.Name,
      () => viewModel.SelectedDescriptor?.Id);
    Opened += (_, _) => {
      _layout.Attach();
      RebuildColumns(viewModel);
    };
    viewModel.PropertyChanged += (_, e) => {
      if (e.PropertyName is nameof(MainViewModel.SelectedNavItem) or nameof(MainViewModel.ActivePage))
        RebuildColumns(viewModel);

      if (e.PropertyName == nameof(MainViewModel.ActivePage))
        HookActivePage(viewModel.ActivePage);
    };
    HookActivePage(viewModel.ActivePage);
    viewModel.ConnectionsRequested += async (_, _) => await OpenConnectionsAsync(viewModel);
    viewModel.AiSettingsRequested += async (_, _) => await OpenAiSettingsAsync(viewModel);
    viewModel.VolumeFilesRequested += OpenVolumeFiles;
    viewModel.ShowRetainReclaim = ShowRetainReclaimAsync;
  }

  private void OnLogsClick(object? sender, RoutedEventArgs e) =>
    _ = LogWindow.ShowAsync(this);

  private void OnAboutClick(object? sender, RoutedEventArgs e) =>
    _ = AboutWindow.ShowAsync(this);

  private void OpenVolumeFiles(VolumeFilesViewModel files) {
    var window = new VolumeFilesWindow(files);
    window.Show(this);
  }

  private async Task ShowRetainReclaimAsync(RetainReclaimViewModel reclaim) {
    var window = new RetainReclaimWindow(reclaim);
    await window.ShowDialog(this);
  }

  private void HookActivePage(ClusterPageViewModel? page) {
    if (_activePage is not null) {
      _activePage.PropertyChanged -= OnActivePagePropertyChanged;
      _activePage.SelectedRowsRestored -= OnSelectedRowsRestored;
    }

    _activePage = page;
    if (page is not null) {
      page.PropertyChanged += OnActivePagePropertyChanged;
      page.SelectedRowsRestored += OnSelectedRowsRestored;
      ApplyDetailsTab(page.SelectedTab);
    }
  }

  private void OnActivePagePropertyChanged(object? sender, PropertyChangedEventArgs e) {
    if (e.PropertyName == nameof(ClusterPageViewModel.SelectedTab) && sender is ClusterPageViewModel page)
      ApplyDetailsTab(page.SelectedTab);

    OnLogsPagePropertyChanged(sender, e);
  }

  private void OnDetailsTabChanged(object? sender, SelectionChangedEventArgs e) {
    if (_syncingDetailsTab || sender is not TabControl tabs || !ReferenceEquals(e.Source, tabs))
      return;
    if (DataContext is not MainViewModel { ActivePage: { } page })
      return;
    if (tabs.SelectedItem is not TabItem { Header: string header } || page.SelectedTab == header)
      return;

    page.SelectedTab = header;
    if (header == "Terminal")
      FocusTerminal();
  }

  private void ApplyDetailsTab(string header) {
    var tabs = this.FindControl<TabControl>("DetailsTabs");
    if (tabs is null)
      return;

    foreach (var item in tabs.Items) {
      if (item is not TabItem { Header: string title, IsVisible: true } tab)
        continue;
      if (!string.Equals(title, header, StringComparison.Ordinal))
        continue;
      if (ReferenceEquals(tabs.SelectedItem, tab)) {
        if (header == "Terminal")
          FocusTerminal();
        return;
      }

      _syncingDetailsTab = true;
      tabs.SelectedItem = tab;
      _syncingDetailsTab = false;
      if (header == "Terminal")
        FocusTerminal();
      return;
    }
  }

  private void FocusTerminal() =>
    Dispatcher.UIThread.Post(() => this.FindControl<TerminalControl>("PodTerminal")?.Focus());

  private void OnLogsPagePropertyChanged(object? sender, PropertyChangedEventArgs e) {
    if (e.PropertyName != nameof(ClusterPageViewModel.LogsText))
      return;
    if (sender is not ClusterPageViewModel { FollowLogs: true })
      return;

    var box = this.FindControl<TextBox>("LogsTextBox");
    if (box is null)
      return;

    box.CaretIndex = box.Text?.Length ?? 0;
  }

  private async Task OpenAiSettingsAsync(MainViewModel viewModel) {
    var window = new AiSettingsWindow(viewModel.CreateAiSettingsViewModel());
    await window.ShowDialog<bool>(this);
    if (viewModel.ShowChat)
      return;

    if (this.FindControl<TabControl>("DetailsTabs") is { SelectedItem: TabItem { Header: "Chat" } } tabs)
      tabs.SelectedIndex = 0;
  }

  private async Task OpenConnectionsAsync(MainViewModel viewModel) {
    var window = new ConnectionsWindow(viewModel.CreateConnectionsViewModel());
    var connect = await window.ShowDialog<string?>(this);
    viewModel.LoadCatalogCommand.Execute(null);
    if (!string.IsNullOrWhiteSpace(connect))
      await viewModel.ConnectNamedAsync(connect);
  }

  private void OnResourceGridSelectionChanged(object? sender, SelectionChangedEventArgs e) {
    if (DataContext is not MainViewModel { ActivePage: { } page })
      return;
    if (page.SyncingSelection || sender is not DataGrid grid)
      return;

    page.ReplaceSelectedRows(grid.SelectedItems.OfType<ResourceRow>());
  }

  private void OnSelectedRowsRestored(IReadOnlyList<ResourceRow> rows) {
    var grid = this.FindControl<DataGrid>("ResourceGrid");
    if (grid is null)
      return;

    ApplyGridSelection(grid, rows, _activePage?.SelectedRow);
  }

  private static void ApplyGridSelection(DataGrid grid, IReadOnlyList<ResourceRow> rows, ResourceRow? current) {
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

  private void OnResourceGridDoubleTapped(object? sender, TappedEventArgs e) {
    if (e.Source is not Control { DataContext: ResourceRow })
      return;
    if (DataContext is not MainViewModel { ActivePage: { } page })
      return;
    if (page.IsPortForwardingView) {
      page.OpenSelectedPortForwardCommand.Execute(null);
      return;
    }

    if (page.BrowseFilesCommand.CanExecute(null))
      page.BrowseFilesCommand.Execute(null);
  }

  private void OnResourceGridCopyingRowClipboardContent(object? sender, DataGridRowClipboardEventArgs e) {
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

  private static string ColumnHeaderText(DataGridColumn column) {
    if (column.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
      return tag;
    if (column.Header is string header)
      return header;
    if (column.Header is Control { DataContext: ColumnFilterViewModel filter })
      return filter.Header;
    return column.Header?.ToString() ?? "";
  }

  private void RebuildColumns(MainViewModel viewModel) {
    var grid = this.FindControl<DataGrid>("ResourceGrid");
    if (grid is null)
      return;

    using (_layout?.SuspendSave()) {
      viewModel.ActivePage?.ReloadColumnFilters();
      grid.Columns.Clear();
      var descriptor = viewModel.SelectedDescriptor;
      var headers = descriptor?.Columns.Select(c => c.Header).ToList()
        ?? ["Name", "Namespace", "Age"];

      foreach (var header in headers) {
        grid.Columns.Add(CreateColumn(header, viewModel.ActivePage));
      }

      _layout?.RestoreTables();
    }
  }

  private DataGridColumn CreateColumn(string header, ClusterPageViewModel? page) {
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
