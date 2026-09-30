# Microsoft Store product features

Paste into Partner Center → Store listings → **Product features**.

Short summaries of the product's key features. The Store shows them as a bulleted list. Up to 20 app features. Each feature is at most 200 characters. Paste the sentence only; Partner Center adds the bullet.

- Set kubectl current-context from the catalog. The overview shows CPU, memory, and pod counts, and can patch container CPU and memory against node capacity.
- Group applications by instance and namespace from standard labels. CPU is a percent of cluster allocatable. Memory is summed from owned pods when metrics are available.
- Pod tables show ready, restarts, status, node, CPU, and memory. Follow logs, open a shell, and add an ephemeral debug container. Filters and sort stay per cluster.
- Ask a local Ollama model about the selected resource. Chat can read issues, YAML, logs, and events only. It cannot apply, restart, or delete, and it does not call a cloud AI API.
- Browse, edit, download, and upload files on a persistent volume or claim. Double-click a PV or PVC row to open the explorer.
- Open Dapr from the navigator: components, configurations, subscriptions, resiliency, HTTP endpoints, sidecars, and the control plane.
- Save port forwards, restore them on reconnect, and retarget a running pod. Double-click an active forward to open it in the browser. Rebind changes the local port.
- Pick a cluster from the kubeconfig catalog. Access follows that identity's permissions.
- Browse cluster, nodes, workloads, config, network, storage, namespaces, events, Helm, Dapr, access control, and custom resources, including CRDs installed in the cluster.
- The open table watches the API. A label selector is sent with the list. Column filters and sort stay on this PC, per cluster.
- View, apply, create, and delete resource YAML, including force delete. If the API server rejects an apply patch, apply falls back to create or replace.
- Scale, restart, pause, resume, and undo workload rollouts. Trigger a CronJob. Cordon and drain nodes.
- Resize a persistent volume claim. Change reclaim policy on a storage class and its volumes.
- Approve or deny a certificate signing request. Create a ServiceAccount token and show it once.
- Custom resource tables use extra columns from the CRD additionalPrinterColumns.
- List Helm releases that are already installed in the cluster.
- Show CPU and memory when the cluster has metrics-server.

The listing description and the other Store text fields are in [description.md](description.md).
