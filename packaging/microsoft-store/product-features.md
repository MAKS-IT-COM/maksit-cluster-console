# Microsoft Store product features

Paste into Partner Center → Store listings → **Product features**.

Short summaries of the product's key features. The Store shows them as a bulleted list. Up to 20 app features. Each feature is at most 200 characters. Paste the sentence only; Partner Center adds the bullet.

- Click a catalog row to open that cluster in this app. The radio only writes kubectl current-context. This app does not use that field. The overview shows CPU, memory, pod counts, and resource limits.
- Group applications by instance and namespace from standard labels. CPU is a percent of cluster allocatable. Memory is summed from owned pods when metrics are available.
- Pod tables show ready, restarts, status, node, CPU, and memory. Follow logs, open a shell, attach output, or add a busybox debug container. Filters and sort stay per cluster.
- Ask a local Ollama model about the cluster or the selection. On the overview, Analyze issues explains the current warnings and errors. The default model is qwen3:8b. No cloud AI.
- Allow the assistant to change the cluster. Restart, pod delete, scale, and YAML apply each wait for Approve or Reject. Reject leaves the cluster unchanged. A larger local model fits repairs better.
- Browse, edit, download, and upload files on a persistent volume or claim. Double-click a PV or PVC row to open the file browser.
- Open Dapr from the navigator: components, configurations, subscriptions, resiliency, HTTP endpoints, sidecars, and the control plane.
- Save port forwards, restore them on reconnect, and retarget a running pod. Double-click an active forward to open it in the browser. Rebind changes the local port.
- Add a kubeconfig context with a token, client certificate, pasted k3s data, or a username and password. Several clusters can stay connected here while kubectl keeps one current-context.
- Browse cluster, nodes, workloads, config, network, storage, namespaces, events, Helm, Dapr, access control, and custom resources. Longhorn and CloudNativePG appear when those APIs are installed.
- The open table watches the API. A label selector is sent with the list. Search filters loaded rows. Filters and sort stay per cluster. Shift or Ctrl selection runs a footer action on the selection.
- View, apply, create, and delete resource YAML, including force delete. If the API server rejects an apply patch, apply falls back to create or replace. JSON and YAML highlighting follows the document.
- Scale and restart workloads. Pause, resume, and undo a Deployment. Trigger a CronJob. Cordon and Drain stay hidden unless another Ready node remains. When AI is on, drain adds a short note.
- Resize a persistent volume claim. Change reclaim policy on a storage class and its volumes. The Images tab marks cached node images Used or Unused.
- ConfigMaps and Secrets have a Data tab with keys, a decoded preview, and apply. A Service status is Active, Pending, or Unreachable.
- Approve or deny a certificate signing request. Create a ServiceAccount token and show it once.
- Custom resource tables use extra columns from the CRD additionalPrinterColumns. Hint explains the kind on screen.
- Helm charts list each installed chart version. A release shows history, user-supplied values, the rendered manifest, and a diff between two revisions. Install and upgrade stay on the Helm CLI.
- CPU and memory appear when the cluster has metrics-server, on the overview and on application and workload tables.

The listing description and the other Store text fields are in [description.md](description.md).
