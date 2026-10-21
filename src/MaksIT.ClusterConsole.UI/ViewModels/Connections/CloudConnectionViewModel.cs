using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Client.KubeConfig;



namespace MaksIT.ClusterConsole.UI.ViewModels.Connections;

public partial class CloudConnectionViewModel : ObservableObject {
  private readonly string _kubeconfigPath;

  public IReadOnlyList<CloudExecPreset> Providers { get; } =
    CloudExecPreset.All.Where(provider => provider.CanFetch).ToList();

  [ObservableProperty]
  private CloudExecPreset? selectedProvider;

  [ObservableProperty]
  private string cluster = "";

  [ObservableProperty]
  private string location = "";

  [ObservableProperty]
  private string project = "";

  [ObservableProperty]
  private string error = "";

  [ObservableProperty]
  private string cliStatus = "";

  [ObservableProperty]
  private bool canDownload;

  [ObservableProperty]
  private bool cliBusy;

  private int _statusGeneration;

  public string Note { get; private set; } = "";

  public bool ShowDownload => OperatingSystem.IsLinux();

  public bool HasCliStatus => CliStatus.Length > 0;

  public CloudConnectionViewModel(string kubeconfigPath) {
    _kubeconfigPath = kubeconfigPath;
    selectedProvider = Providers[0];
    RefreshCli();
  }

  public bool ShowLocation => !string.IsNullOrEmpty(SelectedProvider?.LocationLabel);

  public bool ShowProject => !string.IsNullOrEmpty(SelectedProvider?.ProjectLabel);

  public string ClusterLabel => SelectedProvider?.ClusterLabel ?? "Cluster";

  public string LocationLabel => SelectedProvider?.LocationLabel ?? "";

  public string ProjectLabel => SelectedProvider?.ProjectLabel ?? "";

  public string Hint => SelectedProvider?.InstallHint ?? "";

  partial void OnSelectedProviderChanged(CloudExecPreset? value) {
    OnPropertyChanged(nameof(ShowLocation));
    OnPropertyChanged(nameof(ShowProject));
    OnPropertyChanged(nameof(ClusterLabel));
    OnPropertyChanged(nameof(LocationLabel));
    OnPropertyChanged(nameof(ProjectLabel));
    OnPropertyChanged(nameof(Hint));
    RefreshCli();
  }

  partial void OnCliStatusChanged(string value) =>
    OnPropertyChanged(nameof(HasCliStatus));

  public string? Validate() {
    if (string.IsNullOrWhiteSpace(Cluster))
      return ClusterLabel + " is required.";

    if (ShowLocation && string.IsNullOrWhiteSpace(Location))
      return LocationLabel + " is required.";

    if (ShowProject && string.IsNullOrWhiteSpace(Project))
      return ProjectLabel + " is required.";

    return null;
  }

  public async Task<bool> WriteAsync() {
    Error = "";
    Note = "";
    var invalid = Validate();

    if (invalid is not null) {
      Error = invalid;

      return false;
    }

    var built = CloudKubeconfig.Build(new CloudFetchRequest(
      SelectedProvider?.Id ?? "",
      Cluster,
      Location,
      Project,
      _kubeconfigPath));

    if (!built.IsSuccess || built.Value is null) {
      Error = string.Join("; ", built.Messages);

      return false;
    }

    var ran = await CloudKubeconfig.RunAsync(built.Value, _kubeconfigPath).ConfigureAwait(true);

    if (!ran.IsSuccess) {
      Error = string.Join("; ", ran.Messages);

      return false;
    }

    Note = "The cloud CLI wrote the context. Select it and connect.";

    return true;
  }

  public async Task DownloadAsync() {
    if (CliBusy || !ShowDownload)
      return;

    var providerId = SelectedProvider?.Id;
    var generation = _statusGeneration;
    CliBusy = true;
    CanDownload = false;
    Error = "";

    try {
      var result = await CloudCliSetup.InstallMissingAsync(
        providerId,
        message => Dispatcher.UIThread.Post(() => {
          if (generation == _statusGeneration)
            CliStatus = message;
        })).ConfigureAwait(true);

      if (generation != _statusGeneration)
        return;

      if (!result.IsSuccess) {
        Error = string.Join("; ", result.Messages);
        CliStatus = "";

        return;
      }

      CliStatus = "Installed. Sign in with that CLI, then Add.";
    }
    finally {
      CliBusy = false;
      RefreshCli(generation == _statusGeneration);
    }
  }

  private void RefreshCli(bool keepStatus = false) {
    if (!keepStatus)
      _statusGeneration++;

    if (!ShowDownload) {
      CanDownload = false;

      if (!keepStatus)
        CliStatus = "";

      return;
    }

    var missing = CloudCliSandbox.Missing(SelectedProvider?.Id);
    CanDownload = missing.Count > 0 && !CliBusy;

    if (missing.Count == 0) {
      if (!keepStatus)
        CliStatus = "";

      return;
    }

    CliStatus = "Missing " + string.Join(", ", missing) + ". Download CLI puts them in this app so the sandbox can start them.";
  }
}
