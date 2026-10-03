namespace MaksIT.ClusterConsole.Shared;

public static class ShoulderHints {
  public static string Text(string? id, ResourceDescriptor? descriptor) {
    var explanation = id is not null && ById.TryGetValue(id, out var specific)
      ? specific
      : descriptor is null ? "" : Default(descriptor);

    if (descriptor is null || explanation.Length == 0)
      return explanation;

    return explanation + "\n\n" + HowToRead(id, descriptor);
  }

  private static string HowToRead(string? id, ResourceDescriptor descriptor) {
    var columns = string.Join(" ", descriptor.Columns.Select(column => ExplainColumn(id, column.Header)));
    var tabs = string.Join(", ", descriptor.DetailTabs);
    var scope = descriptor.Namespaced
      ? "Each row is one object. Namespace is where it lives."
      : "Each row is one cluster-wide object.";
    var reading = $"{scope} {columns}";

    if (tabs.Length > 0)
      reading += $" Select a row. This pane shows {tabs}.";

    return reading;
  }

  private static string ExplainColumn(string? id, string header) {
    if (id is not null && ColumnText.TryGetValue(id + "|" + header, out var specific))
      return specific;

    return header switch {
      "Name" => "Name is the object name.",
      "Namespace" => "Namespace is where it lives.",
      "Age" => "Age is how long ago it was created.",
      _ => $"{header} is defined by this custom resource."
    };
  }

  private static string Default(ResourceDescriptor descriptor) {
    if (descriptor.Section == ResourceCatalog.CustomResources
        && descriptor.Id != "customresourcedefinitions") {
      var group = string.IsNullOrWhiteSpace(descriptor.Group) ? "its API group" : descriptor.Group;

      return $"{descriptor.Kind} is a custom resource from {group}. Kubernetes does not define what it means. The software that installed this API does. Select a row to read that object.";
    }

    return $"{descriptor.Kind} has no explanation yet.";
  }

  private static readonly Dictionary<string, string> ColumnText = new(StringComparer.Ordinal) {
    ["nodes|Status"] = "Status is Ready, or the condition that is failing.",
    ["nodes|Roles"] = "Roles are the control-plane or worker marks taken from node labels.",
    ["nodes|Version"] = "Version is the kubelet version.",
    ["pods|Ready"] = "Ready is ready containers over the total.",
    ["pods|Restarts"] = "Restarts is how many times containers in the pod have restarted.",
    ["pods|Status"] = "Status is the pod phase, or the reason a container is waiting.",
    ["pods|Node"] = "Node is the machine the pod is scheduled on.",
    ["pods|CPU"] = "CPU is current core usage and needs metrics-server.",
    ["pods|Memory"] = "Memory is current usage and needs metrics-server.",
    ["deployments|Ready"] = "Ready is how many pods are ready.",
    ["deployments|Up-to-date"] = "Up-to-date is how many pods run the current template.",
    ["deployments|Available"] = "Available is how many pods are ready and have been up long enough to count.",
    ["deployments|CPU"] = "CPU is the core usage of this deployment's pods and needs metrics-server.",
    ["deployments|Memory"] = "Memory is the usage of this deployment's pods and needs metrics-server.",
    ["controllerrevisions|Revision"] = "Revision is the snapshot number. A higher number is newer.",
    ["statefulsets|Ready"] = "Ready is how many members are ready.",
    ["statefulsets|CPU"] = "CPU is the core usage of this set's pods and needs metrics-server.",
    ["statefulsets|Memory"] = "Memory is the usage of this set's pods and needs metrics-server.",
    ["daemonsets|Desired"] = "Desired is how many nodes should run the pod.",
    ["daemonsets|Current"] = "Current is how many nodes have the pod scheduled.",
    ["daemonsets|Ready"] = "Ready is how many of those pods are ready.",
    ["daemonsets|CPU"] = "CPU is the core usage of this set's pods and needs metrics-server.",
    ["daemonsets|Memory"] = "Memory is the usage of this set's pods and needs metrics-server.",
    ["replicasets|Desired"] = "Desired is how many pods the set wants.",
    ["replicasets|Current"] = "Current is how many pods exist.",
    ["replicasets|Ready"] = "Ready is how many of those pods are ready.",
    ["jobs|Completions"] = "Completions is how many successful runs the job wants.",
    ["jobs|Duration"] = "Duration is how long the job has been running.",
    ["cronjobs|Schedule"] = "Schedule is the cron expression that starts the next job.",
    ["cronjobs|Suspend"] = "Suspend is true when new jobs will not start.",
    ["cronjobs|Active"] = "Active is how many jobs from this schedule have not finished.",
    ["replicationcontrollers|Desired"] = "Desired is how many pods the controller wants.",
    ["replicationcontrollers|Current"] = "Current is how many pods exist.",
    ["secrets|Type"] = "Type is the kind of secret, such as Opaque or kubernetes.io/tls.",
    ["horizontalpodautoscalers|Min"] = "Min is the smallest replica count it will set.",
    ["horizontalpodautoscalers|Max"] = "Max is the largest replica count it will set.",
    ["horizontalpodautoscalers|Replicas"] = "Replicas is the count it last set.",
    ["leases|Holder"] = "Holder is the identity that currently owns the lock.",
    ["services|Type"] = "Type is ClusterIP, NodePort, LoadBalancer, or ExternalName.",
    ["services|Status"] = "Status is Active when a load-balancer address is assigned, Pending when none is set, and Unreachable when a requested or BGP address is not the one the cluster reports.",
    ["services|Cluster IP"] = "Cluster IP is the address inside the cluster.",
    ["services|External IP"] = "External IP is the outside address, including a Cilium load-balancer annotation.",
    ["services|Ports"] = "Ports are the service port and the pod port it forwards to.",
    ["ingresses|Class"] = "Class is which ingress controller should implement this route.",
    ["ingresses|Hosts"] = "Hosts are the HTTP names the rules match.",
    ["ipaddresses|Name"] = "Name is the IP address itself.",
    ["ipaddresses|Parent namespace"] = "Parent namespace is where the object that owns this IP lives.",
    ["ipaddresses|Parent name"] = "Parent name is the object that owns this IP.",
    ["ipaddresses|Resource"] = "Resource is the kind of owner, usually a Service.",
    ["ipaddresses|Group"] = "Group is the API group of that owner.",
    ["persistentvolumeclaims|Status"] = "Status is the claim phase: Pending, Bound, or Lost.",
    ["persistentvolumeclaims|Volume"] = "Volume is the PersistentVolume that was bound.",
    ["persistentvolumeclaims|Capacity"] = "Capacity is the size of the bound volume.",
    ["persistentvolumeclaims|Storage Class"] = "Storage Class is the provisioner the claim asked for.",
    ["persistentvolumes|Capacity"] = "Capacity is the size of the disk.",
    ["persistentvolumes|Access"] = "Access is how pods may mount it, such as ReadWriteOnce.",
    ["persistentvolumes|Reclaim"] = "Reclaim is what happens to the disk when the claim is deleted.",
    ["persistentvolumes|Status"] = "Status is the volume phase: Available, Bound, or Released.",
    ["persistentvolumes|Claim"] = "Claim is the bound claim, as namespace and name.",
    ["longhorn-volumes|State"] = "State is attached or detached.",
    ["longhorn-volumes|Robustness"] = "Robustness is healthy, degraded, or faulted.",
    ["longhorn-volumes|Replicas"] = "Replicas is how many copies Longhorn should keep.",
    ["longhorn-volumes|Node"] = "Node is where the volume is attached.",
    ["longhorn-volumes|Size"] = "Size is the requested size.",
    ["longhorn-nodes|Scheduling"] = "Scheduling is whether Longhorn may place replicas on this node.",
    ["longhorn-nodes|Disks"] = "Disks is how many disks Longhorn sees, how much space is free, and how many are not ready.",
    ["cnpg-clusters|Phase"] = "Phase is the operator's summary of the database.",
    ["cnpg-clusters|Instances"] = "Instances is ready instances over the desired count.",
    ["cnpg-clusters|Primary"] = "Primary is the current primary pod.",
    ["storageclasses|Provisioner"] = "Provisioner is the driver that creates volumes of this class.",
    ["storageclasses|Reclaim"] = "Reclaim is the default for volumes this class creates. It does not change volumes that already exist.",
    ["namespaces|Status"] = "Status is the phase, usually Active.",
    ["events|Type"] = "Type is Normal or Warning.",
    ["events|Reason"] = "Reason is the short name of what happened, such as Started or Failed.",
    ["events|Object"] = "Object is the resource the event is about.",
    ["events|Message"] = "Message is the human-readable detail.",
    ["events|Age"] = "Age is when the event was last seen. Events expire.",
    ["certificatesigningrequests|Signer"] = "Signer is who is asked to sign the certificate.",
    ["customresourcedefinitions|Resource"] = "Resource is the kind this definition adds.",
    ["customresourcedefinitions|Group"] = "Group is the API group of that kind.",
    ["customresourcedefinitions|Version"] = "Version is the storage version the API server writes.",
    ["customresourcedefinitions|Scope"] = "Scope is Namespaced or Cluster.",
    ["components|Type"] = "Type is the building block, such as a state store or pub/sub.",
    ["subscriptions|Topic"] = "Topic is the name of the message stream.",
    ["subscriptions|Pubsub"] = "Pubsub is the Component that delivers that topic.",
    ["applications|Instance"] = "Instance is the shared app.kubernetes.io/instance, or the workload name.",
    ["applications|Managed by"] = "Managed by is the tool that installed it, such as Helm.",
    ["applications|Version"] = "Version is the app version label.",
    ["applications|Ready"] = "Ready is how many of the grouped workloads are available.",
    ["applications|CPU"] = "CPU is a percent of what the nodes can give.",
    ["applications|Memory"] = "Memory is the sum of owned pod usage when metrics-server is installed.",
    ["applications|Status"] = "Status is the summary phase of the group.",
    ["port-forwarding|Pod"] = "Pod is the pod the tunnel connects to.",
    ["port-forwarding|Local"] = "Local is the port on this machine.",
    ["port-forwarding|Remote"] = "Remote is the port in the pod.",
    ["port-forwarding|Status"] = "Status is Active while the tunnel is up.",
    ["helm-charts|Chart"] = "Chart is the package name.",
    ["helm-charts|Version"] = "Version is the chart version.",
    ["helm-charts|App"] = "App is the application version the chart declares.",
    ["helm-charts|Status"] = "Status is the install status when every install matches, and mixed when they do not.",
    ["helm-charts|Releases"] = "Releases is how many installs use this chart version.",
    ["helm-charts|Namespaces"] = "Namespaces is where those installs are.",
    ["helm-releases|Revision"] = "Revision is the latest stored release number.",
    ["helm-releases|Status"] = "Status is the Helm status, such as deployed or failed.",
    ["helm-releases|Chart"] = "Chart is the package name and version.",
    ["helm-releases|App"] = "App is the application version.",
    ["helm-releases|Updated"] = "Updated is when this revision was deployed."
  };

  private static readonly Dictionary<string, string> ById = new(StringComparer.Ordinal) {
    ["apiservices"] =
      "An APIService tells the cluster where one API group is served. Most of these point at an extension (an aggregated API) running as a Service. If it is not Available, that API looks missing from this console and from kubectl.",
    ["flowschemas"] =
      "A FlowSchema sorts API requests, not pods. It matches who is calling (a user, a group, or a service account) and what they are doing, then assigns that traffic a priority level. When the API server is busy, that level decides whose requests wait or get rejected. Open Priority Levels to see the queue the schema points at.",
    ["prioritylevelconfigurations"] =
      "A priority level is the queue or the exemption a FlowSchema assigns to API requests. It sets how many of those requests can run at once, and whether extra ones wait or fail. It does not rank pods.",
    ["deviceclasses"] =
      "A DeviceClass is a named kind of hardware a pod can ask for, such as a GPU. It describes which devices qualify. It is not a device on a particular node. Resource Slices list the devices that exist, and a Resource Claim is one pod's request.",
    ["resourceslices"] =
      "A ResourceSlice is a driver's inventory of devices it found, usually on one node. The scheduler reads these when a pod's Resource Claim needs a matching device.",
    ["nodes"] =
      "A node is one machine in the cluster. Pods run on it. Status is Ready, or the condition that is failing. Version is the kubelet. Cordon refuses new pods. Cordon and Drain stay hidden unless another Ready node can still take pods, so a single-node cluster has neither. Drain asks first, in a table: Outcome is Will move or Will remain, and Reason says why a pod stays, including a disruption budget that would refuse it. When AI is on, that window also includes a short note on whether it is safe to continue and which pods stay. Drain runs only after you accept. Cancel changes nothing. Uncordon still restores a node that is already cordoned. The Images tab marks cached images Used or Unused from the pods on that node.",
    ["pods"] =
      "A pod is the smallest thing Kubernetes runs: one or more containers that share a network and storage on one node. Ready is ready containers over the total. Status is the phase, or why a container is waiting. CPU and Memory need metrics-server. Logs and Terminal are for a container in that pod. Debug adds a temporary container beside them.",
    ["deployments"] =
      "A Deployment keeps a set of identical pods running and replaces them, a few at a time, when you change the pod template. It owns ReplicaSets, one per revision. Ready, Up-to-date, and Available count those pods. Scale changes how many run. Rollout history can undo the last template change.",
    ["controllerrevisions"] =
      "A ControllerRevision is a numbered, immutable snapshot of a DaemonSet or StatefulSet template. Kubernetes writes it so a rollout can be undone. You do not create these, and editing one does not change the running workload.",
    ["statefulsets"] =
      "A StatefulSet runs pods that keep a stable name and, usually, their own disk. pod-0 is always pod-0, which is what a database needs. A Deployment's pods are interchangeable and are the wrong tool for that. Scale changes how many members exist.",
    ["daemonsets"] =
      "A DaemonSet runs one pod on every matching node, and starts one on a node when the node joins. Log collectors and network agents use this. Desired is the number of nodes that should have the pod.",
    ["replicasets"] =
      "A ReplicaSet keeps a fixed number of pods that match its selector. A Deployment creates the ReplicaSets for you, one per revision. Creating one by hand is unusual.",
    ["jobs"] =
      "A Job runs pods until a task succeeds, then stops. Completions is how many successful runs it wants. It is not a service that should stay up. A CronJob is what starts a Job on a schedule.",
    ["cronjobs"] =
      "A CronJob starts a Job on a schedule, in cron syntax. Suspend stops new runs without deleting the CronJob. Active is a Job that has not finished.",
    ["replicationcontrollers"] =
      "A ReplicationController is the old way to keep N copies of a pod. ReplicaSets and Deployments replaced it. You will mostly see these in old clusters.",
    ["resourceclaims"] =
      "A ResourceClaim is one request for a device, such as a GPU, from device allocation (DRA). The scheduler binds it to a real device on a node before the pod can run. A Device Class says which devices qualify.",
    ["resourceclaimtemplates"] =
      "A ResourceClaimTemplate is a pattern, not a device request. A pod template references it so each replica gets its own Resource Claim, instead of all replicas sharing one claim.",
    ["configmaps"] =
      "A ConfigMap holds plain configuration for pods: files or settings, mounted as a volume or exposed as environment variables. It is not a Secret. Do not put passwords here. Anyone who can read the namespace can read it.",
    ["podtemplates"] =
      "A PodTemplate stores a pod spec for something else to copy. It does not run anything. Replication controllers are the usual reader. Workloads you deploy yourself use a Deployment or similar, which keeps its own template.",
    ["secrets"] =
      "A Secret holds small sensitive values: passwords, tokens, keys. Pods mount them as files or environment variables. The API stores them encoded, not encrypted, unless the cluster adds encryption at rest. Type says which kind of secret it is.",
    ["resourcequotas"] =
      "A ResourceQuota is a budget for one namespace: how much CPU, memory, and storage it may use, and how many objects of a kind it may create. Requests past the budget are rejected.",
    ["limitranges"] =
      "A LimitRange sets the default, minimum, and maximum CPU and memory for containers in one namespace. It fills in limits when a pod does not set them, and rejects a pod that asks for more than the maximum.",
    ["horizontalpodautoscalers"] =
      "A HorizontalPodAutoscaler (HPA) changes the replica count of a Deployment or similar when a metric, often CPU, moves off the target. Min and Max are the limits it will not cross. Replicas is the count it last set.",
    ["poddisruptionbudgets"] =
      "A PodDisruptionBudget tells a voluntary eviction, such as drain or a node upgrade, how many pods of an app must stay up. It does not stop a node crash or a delete you run yourself. Drain in this console lists the pods a budget would refuse.",
    ["priorityclasses"] =
      "A PriorityClass gives pods a number. When a node is full, the scheduler evicts lower-priority pods to place a higher-priority one. The pod chooses a class by name.",
    ["leases"] =
      "A Lease is a short-lived lock with a holder and a renew time. Leader election uses it so only one controller acts. Each node also holds a lease as its heartbeat. An old renew time means the holder has stopped renewing.",
    ["runtimeclasses"] =
      "A RuntimeClass names a container runtime on the node, such as the default runtime or a sandbox. A pod that sets this class is started with that handler. The class is the choice. The runtime software is installed on the node separately.",
    ["mutatingwebhookconfigurations"] =
      "A mutating webhook is an HTTPS service the API server calls while it is saving an object. The service may change the object, for example to inject a sidecar, and then the API server stores the result.",
    ["validatingwebhookconfigurations"] =
      "A validating webhook is an HTTPS service the API server calls before it saves an object. The service may reject the request. It does not change the object. If the service is down, the API call that needs it fails or is ignored, depending on failurePolicy.",
    ["validatingadmissionpolicies"] =
      "A ValidatingAdmissionPolicy is a rule, written in the cluster in CEL, that can reject an API request. It does the job of a validating webhook without a separate process. A policy does nothing until a Validating Admission Policy Binding selects it.",
    ["validatingadmissionpolicybindings"] =
      "A binding turns a ValidatingAdmissionPolicy on. It names the policy and which resources it applies to. Without a binding, the policy is only a stored rule.",
    ["mutatingadmissionpolicies"] =
      "A MutatingAdmissionPolicy is a rule, written in the cluster in CEL, that can change an object as it is saved. It does the job of a mutating webhook without a separate process. A binding is what turns the policy on.",
    ["mutatingadmissionpolicybindings"] =
      "A binding turns a MutatingAdmissionPolicy on for the resources it names. Without a binding, the policy is only a stored rule.",
    ["storageversionmigrations"] =
      "A StorageVersionMigration asks the API server to rewrite objects of one resource that are still stored in an older version, into the version it stores now. It is a one-time maintenance task after an API upgrade. It is not an app rollout.",
    ["services"] =
      "A Service is a stable address for a set of pods. Clients use its DNS name and cluster IP. The cluster forwards that traffic to pods that match the selector. Type NodePort or LoadBalancer also opens an address outside the cluster. Status is Active when a load-balancer address is assigned, Pending when none is set, and Unreachable when a requested or BGP address is not the one the cluster reports.",
    ["endpoints"] =
      "An Endpoints object is the older list of pod IPs behind one Service. EndpointSlice replaced it. Kubernetes still writes this for compatibility. Editing it by hand is lost the next time the endpoints controller runs.",
    ["endpointslices"] =
      "An EndpointSlice is a page of the pod addresses behind a Service, and whether each is ready. A large Service is split across several slices. This is the list the cluster actually load-balances to.",
    ["ingresses"] =
      "An Ingress is a set of HTTP routes: which host name and path go to which Service. It does nothing by itself. An ingress controller in the cluster reads it and configures a proxy. Class names which controller.",
    ["ingressclasses"] =
      "An IngressClass names an ingress controller. An Ingress picks a class. The controller that registered that class is the one that will implement the routes.",
    ["networkpolicies"] =
      "A NetworkPolicy is a firewall for pods in one namespace. It selects pods and says which traffic may reach them or leave them. It works only when the cluster network (the CNI) enforces it. With no policy, pods accept traffic.",
    ["ipaddresses"] =
      "An IPAddress records that one IP is already given to a parent, usually a Service, so the allocator does not hand it out again. The name is the address. Parent is the object that owns it.",
    ["servicecidrs"] =
      "A ServiceCIDR is a range of addresses the cluster may assign as Service cluster IPs. More than one range can exist. This is not a pod network.",
    ["persistentvolumeclaims"] =
      "A PersistentVolumeClaim is a request for disk: a size and a Storage Class. A pod mounts the claim. Kubernetes binds it to a PersistentVolume, which is the actual disk. Status is the phase. Double-click a bound claim to browse files. Resize changes the request when the storage class allows it.",
    ["persistentvolumes"] =
      "A PersistentVolume is a piece of storage in the cluster, backed by a disk or a file share. One claim binds to it. Reclaim is what happens to that disk when the claim is deleted: keep it (Retain) or let the driver remove it (Delete). Double-click to browse files.",
    ["csidrivers"] =
      "A CSIDriver describes one storage driver to Kubernetes: whether a volume must be attached before a pod can use it, and how the driver wants pod information. The driver software itself runs as pods. This object is only the registration.",
    ["csinodes"] =
      "A CSINode is one Kubernetes node's report of which CSI drivers are installed there and how to reach them. The name matches the node. It is not a disk.",
    ["csistoragecapacities"] =
      "A CSIStorageCapacity reports how much space one storage driver still has in one place, for one storage class. The scheduler uses it so it does not put a pod on a node where the volume cannot be created.",
    ["volumeattachments"] =
      "A VolumeAttachment is the request to attach one volume to one node, and whether the CSI driver finished that attach. The attach-detach controller writes these. A stuck one means the disk did not appear on the node.",
    ["volumeattributesclasses"] =
      "A VolumeAttributesClass is a named set of parameters a storage driver can change on an existing volume, such as IOPS. A claim switches to a class to ask for that change, instead of being recreated.",
    ["storageclasses"] =
      "A StorageClass is a way to create volumes: which driver, and with which parameters. A PersistentVolumeClaim names a class, and the driver creates a volume for it. Reclaim is the default for volumes this class creates. Changing reclaim here does not change volumes that already exist.",
    [ResourceCatalog.LonghornVolumesId] =
      "A Longhorn volume is a replicated disk managed by Longhorn, usually the disk behind a PersistentVolume. State is attached to a node or detached. Robustness is healthy, degraded, or faulted. Replicas is how many copies Longhorn should keep. Size is the requested size.",
    [ResourceCatalog.LonghornNodesId] =
      "A Longhorn node is Longhorn's record of one machine and the disks it may store replicas on. It is not the Kubernetes node, though the name matches. Scheduling is whether Longhorn may place new replicas there. Disks is how many disks it sees, how much space is free, and how many are not ready.",
    [ResourceCatalog.CnpgClustersId] =
      "A CloudNativePG cluster is one Postgres database run by the CloudNativePG operator: the instances, which pod is primary, and the failover state. Phase is the operator's summary. Instances is ready instances over the desired count. This screen does not delete or apply the cluster.",
    ["namespaces"] =
      "A namespace is a naming folder. Two objects in different namespaces may share a name. It does not, by itself, stop one app from calling another. Network policies and roles do that. Selecting a row here does not filter the other tables. Use the namespace column filter for that.",
    ["events"] =
      "An Event is a short note from the cluster about something that happened to an object, such as a failed pull or a started container. Type is Normal or Warning. They expire. This is not a log and not a full history.",
    ["serviceaccounts"] =
      "A ServiceAccount is the identity a pod uses when it calls the Kubernetes API. It is not a person and not a login to this console. The token action creates a token for that account and shows it once.",
    ["certificatesigningrequests"] =
      "A certificate signing request asks a signer in the cluster to sign a certificate. Signer is who should sign it. Approve or deny from the footer. The decision is stored on the request. Kubernetes does not install the certificate into an app for you.",
    ["clustertrustbundles"] =
      "A ClusterTrustBundle is a set of CA certificates published for the whole cluster, so workloads can trust a private authority. Pods can project the bundle as a file. It is not a certificate for one Service.",
    ["roles"] =
      "A Role is a list of permissions inside one namespace: which actions are allowed on which resource types. It grants nothing until a Role Binding attaches it to a user, group, or service account.",
    ["rolebindings"] =
      "A RoleBinding grants a Role, or a ClusterRole, to subjects in one namespace. The subjects are users, groups, or service accounts. Outside that namespace, this binding does not apply.",
    ["clusterroles"] =
      "A ClusterRole is a list of permissions that can apply to the whole cluster, including cluster-scoped resources. A ClusterRoleBinding grants it everywhere. A RoleBinding can also grant it, but then only inside that one namespace.",
    ["clusterrolebindings"] =
      "A ClusterRoleBinding grants a ClusterRole to subjects in every namespace. It cannot grant a namespaced Role. Use a Role Binding when the access should stop at one namespace.",
    ["customresourcedefinitions"] =
      "A CustomResourceDefinition teaches the API server a new kind of object, such as a database or a certificate. After it is established, those objects appear under Custom Resources. This row is the definition, not one of those objects.",
    ["components"] =
      "A Dapr Component is one building block and how to reach it: a state store, a pub/sub broker, a secret store, or a binding. Type is the kind of block. The app uses the Dapr API. This object holds the address and credentials.",
    ["configurations"] =
      "A Dapr Configuration is sidecar settings for apps that reference it: tracing, metrics, and which features are on. It is not a Kubernetes ConfigMap, and it is not one building block. Components are the building blocks.",
    ["subscriptions"] =
      "A Dapr Subscription says that an app wants messages from a topic. Topic is the name. Pubsub is which broker Component delivers it. The sidecar receives the messages and posts them to the app.",
    ["resiliencies"] =
      "A Dapr Resiliency policy sets retries, timeouts, and circuit breakers for calls an app makes through the sidecar. The app references the policy. It does not replace a Kubernetes probe.",
    ["httpendpoints"] =
      "A Dapr HTTPEndpoint names an external HTTP service the sidecar may call for an app, including retries from a resiliency policy. Apps call it through Dapr instead of using the URL themselves.",
    [ResourceCatalog.ApplicationsId] =
      "An Application here is not a Kubernetes object. It is this console grouping workloads that share app.kubernetes.io/instance, or the same name, in one namespace. Ready is how many of those workloads are available. CPU is a percent of what the nodes can give. Memory is the sum of owned pod usage when metrics-server is installed. Select a row for those workloads, their pods, logs, and a terminal.",
    [ResourceCatalog.PortForwardingId] =
      "A port forward is a tunnel this console opened from your machine to a port on a pod. It is not a Service, and it exists only while this console is connected. Local is the port on this machine. Remote is the port in the pod. Status is Active while the tunnel is up. Double-click an Active row to open it in the browser. Rebind changes the local port. These tunnels are restored when you reconnect.",
    [ResourceCatalog.HelmChartsId] =
      "A chart is a packaged Kubernetes application. This screen is not a catalog of charts you could install. Each row is one chart version already installed in this cluster. Releases is how many installs use that version. Namespaces is where they are. Status is the install status when they all match, and mixed when they do not. Select a row to see those installs. Open Helm → Releases for one install's history, values, and manifest. Install and upgrade stay on the Helm CLI.",
    [ResourceCatalog.HelmReleasesId] =
      "A Helm release is one install of a chart in one namespace. Helm stores each revision as a Secret. This screen lists those installs. Revision is the latest stored one. Chart is the package and its version. Select a row for history, the values you supplied, the rendered manifest, and a diff between two revisions. Helm → Charts groups those installs by chart version. Install and upgrade stay on the Helm CLI.",
    [ResourceCatalog.DaprSidecarsId] =
      "A Dapr sidecar is the daprd container injected into an application pod. The app talks to it on localhost, and the sidecar talks to Components such as state stores and pub/sub. This list is those pods, not the building blocks. Status, node, logs, and a terminal are for that pod.",
    [ResourceCatalog.DaprControlPlaneId] =
      "The Dapr control plane is the set of system processes in the dapr namespace: the injector, the operator, the placement service, and sentry. These are not application sidecars. Status and restarts tell you whether that system is up."
  };
}
