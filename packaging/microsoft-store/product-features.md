# Microsoft Store product features

Paste into Partner Center → Store listings → **Product features**.

Short summaries of the product's key features. The Store shows them as a bulleted list. Up to 20 app features. Each feature is at most 200 characters. Paste the sentence only; Partner Center adds the bullet.

- Click a catalog row to open that cluster here. The radio only writes kubectl current-context for the command line. This app does not use that field. The overview shows CPU, memory, and pod counts.
- Group applications by instance and namespace from standard labels. CPU is a percent of cluster allocatable. Memory is summed from owned pods when metrics are available.
- Pod tables show ready, restarts, status, node, CPU, and memory. Follow logs, open a shell, attach output, or add a busybox debug container. Filters and sort stay per cluster.
- Ask any local Ollama model about the selection. Default qwen3:8b reads issues, YAML, logs, and events. Larger models such as qwen3:14b, qwen3:32b, or qwen3-coder:30b fit repairs better. No cloud AI.
- Allow the assistant to change the cluster. Restart, pod delete, scale, and YAML apply each wait for Approve or Reject in the chat. Reject leaves the cluster unchanged.
- Browse, edit, download, and upload files on a persistent volume or claim. Double-click a PV or PVC row to open the explorer.
- Open Dapr from the navigator: components, configurations, subscriptions, resiliency, HTTP endpoints, sidecars, and the control plane.
- Save port forwards, restore them on reconnect, and retarget a running pod. Double-click an active forward to open it in the browser. Rebind changes the local port.
- Add a kubeconfig context with a token, client certificate, or pasted k3s data. Several clusters can stay connected in this app while kubectl keeps a single current-context.
- Browse cluster, nodes, workloads, config, network, storage, namespaces, events, Helm, Dapr, access control, and custom resources, including CRDs installed in the cluster.
- The open table watches the API. A label selector is sent with the list. Column filters and sort stay on this PC, per cluster.
- View, apply, create, and delete resource YAML, including force delete. If the API server rejects an apply patch, apply falls back to create or replace.
- Scale and restart workloads. Pause, resume, and undo a Deployment rollout. Trigger a CronJob. Cordon, uncordon, and drain a node after a table of pods that will move or remain.
- Resize a persistent volume claim. Change reclaim policy on a storage class and its volumes.
- Approve or deny a certificate signing request. Create a ServiceAccount token and show it once.
- Custom resource tables use extra columns from the CRD additionalPrinterColumns.
- Helm charts list each installed chart version. A release shows history, user-supplied values, the rendered manifest, and a diff between two revisions.
- Show CPU and memory when the cluster has metrics-server.

The listing description and the other Store text fields are in [description.md](description.md).
