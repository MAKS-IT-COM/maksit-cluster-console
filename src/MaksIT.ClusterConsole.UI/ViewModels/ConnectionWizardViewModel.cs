using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Client;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public sealed record AuthKindChoice(string Id, string Title) {
  public override string ToString() => Title;
}

public partial class ConnectionWizardViewModel : ObservableObject {
  public IReadOnlyList<AuthKindChoice> AuthKinds { get; } = [
    new("token", "Bearer token"),
    new("cert", "Client certificate + key"),
    new("k3sdata", "Paste k3s certificate data"),
    new("basic", "Username and password")
  ];

  [ObservableProperty]
  private int step;

  [ObservableProperty]
  private string contextName = "";

  [ObservableProperty]
  private string clusterName = "";

  [ObservableProperty]
  private string userName = "";

  [ObservableProperty]
  private string namespaceName = "";

  [ObservableProperty]
  private string server = "";

  [ObservableProperty]
  private string caFile = "";

  [ObservableProperty]
  private bool embedClusterCa = true;

  [ObservableProperty]
  private bool insecureSkipTlsVerify;

  [ObservableProperty]
  private AuthKindChoice? selectedAuthKind;

  [ObservableProperty]
  private string token = "";

  [ObservableProperty]
  private string clientCertFile = "";

  [ObservableProperty]
  private string clientKeyFile = "";

  [ObservableProperty]
  private bool embedClientCerts = true;

  [ObservableProperty]
  private string caData = "";

  [ObservableProperty]
  private string clientCertData = "";

  [ObservableProperty]
  private string clientKeyData = "";

  [ObservableProperty]
  private string basicUser = "";

  [ObservableProperty]
  private string basicPassword = "";

  [ObservableProperty]
  private bool useAfterAdd = true;

  [ObservableProperty]
  private string error = "";

  public ConnectionWizardViewModel() {
    selectedAuthKind = AuthKinds[0];
  }

  public bool IsIdentityStep => Step == 0;

  public bool IsTlsStep => Step == 1;

  public bool IsAuthStep => Step == 2;

  public bool CanGoBack => Step > 0;

  public string NextLabel => Step == 2 ? "Add" : "Next";

  public string StepTitle => Step switch {
    0 => "Identity and server",
    1 => "TLS",
    _ => "Authentication"
  };

  public bool IsTokenAuth => SelectedAuthKind?.Id == "token";

  public bool IsCertAuth => SelectedAuthKind?.Id == "cert";

  public bool IsK3sDataAuth => SelectedAuthKind?.Id == "k3sdata";

  public bool IsBasicAuth => SelectedAuthKind?.Id == "basic";

  public string ClusterWatermark =>
    string.IsNullOrWhiteSpace(ContextName) ? "blank → {context}-cluster" : ContextName.Trim() + "-cluster";

  public string UserWatermark =>
    string.IsNullOrWhiteSpace(ContextName) ? "blank → {context}-user" : ContextName.Trim() + "-user";

  partial void OnStepChanged(int value) {
    OnPropertyChanged(nameof(IsIdentityStep));
    OnPropertyChanged(nameof(IsTlsStep));
    OnPropertyChanged(nameof(IsAuthStep));
    OnPropertyChanged(nameof(CanGoBack));
    OnPropertyChanged(nameof(NextLabel));
    OnPropertyChanged(nameof(StepTitle));
  }

  partial void OnSelectedAuthKindChanged(AuthKindChoice? value) {
    OnPropertyChanged(nameof(IsTokenAuth));
    OnPropertyChanged(nameof(IsCertAuth));
    OnPropertyChanged(nameof(IsK3sDataAuth));
    OnPropertyChanged(nameof(IsBasicAuth));
  }

  partial void OnContextNameChanged(string value) {
    OnPropertyChanged(nameof(ClusterWatermark));
    OnPropertyChanged(nameof(UserWatermark));
  }

  [RelayCommand]
  private void Back() {
    if (Step > 0)
      Step--;
    Error = "";
  }

  public bool TryAdvance() {
    Error = "";
    var invalid = Step switch {
      0 => ValidateIdentity(),
      1 => null,
      _ => ValidateAuth()
    };
    if (invalid is not null) {
      Error = invalid;
      return false;
    }

    if (Step < 2) {
      Step++;
      return false;
    }

    return true;
  }

  public KubeConnectionRequest ToRequest() {
    if (!KubeAuthKind.TryParse(SelectedAuthKind?.Id, out var kind))
      kind = KubeAuthKind.Token;

    return new KubeConnectionRequest {
      ContextName = ContextName.Trim(),
      ClusterName = BlankToNull(ClusterName),
      UserName = BlankToNull(UserName),
      Namespace = BlankToNull(NamespaceName),
      Server = Server.Trim(),
      CaFile = BlankToNull(CaFile),
      CaData = kind == KubeAuthKind.K3sData ? BlankToNull(CaData) : null,
      EmbedClusterCa = EmbedClusterCa,
      InsecureSkipTlsVerify = InsecureSkipTlsVerify,
      AuthKind = kind,
      Token = kind == KubeAuthKind.Token ? BlankToNull(Token) : null,
      ClientCertFile = kind == KubeAuthKind.Cert ? BlankToNull(ClientCertFile) : null,
      ClientKeyFile = kind == KubeAuthKind.Cert ? BlankToNull(ClientKeyFile) : null,
      ClientCertData = kind == KubeAuthKind.K3sData ? BlankToNull(ClientCertData) : null,
      ClientKeyData = kind == KubeAuthKind.K3sData ? BlankToNull(ClientKeyData) : null,
      EmbedClientCerts = EmbedClientCerts,
      BasicUser = kind == KubeAuthKind.Basic ? BlankToNull(BasicUser) : null,
      BasicPassword = kind == KubeAuthKind.Basic ? BlankToNull(BasicPassword) : null,
      UseAfterAdd = UseAfterAdd
    };
  }

  private string? ValidateIdentity() {
    if (string.IsNullOrWhiteSpace(ContextName))
      return "Context name is required.";
    if (string.IsNullOrWhiteSpace(Server))
      return "Server URL is required.";
    return null;
  }

  private string? ValidateAuth() {
    var id = SelectedAuthKind?.Id;
    if (id == "token" && string.IsNullOrWhiteSpace(Token))
      return "Token is required.";
    if (id == "cert" && (string.IsNullOrWhiteSpace(ClientCertFile) || string.IsNullOrWhiteSpace(ClientKeyFile)))
      return "Client certificate and key files are required.";
    if (id == "k3sdata"
        && (string.IsNullOrWhiteSpace(CaData)
            || string.IsNullOrWhiteSpace(ClientCertData)
            || string.IsNullOrWhiteSpace(ClientKeyData)))
      return "CA, client certificate, and client key data are required.";
    if (id == "basic" && (string.IsNullOrWhiteSpace(BasicUser) || string.IsNullOrWhiteSpace(BasicPassword)))
      return "Username and password are required.";
    return null;
  }

  private static string? BlankToNull(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
