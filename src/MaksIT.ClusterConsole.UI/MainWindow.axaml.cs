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
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.UI.Controls;
using MaksIT.ClusterConsole.UI.Converters;
using MaksIT.ClusterConsole.UI.ViewModels;


namespace MaksIT.ClusterConsole.UI;

public partial class MainWindow : Window {
  private LayoutPersistence? _layout;

  public MainWindow() {
    InitializeComponent();
  }

  public MainWindow(MainViewModel viewModel, ConfigurationFileService configuration) : this() {
    DataContext = viewModel;
    _layout = new LayoutPersistence(this, configuration, () => viewModel.SelectedDescriptor?.Id);
    Opened += (_, _) => {
      _layout.Attach();
      RebuildColumns(viewModel);
    };
    viewModel.PropertyChanged += (_, e) => {
      if (e.PropertyName is nameof(MainViewModel.SelectedNavItem) or nameof(MainViewModel.ActivePage))
        RebuildColumns(viewModel);
    };
    viewModel.ConnectionsRequested += async (_, _) => await OpenConnectionsAsync(viewModel);
    viewModel.VolumeFilesRequested += OpenVolumeFiles;
  }

  private void OpenVolumeFiles(VolumeFilesViewModel files) {
    var window = new VolumeFilesWindow(files);
    window.Show(this);
  }

  private async Task OpenConnectionsAsync(MainViewModel viewModel) {
    var window = new ConnectionsWindow(viewModel.CreateConnectionsViewModel());
    var connect = await window.ShowDialog<string?>(this);
    viewModel.LoadCatalogCommand.Execute(null);
    if (!string.IsNullOrWhiteSpace(connect))
      await viewModel.ConnectNamedAsync(connect);
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

  private void RebuildColumns(MainViewModel viewModel) {
    var grid = this.FindControl<DataGrid>("ResourceGrid");
    if (grid is null)
      return;

    grid.Columns.Clear();
    var descriptor = viewModel.SelectedDescriptor;
    var headers = descriptor?.Columns.Select(c => c.Header).ToList()
      ?? ["Name", "Namespace", "Age"];

    foreach (var header in headers) {
      grid.Columns.Add(CreateColumn(header, viewModel.ActivePage));
    }

    _layout?.ApplyResourceColumns(grid);
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
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = 72
      };
    }

    return new DataGridTextColumn {
      Header = columnHeader,
      Tag = header,
      CanUserSort = true,
      CustomSortComparer = comparer,
      Binding = new Binding(nameof(ResourceRow.Cells)) {
        Mode = BindingMode.OneWay,
        Converter = new DictionaryKeyConverter(header)
      },
      Width = new DataGridLength(1, DataGridLengthUnitType.Star),
      MinWidth = 72
    };
  }

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
