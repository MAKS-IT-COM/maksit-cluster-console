using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Shared;

public static class ResourceCatalog {
  public const string Cluster = "Cluster";
  public const string Nodes = "Nodes";
  public const string Workloads = "Workloads";
  public const string Applications = "Applications";
  public const string Config = "Config";
  public const string Network = "Network";
  public const string Storage = "Storage";
  public const string Namespaces = "Namespaces";
  public const string Events = "Events";
  public const string Helm = "Helm";
  public const string Dapr = "Dapr";
  public const string Longhorn = "Longhorn";
  public const string CloudNativePG = "CloudNativePG";
  public const string AccessControl = "Access Control";
  public const string CustomResources = "Custom Resources";

  public const string OverviewId = "overview";
  public const string WorkloadsOverviewId = "workloads-overview";
  public const string ApplicationsId = "applications";
  public const string PortForwardingId = "port-forwarding";
  public const string HelmChartsId = "helm-charts";
  public const string HelmReleasesId = "helm-releases";
  public const string LonghornVolumesId = "longhorn-volumes";
  public const string LonghornNodesId = "longhorn-nodes";
  public const string CnpgClustersId = "cnpg-clusters";
  public const string DaprSidecarsId = "dapr-sidecars";
  public const string DaprControlPlaneId = "dapr-control-plane";

  public static IReadOnlyList<string> Sections { get; } = [
    Cluster,
    Nodes,
    Applications,
    Workloads,
    Config,
    Network,
    Storage,
    Longhorn,
    Namespaces,
    Events,
    Helm,
    Dapr,
    CloudNativePG,
    AccessControl,
    CustomResources
  ];

  public static IReadOnlyList<ResourceDescriptor> BuiltIns { get; } = Build();

  public static ResourceDescriptor ApplicationsDescriptor { get; } = new(
    ApplicationsId,
    "Applications",
    Applications,
    "",
    "v1",
    "applications",
    "Application",
    true,
    [
      new("Instance", "app.instance"),
      new("Namespace", "metadata.namespace"),
      new("Managed by", "app.managedBy"),
      new("Version", "app.version"),
      new("Ready", "status.ready"),
      new("CPU", "app.cpu"),
      new("Memory", "app.memory"),
      new("Status", "status.phase"),
      new("Age", "metadata.creationTimestamp")
    ],
    new ResourceActions(CanScale: false, CanRestart: false, CanApply: false),
    [DetailTab.Overview, DetailTab.Yaml, DetailTab.Events, DetailTab.Pods, DetailTab.Logs, DetailTab.Terminal]);

  public static ResourceDescriptor PortForwardingDescriptor { get; } = new(
    PortForwardingId,
    "Port Forwarding",
    Network,
    "",
    "v1",
    "portforwards",
    "PortForward",
    true,
    [
      new("Name", "metadata.name"),
      new("Namespace", "metadata.namespace"),
      new("Pod", "pod"),
      new("Local", "localPort"),
      new("Remote", "containerPort"),
      new("Status", "status")
    ],
    new ResourceActions(CanDelete: false, CanApply: false),
    [DetailTab.Overview]);

  public static ResourceDescriptor HelmChartsDescriptor { get; } = new(
    HelmChartsId,
    "Charts",
    Helm,
    "",
    "v1",
    "secrets",
    "Secret",
    false,
    [
      new("Chart", "chart"),
      new("Version", "version"),
      new("App", "appVersion"),
      new("Status", "status"),
      new("Releases", "releases"),
      new("Namespaces", "namespaces")
    ],
    new ResourceActions(CanDelete: false, CanApply: false),
    [DetailTab.Overview]);

  public static ResourceDescriptor HelmReleasesDescriptor { get; } = new(
    HelmReleasesId,
    "Releases",
    Helm,
    "",
    "v1",
    "secrets",
    "Secret",
    true,
    [
      new("Name", "metadata.name"),
      new("Namespace", "metadata.namespace"),
      new("Revision", "revision"),
      new("Status", "status"),
      new("Chart", "chart"),
      new("App", "appVersion"),
      new("Updated", "updated")
    ],
    new ResourceActions(CanDelete: false, CanApply: false),
    [DetailTab.Overview, DetailTab.History, DetailTab.Values, DetailTab.Manifest]);

  public static ResourceDescriptor? Find(string id) =>
    id switch {
      ApplicationsId => ApplicationsDescriptor,
      PortForwardingId => PortForwardingDescriptor,
      HelmReleasesId => HelmReleasesDescriptor,
      HelmChartsId => HelmChartsDescriptor,
      _ => BuiltIns.FirstOrDefault(d => d.Id == id)
    };

  public static ResourceDescriptor? FindByGvk(string? apiVersion, string? kind) {
    if (string.IsNullOrWhiteSpace(kind))
      return null;

    apiVersion ??= "v1";
    var slash = apiVersion.IndexOf('/');
    var group = slash < 0 ? "" : apiVersion[..slash];
    var version = slash < 0 ? apiVersion : apiVersion[(slash + 1)..];

    return BuiltIns.FirstOrDefault(d =>
      d.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
      && d.Group.Equals(group, StringComparison.OrdinalIgnoreCase)
      && d.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
  }

  public static ResourceDescriptor? FromCustomResourceDefinition(JsonObject crd) {
    var spec = crd["spec"] as JsonObject;
    var group = spec?["group"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(group)
        || group.Equals("apiextensions.k8s.io", StringComparison.OrdinalIgnoreCase))
      return null;

    var names = spec?["names"] as JsonObject;
    var plural = names?["plural"]?.GetValue<string>();
    var kind = names?["kind"]?.GetValue<string>();

    if (string.IsNullOrEmpty(plural) || string.IsNullOrEmpty(kind)
        || kind.Equals("CustomResourceDefinition", StringComparison.OrdinalIgnoreCase))
      return null;

    var version = JsonPath.CrdStorageVersion(crd);

    if (string.IsNullOrEmpty(version))
      return null;

    var scope = spec?["scope"]?.GetValue<string>();
    var namespaced = !string.Equals(scope, "Cluster", StringComparison.OrdinalIgnoreCase);
    var columns = new List<ColumnSpec> {
      new("Name", "metadata.name"),
      new("Namespace", "metadata.namespace")
    };
    var stored = CrdVersion(crd, version);

    if (stored?["additionalPrinterColumns"] is JsonArray printers) {
      foreach (var column in printers.OfType<JsonObject>()) {
        var header = column["name"]?.GetValue<string>();
        var jsonPath = column["jsonPath"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(jsonPath))
          continue;

        if (columns.Any(c => c.Header.Equals(header, StringComparison.OrdinalIgnoreCase)))
          continue;

        var path = PrinterColumnPath(jsonPath);

        if (string.IsNullOrEmpty(path))
          continue;

        columns.Add(new ColumnSpec(header, path));
      }
    }

    columns.Add(new ColumnSpec("Age", "metadata.creationTimestamp"));

    return new ResourceDescriptor(
      $"crd:{group}/{version}/{plural}",
      kind,
      CustomResources,
      group,
      version,
      plural,
      kind,
      namespaced,
      columns,
      new ResourceActions(),
      [DetailTab.Overview, DetailTab.Yaml, DetailTab.Events]);
  }

  public static string PrinterColumnPath(string jsonPath) {
    var path = jsonPath.Trim();

    if (path.StartsWith('.'))
      path = path[1..];

    var bracket = path.IndexOf('[');

    if (bracket >= 0)
      path = path[..bracket];

    return path.Trim('.');
  }

  private static JsonObject? CrdVersion(JsonObject crd, string version) {
    var versions = crd["spec"]?["versions"] as JsonArray;

    return versions?.OfType<JsonObject>().FirstOrDefault(v =>
      string.Equals(v["name"]?.GetValue<string>(), version, StringComparison.Ordinal));
  }

  public static IReadOnlyList<(string Group, IReadOnlyList<ResourceDescriptor> Kinds)> GroupCustomResources(
    IEnumerable<ResourceDescriptor> descriptors) =>
    descriptors
      .Where(d => d.Section == CustomResources && d.Kind != "CustomResourceDefinition")
      .GroupBy(d => d.Group, StringComparer.Ordinal)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
      .Select(g => (g.Key, (IReadOnlyList<ResourceDescriptor>)g.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase).ToList()))
      .ToList();

  private static IReadOnlyList<ResourceDescriptor> Build() {
    var yamlTabs = new[] { DetailTab.Overview, DetailTab.Yaml, DetailTab.Events };
    var nodeTabs = new[] { DetailTab.Overview, DetailTab.Yaml, DetailTab.Events, DetailTab.Images };
    var podTabs = new[] { DetailTab.Overview, DetailTab.Yaml, DetailTab.Events, DetailTab.Logs, DetailTab.Terminal };
    var workloadTabs = new[] { DetailTab.Overview, DetailTab.Yaml, DetailTab.Events, DetailTab.Pods, DetailTab.Logs, DetailTab.Terminal };
    var serviceTabs = new[] { DetailTab.Overview, DetailTab.Yaml, DetailTab.Events, DetailTab.Pods };
    var crud = new ResourceActions();
    var scale = new ResourceActions(CanScale: true, CanRestart: true);
    var logs = new ResourceActions(CanLogs: true, CanExec: true, CanPortForward: true, CanAttach: true, CanDebug: true);
    var node = new ResourceActions(CanCordon: true, CanDrain: true);
    var cron = new ResourceActions(CanTrigger: true);

    ColumnSpec[] std = [new("Name", "metadata.name"), new("Namespace", "metadata.namespace"), new("Age", "metadata.creationTimestamp")];
    ColumnSpec[] named = [new("Name", "metadata.name"), new("Age", "metadata.creationTimestamp")];

    return [
      D("apiservices", "API Services", Cluster, "apiregistration.k8s.io", "v1", "apiservices", "APIService", false, named, crud, yamlTabs),
      D("flowschemas", "Flow Schemas", Cluster, "flowcontrol.apiserver.k8s.io", "v1", "flowschemas", "FlowSchema", false, named, crud, yamlTabs),
      D("prioritylevelconfigurations", "Priority Levels", Cluster, "flowcontrol.apiserver.k8s.io", "v1", "prioritylevelconfigurations", "PriorityLevelConfiguration", false, named, crud, yamlTabs),
      D("deviceclasses", "Device Classes", Cluster, "resource.k8s.io", "v1", "deviceclasses", "DeviceClass", false, named, crud, yamlTabs),
      D("resourceslices", "Resource Slices", Cluster, "resource.k8s.io", "v1", "resourceslices", "ResourceSlice", false, named, crud, yamlTabs),
      D("nodes", "Nodes", Nodes, "", "v1", "nodes", "Node", false,
        [new("Name", "metadata.name"), new("Status", "status.conditions"), new("Roles", "metadata.labels"), new("Version", "status.nodeInfo.kubeletVersion"), new("Age", "metadata.creationTimestamp")],
        node, nodeTabs),
      D("pods", "Pods", Workloads, "", "v1", "pods", "Pod", true,
        [..std, new("Ready", "status.containerStatuses"), new("Restarts", "status.containerStatuses"), new("Status", "pod.status"), new("Node", "spec.nodeName"), new("CPU", "metrics.cpu"), new("Memory", "metrics.memory")],
        logs, podTabs),
      D("deployments", "Deployments", Workloads, "apps", "v1", "deployments", "Deployment", true,
        [..std, new("Ready", "status.readyReplicas"), new("Up-to-date", "status.updatedReplicas"), new("Available", "status.availableReplicas"), new("CPU", "metrics.cpu"), new("Memory", "metrics.memory")],
        new ResourceActions(CanScale: true, CanRestart: true, CanRollout: true), workloadTabs),
      D("controllerrevisions", "Controller Revisions", Workloads, "apps", "v1", "controllerrevisions", "ControllerRevision", true,
        [..std, new("Revision", "revision")],
        crud, yamlTabs),
      D("statefulsets", "StatefulSets", Workloads, "apps", "v1", "statefulsets", "StatefulSet", true,
        [..std, new("Ready", "status.readyReplicas"), new("CPU", "metrics.cpu"), new("Memory", "metrics.memory")],
        scale, workloadTabs),
      D("daemonsets", "DaemonSets", Workloads, "apps", "v1", "daemonsets", "DaemonSet", true,
        [..std, new("Desired", "status.desiredNumberScheduled"), new("Current", "status.currentNumberScheduled"), new("Ready", "status.numberReady"), new("CPU", "metrics.cpu"), new("Memory", "metrics.memory")],
        new ResourceActions(CanRestart: true), workloadTabs),
      D("replicasets", "ReplicaSets", Workloads, "apps", "v1", "replicasets", "ReplicaSet", true,
        [..std, new("Desired", "spec.replicas"), new("Current", "status.replicas"), new("Ready", "status.readyReplicas")],
        new ResourceActions(CanScale: true), workloadTabs),
      D("jobs", "Jobs", Workloads, "batch", "v1", "jobs", "Job", true,
        [..std, new("Completions", "spec.completions"), new("Duration", "status.startTime")],
        crud, workloadTabs),
      D("cronjobs", "CronJobs", Workloads, "batch", "v1", "cronjobs", "CronJob", true,
        [..std, new("Schedule", "spec.schedule"), new("Suspend", "spec.suspend"), new("Active", "status.active")],
        cron, yamlTabs),
      D("replicationcontrollers", "Replication Controllers", Workloads, "", "v1", "replicationcontrollers", "ReplicationController", true,
        [..std, new("Desired", "spec.replicas"), new("Current", "status.replicas")],
        new ResourceActions(CanScale: true), workloadTabs),
      D("resourceclaims", "Resource Claims", Workloads, "resource.k8s.io", "v1", "resourceclaims", "ResourceClaim", true, std, crud, yamlTabs),
      D("resourceclaimtemplates", "Resource Claim Templates", Workloads, "resource.k8s.io", "v1", "resourceclaimtemplates", "ResourceClaimTemplate", true, std, crud, yamlTabs),
      D("configmaps", "ConfigMaps", Config, "", "v1", "configmaps", "ConfigMap", true, std, crud, yamlTabs),
      D("podtemplates", "Pod Templates", Config, "", "v1", "podtemplates", "PodTemplate", true, std, crud, yamlTabs),
      D("secrets", "Secrets", Config, "", "v1", "secrets", "Secret", true,
        [..std, new("Type", "type")],
        crud, yamlTabs),
      D("resourcequotas", "Resource Quotas", Config, "", "v1", "resourcequotas", "ResourceQuota", true, std, crud, yamlTabs),
      D("limitranges", "Limit Ranges", Config, "", "v1", "limitranges", "LimitRange", true, std, crud, yamlTabs),
      D("horizontalpodautoscalers", "HPA", Config, "autoscaling", "v2", "horizontalpodautoscalers", "HorizontalPodAutoscaler", true,
        [..std, new("Min", "spec.minReplicas"), new("Max", "spec.maxReplicas"), new("Replicas", "status.currentReplicas")],
        crud, yamlTabs),
      D("poddisruptionbudgets", "Pod Disruption Budgets", Config, "policy", "v1", "poddisruptionbudgets", "PodDisruptionBudget", true, std, crud, yamlTabs),
      D("priorityclasses", "Priority Classes", Config, "scheduling.k8s.io", "v1", "priorityclasses", "PriorityClass", false, named, crud, yamlTabs),
      D("leases", "Leases", Config, "coordination.k8s.io", "v1", "leases", "Lease", true,
        [..std, new("Holder", "spec.holderIdentity")],
        crud, yamlTabs),
      D("runtimeclasses", "Runtime Classes", Config, "node.k8s.io", "v1", "runtimeclasses", "RuntimeClass", false, named, crud, yamlTabs),
      D("mutatingwebhookconfigurations", "Mutating Webhooks", Config, "admissionregistration.k8s.io", "v1", "mutatingwebhookconfigurations", "MutatingWebhookConfiguration", false, named, crud, yamlTabs),
      D("validatingwebhookconfigurations", "Validating Webhooks", Config, "admissionregistration.k8s.io", "v1", "validatingwebhookconfigurations", "ValidatingWebhookConfiguration", false, named, crud, yamlTabs),
      D("validatingadmissionpolicies", "Validating Admission Policies", Config, "admissionregistration.k8s.io", "v1", "validatingadmissionpolicies", "ValidatingAdmissionPolicy", false, named, crud, yamlTabs),
      D("validatingadmissionpolicybindings", "Validating Admission Policy Bindings", Config, "admissionregistration.k8s.io", "v1", "validatingadmissionpolicybindings", "ValidatingAdmissionPolicyBinding", false, named, crud, yamlTabs),
      D("mutatingadmissionpolicies", "Mutating Admission Policies", Config, "admissionregistration.k8s.io", "v1beta1", "mutatingadmissionpolicies", "MutatingAdmissionPolicy", false, named, crud, yamlTabs),
      D("mutatingadmissionpolicybindings", "Mutating Admission Policy Bindings", Config, "admissionregistration.k8s.io", "v1beta1", "mutatingadmissionpolicybindings", "MutatingAdmissionPolicyBinding", false, named, crud, yamlTabs),
      D("storageversionmigrations", "Storage Version Migrations", Config, "storagemigration.k8s.io", "v1beta1", "storageversionmigrations", "StorageVersionMigration", false, named, crud, yamlTabs),
      D("services", "Services", Network, "", "v1", "services", "Service", true,
        [..std, new("Type", "spec.type"), new("Status", "service.status"), new("Cluster IP", "spec.clusterIP"), new("External IP", "service.externalIP"), new("Ports", "spec.ports")],
        new ResourceActions(CanPortForward: true), serviceTabs),
      D("endpoints", "Endpoints", Network, "", "v1", "endpoints", "Endpoints", true, std, crud, yamlTabs),
      D("endpointslices", "Endpoint Slices", Network, "discovery.k8s.io", "v1", "endpointslices", "EndpointSlice", true, std, crud, yamlTabs),
      D("ingresses", "Ingresses", Network, "networking.k8s.io", "v1", "ingresses", "Ingress", true,
        [..std, new("Class", "spec.ingressClassName"), new("Hosts", "spec.rules")],
        crud, yamlTabs),
      D("ingressclasses", "Ingress Classes", Network, "networking.k8s.io", "v1", "ingressclasses", "IngressClass", false, named, crud, yamlTabs),
      D("networkpolicies", "Network Policies", Network, "networking.k8s.io", "v1", "networkpolicies", "NetworkPolicy", true, std, crud, yamlTabs),
      D("ipaddresses", "IP Addresses", Network, "networking.k8s.io", "v1", "ipaddresses", "IPAddress", false,
        [
          new("Name", "metadata.name"),
          new("Parent namespace", "spec.parentRef.namespace"),
          new("Parent name", "spec.parentRef.name"),
          new("Resource", "spec.parentRef.resource"),
          new("Group", "spec.parentRef.group"),
          new("Age", "metadata.creationTimestamp")
        ],
        crud, yamlTabs),
      D("servicecidrs", "Service CIDRs", Network, "networking.k8s.io", "v1", "servicecidrs", "ServiceCIDR", false, named, crud, yamlTabs),
      D("persistentvolumeclaims", "Persistent Volume Claims", Storage, "", "v1", "persistentvolumeclaims", "PersistentVolumeClaim", true,
        [..std, new("Status", "status.phase"), new("Volume", "spec.volumeName"), new("Capacity", "status.capacity.storage"), new("Storage Class", "spec.storageClassName")],
        new ResourceActions(CanResize: true), yamlTabs),
      D("persistentvolumes", "Persistent Volumes", Storage, "", "v1", "persistentvolumes", "PersistentVolume", false,
        [new("Name", "metadata.name"), new("Capacity", "spec.capacity.storage"), new("Access", "spec.accessModes"), new("Reclaim", "spec.persistentVolumeReclaimPolicy"), new("Status", "status.phase"), new("Claim", "pv.claim"), new("Age", "metadata.creationTimestamp")],
        new ResourceActions(CanRetain: true), yamlTabs),
      D("csidrivers", "CSI Drivers", Storage, "storage.k8s.io", "v1", "csidrivers", "CSIDriver", false, named, crud, yamlTabs),
      D("csinodes", "CSI Nodes", Storage, "storage.k8s.io", "v1", "csinodes", "CSINode", false, named, crud, yamlTabs),
      D("csistoragecapacities", "CSI Storage Capacities", Storage, "storage.k8s.io", "v1", "csistoragecapacities", "CSIStorageCapacity", true, std, crud, yamlTabs),
      D("volumeattachments", "Volume Attachments", Storage, "storage.k8s.io", "v1", "volumeattachments", "VolumeAttachment", false, named, crud, yamlTabs),
      D("volumeattributesclasses", "Volume Attributes Classes", Storage, "storage.k8s.io", "v1", "volumeattributesclasses", "VolumeAttributesClass", false, named, crud, yamlTabs),
      D(LonghornVolumesId, "Volumes", Longhorn, "longhorn.io", "v1beta2", "volumes", "Volume", true,
        [
          new("Name", "metadata.name"),
          new("Namespace", "metadata.namespace"),
          new("State", "status.state"),
          new("Robustness", "status.robustness"),
          new("Replicas", "spec.numberOfReplicas"),
          new("Node", "status.currentNodeID"),
          new("Size", "longhorn.size"),
          new("Age", "metadata.creationTimestamp")
        ],
        crud, yamlTabs),
      D(LonghornNodesId, "Nodes", Longhorn, "longhorn.io", "v1beta2", "nodes", "Node", true,
        [
          new("Name", "metadata.name"),
          new("Namespace", "metadata.namespace"),
          new("Scheduling", "longhorn.scheduling"),
          new("Disks", "longhorn.disks"),
          new("Age", "metadata.creationTimestamp")
        ],
        crud, yamlTabs),
      D(CnpgClustersId, "Clusters", CloudNativePG, "postgresql.cnpg.io", "v1", "clusters", "Cluster", true,
        [
          new("Name", "metadata.name"),
          new("Namespace", "metadata.namespace"),
          new("Phase", "status.phase"),
          new("Instances", "cnpg.instances"),
          new("Primary", "status.currentPrimary"),
          new("Age", "metadata.creationTimestamp")
        ],
        new ResourceActions(CanDelete: false, CanApply: false),
        yamlTabs),
      D("storageclasses", "Storage Classes", Storage, "storage.k8s.io", "v1", "storageclasses", "StorageClass", false,
        [new("Name", "metadata.name"), new("Provisioner", "provisioner"), new("Reclaim", "reclaimPolicy"), new("Age", "metadata.creationTimestamp")],
        new ResourceActions(CanRetain: true), yamlTabs),
      D("namespaces", "Namespaces", Namespaces, "", "v1", "namespaces", "Namespace", false,
        [new("Name", "metadata.name"), new("Status", "status.phase"), new("Age", "metadata.creationTimestamp")],
        crud, yamlTabs),
      D("events", "Events", Events, "", "v1", "events", "Event", true,
        [new("Type", "type"), new("Reason", "reason"), new("Object", "involvedObject.name"), new("Message", "message"), new("Namespace", "metadata.namespace"), new("Age", "metadata.creationTimestamp")],
        new ResourceActions(CanDelete: false, CanApply: false), [DetailTab.Overview, DetailTab.Yaml]),
      D("serviceaccounts", "Service Accounts", AccessControl, "", "v1", "serviceaccounts", "ServiceAccount", true, std, new ResourceActions(CanToken: true), yamlTabs),
      D("certificatesigningrequests", "Certificate Signing Requests", AccessControl, "certificates.k8s.io", "v1", "certificatesigningrequests", "CertificateSigningRequest", false,
        [new("Name", "metadata.name"), new("Signer", "spec.signerName"), new("Age", "metadata.creationTimestamp")],
        new ResourceActions(CanApprove: true), yamlTabs),
      D("clustertrustbundles", "Cluster Trust Bundles", AccessControl, "certificates.k8s.io", "v1beta1", "clustertrustbundles", "ClusterTrustBundle", false, named, crud, yamlTabs),
      D("roles", "Roles", AccessControl, "rbac.authorization.k8s.io", "v1", "roles", "Role", true, std, crud, yamlTabs),
      D("rolebindings", "Role Bindings", AccessControl, "rbac.authorization.k8s.io", "v1", "rolebindings", "RoleBinding", true, std, crud, yamlTabs),
      D("clusterroles", "Cluster Roles", AccessControl, "rbac.authorization.k8s.io", "v1", "clusterroles", "ClusterRole", false, named, crud, yamlTabs),
      D("clusterrolebindings", "Cluster Role Bindings", AccessControl, "rbac.authorization.k8s.io", "v1", "clusterrolebindings", "ClusterRoleBinding", false, named, crud, yamlTabs),
      D("customresourcedefinitions", "Definitions", CustomResources, "apiextensions.k8s.io", "v1", "customresourcedefinitions", "CustomResourceDefinition", false,
        [
          new("Resource", "spec.names.kind"),
          new("Group", "spec.group"),
          new("Version", "crd.storageVersion"),
          new("Scope", "spec.scope"),
          new("Age", "metadata.creationTimestamp")
        ],
        crud, yamlTabs),
      D("components", "Components", Dapr, "dapr.io", "v1alpha1", "components", "Component", true,
        [..std, new("Type", "spec.type")],
        crud, yamlTabs),
      D("configurations", "Configurations", Dapr, "dapr.io", "v1alpha1", "configurations", "Configuration", true, std, crud, yamlTabs),
      D("subscriptions", "Subscriptions", Dapr, "dapr.io", "v2alpha1", "subscriptions", "Subscription", true,
        [..std, new("Topic", "spec.topic"), new("Pubsub", "spec.pubsubname")],
        crud, yamlTabs),
      D("resiliencies", "Resiliency", Dapr, "dapr.io", "v1alpha1", "resiliencies", "Resiliency", true, std, crud, yamlTabs),
      D("httpendpoints", "HTTP Endpoints", Dapr, "dapr.io", "v1alpha1", "httpendpoints", "HTTPEndpoint", true, std, crud, yamlTabs),
    ];
  }

  private static ResourceDescriptor D(
    string id,
    string title,
    string section,
    string group,
    string version,
    string plural,
    string kind,
    bool namespaced,
    IReadOnlyList<ColumnSpec> columns,
    ResourceActions actions,
    IReadOnlyList<string> tabs) =>
    new(id, title, section, group, version, plural, kind, namespaced, columns, actions, tabs);
}
