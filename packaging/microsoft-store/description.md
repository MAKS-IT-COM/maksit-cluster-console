# Microsoft Store description

Paste into Partner Center → Store listings. Plain text only. URLs in these fields are not clickable; put the privacy policy, support site, and license in their own submission fields.

Leave **What's new in this version** empty on the first submission. Use it only for a later update (1,500 characters).

Hardware checkboxes (keyboard, memory, processor) stay in [CONTRIBUTING.md](../../CONTRIBUTING.md) under **System requirements (Properties)**.

## Short description

Shown at the top of the listing. The field allows 1,000 characters. Keep this under 270 so every Store view shows the full sentence.

```text
A desktop app for an existing Kubernetes cluster. Connect with your kubeconfig, then browse resources, apply YAML, follow pod logs, open a shell, and forward ports to this PC.
```

## Description

Required. Up to 10,000 characters. This text is different from the short description so the page does not repeat the same paragraph.

```text
Work on a Kubernetes cluster from Windows without keeping a browser dashboard or a terminal open for every task.

Connect with the kubeconfig you already use. What you can see and change is exactly what that identity is allowed to do. Choosing a context in the catalog also sets kubectl's current context, so this app and the command line stay on the same cluster.

Move through the cluster from one navigator: cluster and nodes, applications, workloads, config, network, storage, namespaces, events, Helm releases, Dapr, access control, and custom resources, including CRDs already installed. The open table watches the API, and a label selector is sent with the list. Filters and sort stay per cluster. Custom resource tables show extra columns from the CRD. Open a row for YAML, events, and status.

Everyday changes stay in the same window. Apply or create YAML. If the API server rejects an apply patch, apply falls back to create or replace. Force-delete strips finalizers. Scale, restart, pause, resume, and undo workload rollouts, trigger a CronJob, and cordon or drain a node. On a pod, follow the log, open a shell, or add an ephemeral debug container.

Resize a persistent volume claim, or change reclaim policy on a storage class and its volumes. Approve or deny a certificate signing request, and create a ServiceAccount token that is shown once. Port forwards are saved, restored when you reconnect, and a double-click on an active forward opens it in the browser.

CPU and memory appear on the overview, applications, and workload tables when the cluster has metrics-server. From the overview you can patch container CPU and memory against node capacity. Persistent volumes and claims open a file browser for viewing, editing, downloading, and uploading files. Helm and Dapr pages list objects that are already in the cluster.

Chat is optional and stays on this PC. It talks to a local Ollama server and can read issues, YAML, logs, and events for the resource you selected. Apply, restart, and delete stay manual. Cluster data is not sent to a cloud AI service.

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
Ollama on this PC, only if you use Chat
```

Category: Developer tools.
