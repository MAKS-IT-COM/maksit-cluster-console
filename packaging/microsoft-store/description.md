# Microsoft Store description

Paste into Partner Center → Store listings. Plain text only. URLs in these fields are not clickable; put the privacy policy, support site, and license in their own submission fields.

Leave **What's new in this version** empty on the first submission. Use it only for a later update (1,500 characters).

Hardware checkboxes (keyboard, memory, processor) stay in [CONTRIBUTING.md](../../CONTRIBUTING.md) under **System requirements (Properties)**.

## Short description

Shown at the top of the listing. The field allows 1,000 characters. Keep this under 270 so every Store view shows the full sentence.

```text
A desktop app for an existing Kubernetes cluster. Connect with your kubeconfig, then browse resources, apply YAML, follow pod logs, open a shell, and forward ports to this PC. Optional local chat can repair the cluster after you approve each change.
```

## Description

Required. Up to 10,000 characters. This text is different from the short description so the page does not repeat the same paragraph.

```text
Work on a Kubernetes cluster from Windows without keeping a browser dashboard or a terminal open for every task.

Connect with the kubeconfig you already use. What you can see and change is exactly what that identity is allowed to do. Click a catalog row to open that context in this app. The radio beside the row only writes kubectl current-context, so the command line can follow. This app does not read that field. Several contexts can stay open here while kubectl still has one current context.

Move through the cluster from one navigator: cluster and nodes, applications, workloads, config, network, storage, namespaces, events, Helm releases, Dapr, access control, and custom resources, including CRDs already installed. Several contexts can stay connected. Add a context with a token, a client certificate, or pasted k3s certificate data. The open table watches the API, and a label selector is sent with the list. Search filters the rows already loaded. Filters and sort stay per cluster. Selecting several rows runs a footer action on each of them. Custom resource tables show extra columns from the CRD. Open a row for YAML, events, and status. Hint explains the kind on screen.

Everyday changes stay in the same window. Apply or create YAML. If the API server rejects an apply patch, apply falls back to create or replace. Force-delete strips finalizers. Scale a Deployment, StatefulSet, ReplicaSet, or ReplicationController. Restart a Deployment, StatefulSet, or DaemonSet. Pause, resume, and undo a Deployment rollout. Trigger a CronJob. Cordon or uncordon a node. Drain asks first and shows which pods will move and which will remain, including anything a disruption budget would refuse. On a pod, follow the log, open a shell, attach output, or add a debug container. The image defaults to busybox and stays until the pod is recreated. ConfigMaps and Secrets edit as data keys. A Service shows Active, Pending, or Unreachable.

Resize a persistent volume claim, or change reclaim policy on a storage class and its volumes. Approve or deny a certificate signing request, and create a ServiceAccount token that is shown once. Port forwards are saved, restored when you reconnect, and a double-click on an active forward opens it in the browser.

CPU and memory appear on the overview, applications, and workload tables when the cluster has metrics-server. From the overview you can patch container CPU and memory against node capacity. Persistent volumes and claims open a file browser for viewing, editing, downloading, and uploading files. Helm and Dapr pages list objects that are already in the cluster.

Chat is optional and stays on this PC. It talks to a local Ollama server. The model is any one you have pulled. qwen3:8b is the default and is enough to read issues, YAML, logs, and events. Repairs work better with a larger model such as qwen3:14b, qwen3:32b, or qwen3-coder:30b. Turn on Allow the assistant to change the cluster and the same chat can restart a workload, delete a pod, scale, or apply YAML. Each of those waits for Approve or Reject. Reject leaves the cluster unchanged. Cluster data is not sent to a cloud AI service.

Your layout, open clusters, and saved port forwards are stored on this computer. Cluster Console talks to a cluster you already run.

You need 64-bit Windows, a kubeconfig that can reach a running cluster, and a network path to that cluster's API. Install Ollama locally only if you want Chat.
```

Product features (up to 20 bullets) are in [product-features.md](product-features.md). Keywords are in [keywords.md](keywords.md). Copyright, additional license terms, and Developed by are in [additional-info.md](additional-info.md).

## Additional system requirements

Optional text lines, separate from the Properties hardware table. Up to 11 minimum and 11 recommended. Each line is at most 200 characters.

Minimum:

```text
64-bit Windows 10 or Windows 11
A kubeconfig with permission to reach a running Kubernetes cluster
Network access to that cluster's API server
```

Recommended:

```text
metrics-server installed in the cluster, for CPU and memory columns
Ollama on this PC if you use Chat. Pull qwen3:14b or a larger model if the assistant may change the cluster
```

Category: Developer tools.
