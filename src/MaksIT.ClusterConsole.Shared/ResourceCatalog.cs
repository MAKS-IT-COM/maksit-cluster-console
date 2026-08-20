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
  public const string AccessControl = "Access Control";
  public const string CustomResources = "Custom Resources";

  public const string OverviewId = "overview";
  public const string WorkloadsOverviewId = "workloads-overview";
  public const string ApplicationsId = "applications";
  public const string PortForwardingId = "port-forwarding";
  public const string HelmChartsId = "helm-charts";
  public const string HelmReleasesId = "helm-releases";
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
    Namespaces,
    Events,
    Helm,
    Dapr,
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
      new("Status", "status.phase"),
      new("Age", "metadata.creationTimestamp")
    ],
    new ResourceActions(CanScale: false, CanRestart: false, CanApply: false),
    ["Overview", "YAML", "Events", "Pods", "Logs", "Terminal"]);

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
    ["Overview"]);

  public static ResourceDescriptor? Find(string id) =>
    id switch {
      ApplicationsId => ApplicationsDescriptor,
      PortForwardingId => PortForwardingDescriptor,
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

    return new ResourceDescriptor(
      $"crd:{group}/{version}/{plural}",
      kind,
      CustomResources,
      group,
      version,
      plural,
      kind,
      namespaced,
      [
        new("Name", "metadata.name"),
        new("Namespace", "metadata.namespace"),
        new("Age", "metadata.creationTimestamp")
      ],
      new ResourceActions(),
      ["Overview", "YAML", "Events"]);
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
    var yamlTabs = new[] { "Overview", "YAML", "Events" };
    var podTabs = new[] { "Overview", "YAML", "Events", "Logs", "Terminal" };
    var workloadTabs = new[] { "Overview", "YAML", "Events", "Pods", "Logs", "Terminal" };
    var serviceTabs = new[] { "Overview", "YAML", "Events", "Pods" };
    var crud = new ResourceActions();
    var scale = new ResourceActions(CanScale: true, CanRestart: true);
    var logs = new ResourceActions(CanLogs: true, CanExec: true, CanPortForward: true);
    var node = new ResourceActions(CanCordon: true, CanDrain: true);
    var cron = new ResourceActions(CanTrigger: true);

    ColumnSpec[] std = [new("Name", "metadata.name"), new("Namespace", "metadata.namespace"), new("Age", "metadata.creationTimestamp")];
    ColumnSpec[] named = [new("Name", "metadata.name"), new("Age", "metadata.creationTimestamp")];

    return [
      D("nodes", "Nodes", Nodes, "", "v1", "nodes", "Node", false,
        [new("Name", "metadata.name"), new("Status", "status.conditions"), new("Roles", "metadata.labels"), new("Version", "status.nodeInfo.kubeletVersion"), new("Age", "metadata.creationTimestamp")],
        node, yamlTabs),
      D("pods", "Pods", Workloads, "", "v1", "pods", "Pod", true,
        [..std, new("Ready", "status.containerStatuses"), new("Restarts", "status.containerStatuses"), new("Status", "pod.status"), new("Node", "spec.nodeName"), new("CPU", "metrics.cpu"), new("Memory", "metrics.memory")],
        logs, podTabs),
      D("deployments", "Deployments", Workloads, "apps", "v1", "deployments", "Deployment", true,
        [..std, new("Ready", "status.readyReplicas"), new("Up-to-date", "status.updatedReplicas"), new("Available", "status.availableReplicas")],
        scale, workloadTabs),
      D("statefulsets", "StatefulSets", Workloads, "apps", "v1", "statefulsets", "StatefulSet", true,
        [..std, new("Ready", "status.readyReplicas")],
        scale, workloadTabs),
      D("daemonsets", "DaemonSets", Workloads, "apps", "v1", "daemonsets", "DaemonSet", true,
        [..std, new("Desired", "status.desiredNumberScheduled"), new("Current", "status.currentNumberScheduled"), new("Ready", "status.numberReady")],
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
      D("configmaps", "ConfigMaps", Config, "", "v1", "configmaps", "ConfigMap", true, std, crud, yamlTabs),
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
      D("services", "Services", Network, "", "v1", "services", "Service", true,
        [..std, new("Type", "spec.type"), new("Cluster IP", "spec.clusterIP"), new("External IP", "service.externalIP"), new("Ports", "spec.ports")],
        new ResourceActions(CanPortForward: true), serviceTabs),
      D("endpoints", "Endpoints", Network, "", "v1", "endpoints", "Endpoints", true, std, crud, yamlTabs),
      D("endpointslices", "Endpoint Slices", Network, "discovery.k8s.io", "v1", "endpointslices", "EndpointSlice", true, std, crud, yamlTabs),
      D("ingresses", "Ingresses", Network, "networking.k8s.io", "v1", "ingresses", "Ingress", true,
        [..std, new("Class", "spec.ingressClassName"), new("Hosts", "spec.rules")],
        crud, yamlTabs),
      D("ingressclasses", "Ingress Classes", Network, "networking.k8s.io", "v1", "ingressclasses", "IngressClass", false, named, crud, yamlTabs),
      D("networkpolicies", "Network Policies", Network, "networking.k8s.io", "v1", "networkpolicies", "NetworkPolicy", true, std, crud, yamlTabs),
      D("persistentvolumeclaims", "Persistent Volume Claims", Storage, "", "v1", "persistentvolumeclaims", "PersistentVolumeClaim", true,
        [..std, new("Status", "status.phase"), new("Volume", "spec.volumeName"), new("Capacity", "status.capacity.storage"), new("Storage Class", "spec.storageClassName")],
        crud, yamlTabs),
      D("persistentvolumes", "Persistent Volumes", Storage, "", "v1", "persistentvolumes", "PersistentVolume", false,
        [new("Name", "metadata.name"), new("Capacity", "spec.capacity.storage"), new("Access", "spec.accessModes"), new("Reclaim", "spec.persistentVolumeReclaimPolicy"), new("Status", "status.phase"), new("Claim", "pv.claim"), new("Age", "metadata.creationTimestamp")],
        crud, yamlTabs),
      D("storageclasses", "Storage Classes", Storage, "storage.k8s.io", "v1", "storageclasses", "StorageClass", false,
        [new("Name", "metadata.name"), new("Provisioner", "provisioner"), new("Reclaim", "reclaimPolicy"), new("Age", "metadata.creationTimestamp")],
        crud, yamlTabs),
      D("namespaces", "Namespaces", Namespaces, "", "v1", "namespaces", "Namespace", false,
        [new("Name", "metadata.name"), new("Status", "status.phase"), new("Age", "metadata.creationTimestamp")],
        crud, yamlTabs),
      D("events", "Events", Events, "", "v1", "events", "Event", true,
        [new("Type", "type"), new("Reason", "reason"), new("Object", "involvedObject.name"), new("Message", "message"), new("Namespace", "metadata.namespace"), new("Age", "metadata.creationTimestamp")],
        new ResourceActions(CanDelete: false, CanApply: false), ["Overview", "YAML"]),
      D("serviceaccounts", "Service Accounts", AccessControl, "", "v1", "serviceaccounts", "ServiceAccount", true, std, crud, yamlTabs),
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
