using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.UI.Controls.Footer;
using MaksIT.ClusterConsole.UI.ViewModels.Connections;


namespace MaksIT.ClusterConsole.UI.Windows;

public partial class CloudConnectionWindow : Window {
  public CloudConnectionWindow() {
    InitializeComponent();
  }

  public CloudConnectionWindow(CloudConnectionViewModel viewModel) : this() {
    DataContext = viewModel;
    var download = new AsyncRelayCommand(DownloadAsync, () => viewModel.CanDownload);
    viewModel.PropertyChanged += (_, e) => {
      if (e.PropertyName == nameof(CloudConnectionViewModel.CanDownload))
        download.NotifyCanExecuteChanged();
    };
    var items = new List<FooterItem>();

    if (viewModel.ShowDownload)
      items.Add(new FooterButton("Download CLI", download));

    items.Add(new FooterButton("Cancel", new RelayCommand(() => Close(null))) { Edge = FooterEdge.Trailing });
    items.Add(new FooterButton("Add", new AsyncRelayCommand(AddAsync)) { Edge = FooterEdge.Trailing });
    Actions.Items = items;
  }

  public string? Note { get; private set; }

  private async Task AddAsync() {
    if (DataContext is not CloudConnectionViewModel vm)
      return;

    if (!await vm.WriteAsync())
      return;

    Note = vm.Note;
    Close(Note);
  }

  private async Task DownloadAsync() {
    if (DataContext is not CloudConnectionViewModel vm)
      return;

    await vm.DownloadAsync();
  }
}
