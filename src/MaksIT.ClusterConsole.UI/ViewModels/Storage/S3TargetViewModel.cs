using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;
using MaksIT.ClusterConsole.Client.S3;


namespace MaksIT.ClusterConsole.UI.ViewModels.Storage;

public partial class S3TargetViewModel : ObservableObject {
  private readonly string _namespace;
  private readonly string _credential;
  private readonly string _poll;
  private readonly string _storedSecret;
  private readonly string _storedCertificate;
  private readonly HashSet<string> _names;
  private bool _loading = true;
  private bool _adjusting;
  private bool _pathStyleTouched;

  public S3TargetViewModel(string ns, IReadOnlyCollection<string> names, S3Target? existing) {
    var regionText = existing?.Region;
    _namespace = string.IsNullOrWhiteSpace(existing?.Namespace) ? ns : existing!.Namespace;
    _names = new HashSet<string>(names, StringComparer.Ordinal);
    IsEdit = existing is not null;
    name = existing?.Name ?? (_names.Contains("default") ? "" : "default");
    bucket = existing?.Bucket ?? "";
    region = string.IsNullOrWhiteSpace(regionText) ? "us-east-1" : regionText;
    prefix = existing?.Prefix ?? "";
    endpoint = existing?.Endpoint ?? "";
    pathStyle = existing?.PathStyle ?? false;
    accessKey = existing?.AccessKey ?? "";
    _storedSecret = existing?.SecretKey ?? "";
    _storedCertificate = existing?.Certificate ?? "";
    _credential = existing?.CredentialSecret ?? "";
    _poll = string.IsNullOrWhiteSpace(existing?.PollInterval) ? LonghornS3.DefaultPoll : existing.PollInterval;
    Heading = IsEdit ? "Edit S3 target" : "Add S3 target";
    _loading = false;
  }

  public bool IsEdit { get; }

  public bool NameEnabled =>
    !IsEdit;

  public string Heading { get; }

  public string SecretHint =>
    IsEdit
      ? "Leave the secret and the certificate empty to keep the saved values. The secret stays in the Longhorn namespace."
      : "The access key and secret are stored in a Kubernetes secret. default is the Longhorn default backup target. Any other name is an additional target.";

  [ObservableProperty]
  private string name;

  [ObservableProperty]
  private string bucket;

  [ObservableProperty]
  private string region;

  [ObservableProperty]
  private string prefix;

  [ObservableProperty]
  private string endpoint;

  [ObservableProperty]
  private bool pathStyle;

  [ObservableProperty]
  private string accessKey;

  [ObservableProperty]
  private string secretKey = "";

  [ObservableProperty]
  private string certificate = "";

  [ObservableProperty]
  private string error = "";

  partial void OnEndpointChanged(string value) {
    if (_loading || _pathStyleTouched)
      return;

    _adjusting = true;
    PathStyle = !string.IsNullOrWhiteSpace(value);
    _adjusting = false;
  }

  partial void OnPathStyleChanged(bool value) {
    if (_loading || _adjusting)
      return;

    _pathStyleTouched = true;
  }

  public bool TryAccept() {
    Error = Validate();

    return Error.Length == 0;
  }

  public S3Target ToTarget() {
    var targetName = Name.Trim().ToLowerInvariant();
    var secret = SecretKey.Trim();
    var cert = Certificate.Trim();

    return new S3Target(
      targetName,
      _namespace,
      Bucket.Trim(),
      Region.Trim(),
      Prefix.Trim().Trim('/'),
      Endpoint.Trim(),
      AccessKey.Trim(),
      secret.Length == 0 ? _storedSecret : secret,
      PathStyle,
      cert.Length == 0 ? _storedCertificate : cert,
      _credential.Length == 0 ? LonghornS3.SecretNameFor(targetName) : _credential,
      _poll);
  }

  private string Validate() {
    Name = Name.Trim().ToLowerInvariant();
    Bucket = Bucket.Trim();
    Region = Region.Trim();
    Prefix = Prefix.Trim().Trim('/');
    Endpoint = Endpoint.Trim();
    AccessKey = AccessKey.Trim();

    if (!S3Layout.IsResourceName(Name))
      return "Use a lowercase name, such as default or offsite.";

    if (!IsEdit && _names.Contains(Name))
      return "That target already exists.";

    if (!S3Layout.IsBucket(Bucket))
      return "Enter the bucket name.";

    if (!S3Layout.IsRegion(Region))
      return "Enter the region. us-east-1 is the usual value.";

    if (!S3Layout.IsPrefix(Prefix))
      return "The path cannot contain .. or an empty segment.";

    if (Endpoint.Length > 0) {
      foreach (var item in Endpoint.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
        if (!item.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !item.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
          return "Each endpoint must start with http:// or https://.";
      }
    }

    if (AccessKey.Length == 0)
      return "Enter the access key.";

    if (!IsEdit && SecretKey.Trim().Length == 0)
      return "Enter the secret key.";

    return "";
  }
}
