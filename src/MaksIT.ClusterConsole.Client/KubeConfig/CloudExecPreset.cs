using System.Text;


namespace MaksIT.ClusterConsole.Client.KubeConfig;

/// <summary>
/// Exec credential plugins kubectl writes for the major managed Kubernetes services.
/// The CLI named by <see cref="Command"/> must be on PATH. Flatpak starts a copy downloaded
/// into the app. It cannot start a host install.
/// </summary>
public sealed record CloudExecPreset(
  string Id,
  string Title,
  string Command,
  IReadOnlyList<string> Args,
  string ArgumentLabel,
  bool ArgumentRequired,
  bool ProvideClusterInfo,
  string InstallHint,
  bool CanFetch = false,
  string ClusterLabel = "Cluster",
  string LocationLabel = "",
  string ProjectLabel = "",
  bool FetchOnly = false) {
  public const string CustomId = "custom";

  public static readonly CloudExecPreset Google = new(
    "google",
    "Google Kubernetes Engine",
    "gke-gcloud-auth-plugin",
    [],
    "",
    false,
    true,
    "Install with: gcloud components install gke-gcloud-auth-plugin. The plugin must be on PATH.",
    CanFetch: true,
    ClusterLabel: "Cluster name",
    LocationLabel: "Location (region or zone)",
    ProjectLabel: "Project");

  public static readonly CloudExecPreset Azure = new(
    "azure",
    "Azure Kubernetes Service",
    "kubelogin",
    ["get-token", "--login", "azurecli", "--server-id", "6dae42f8-4368-4678-94ff-3960e28e3630"],
    "",
    false,
    false,
    "Install kubelogin and sign in with az login. This server id is public Azure. Use Custom for Azure Government or China.",
    CanFetch: true,
    ClusterLabel: "Cluster name",
    ProjectLabel: "Resource group");

  public static readonly CloudExecPreset Amazon = new(
    "amazon",
    "Amazon Elastic Kubernetes Service",
    "aws",
    ["eks", "get-token", "--cluster-name"],
    "Cluster name",
    true,
    false,
    "Install the AWS CLI and run aws configure. The default region is used unless the CLI config sets one.",
    CanFetch: true,
    ClusterLabel: "Cluster name",
    LocationLabel: "Region");

  public static readonly CloudExecPreset Oracle = new(
    "oracle",
    "Oracle Container Engine for Kubernetes",
    "oci",
    ["ce", "cluster", "generate-token", "--cluster-id"],
    "Cluster OCID",
    true,
    false,
    "Install the OCI CLI and configure a region. The cluster OCID is the extra argument.",
    CanFetch: true,
    ClusterLabel: "Cluster OCID",
    LocationLabel: "Region");

  public static readonly CloudExecPreset DigitalOcean = new(
    "digitalocean",
    "DigitalOcean Kubernetes",
    "doctl",
    ["kubernetes", "cluster", "kubeconfig", "exec", "--version", "v1beta1"],
    "Cluster ID",
    true,
    false,
    "Install doctl and run doctl auth init.",
    CanFetch: true,
    ClusterLabel: "Cluster ID or name");

  public static readonly CloudExecPreset Ibm = new(
    "ibm",
    "IBM Cloud Kubernetes Service",
    "ibmcloud",
    [],
    "",
    false,
    false,
    "Install the IBM Cloud CLI and the container service plugin, then ibmcloud login. The CLI writes the kubeconfig.",
    CanFetch: true,
    ClusterLabel: "Cluster name or ID",
    FetchOnly: true);

  public static readonly CloudExecPreset Custom = new(
    CustomId,
    "Custom exec plugin",
    "",
    [],
    "Arguments",
    false,
    false,
    "Any other provider whose kubeconfig uses user.exec, such as Linode, Alibaba, Civo, or Scaleway. Command is the executable. Arguments are split on spaces. If that CLI writes a kubeconfig, open the context it created.");

  public static IReadOnlyList<CloudExecPreset> All { get; } = [
    Google,
    Azure,
    Amazon,
    Oracle,
    DigitalOcean,
    Ibm,
    Custom
  ];

  public static CloudExecPreset? Find(string? id) =>
    All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

  public IList<string> Arguments(string? extra) {
    if (Id == CustomId)
      return SplitArgs(extra);

    if (string.IsNullOrWhiteSpace(extra))
      return Args.ToList();

    return [..Args, extra.Trim()];
  }

  public static List<string> SplitArgs(string? text) {
    var args = new List<string>();

    if (string.IsNullOrWhiteSpace(text))
      return args;

    var current = new StringBuilder();
    var quote = '\0';

    foreach (var ch in text.Trim()) {
      if (quote == '\0' && ch is '"' or '\'') {
        quote = ch;

        continue;
      }

      if (quote != '\0' && ch == quote) {
        quote = '\0';

        continue;
      }

      if (quote == '\0' && char.IsWhiteSpace(ch)) {
        if (current.Length > 0) {
          args.Add(current.ToString());
          current.Clear();
        }

        continue;
      }

      current.Append(ch);
    }

    if (current.Length > 0)
      args.Add(current.ToString());

    return args;
  }
}
