# What's new

Short notes shown in the app after an upgrade. The full history, including maintainer notes, is [CHANGELOG.md](CHANGELOG.md).

## [0.8.6] - 2026-10-03

- The drain dialog can show a short AI note on whether it is safe to continue, which pods will move, and which stay.
- Cordon and Drain stay hidden unless another Ready node can take pods.

## [0.8.5] - 2026-10-01

- Helm Charts lists each chart version installed in the cluster, from the release secrets, instead of an empty page.
- Helm releases show revision history, user-supplied values, the rendered manifest, and a diff between two revisions.
- Drain asks before it changes the node. The dialog is a table: teal Will move, amber Will remain, including anything a PodDisruptionBudget would refuse. Long text wraps and the row grows. Drain continues only after you accept. Cancel leaves the node as it is. A denied eviction is not deleted.
- Longhorn volumes and nodes, and CloudNativePG clusters, appear in the navigator when those APIs are installed.
- Cluster issues include LoadBalancer services that are pending or unreachable, and PersistentVolumeClaims that are Pending.
- Hint explains the kind on screen.

## [0.8.3] - 2026-09-29

- Pod Terminal opens a live shell. Attach follows a container's output. Debug adds an ephemeral container.
- Deployments, StatefulSets, and DaemonSets can pause, resume, show rollout history, and undo.
- PersistentVolumeClaims can be resized. Storage classes and volumes can switch reclaim policy between Delete and Retain. A class change is delete-and-recreate; the dialog explains the gap for new claims.
- Certificate signing requests can be approved or denied. A ServiceAccount token is created and shown once.
- Node details include an Images tab. Cached images are marked Used or Unused from the pods on that node.
- The navigator adds admission policies, CSI objects, API services, flow schemas, priority levels, and dynamic resource allocation types. Custom resource tables use the CRD additional printer columns.
- A list can send a label selector with the API request.
- YAML apply falls back to create or replace when the API server rejects an apply patch.
- Settings → AI sets the local Ollama endpoint and model. Chat stays read-only unless the agent is enabled; restart, pod delete, scale, and apply then wait for a confirmation.
- Help → About shows the product, license, and contact.
- YAML highlighting covers the whole document.

## [0.8.1] - 2026-09-27

- Windows setup keeps a single desktop shortcut across upgrades.

## [0.8.0] - 2026-09-26

- Help → Logs shows the app log and crash reports, and an unhandled error opens a copyable debug window instead of closing the app.
- YAML view, volume files, and ConfigMap/Secret values highlight as JSON or YAML.
- Services table includes Status: Active, Pending, or Unreachable.
- Applying YAML for a LoadBalancer service no longer crashes when the last-applied annotation contains a newline.

## [0.6.0] - 2026-08-30

- Footer actions that can run independently apply to every selected table row, not only the current row.

## [0.5.0] - 2026-08-22

- Applications shows live CPU and memory when metrics-server is available.
- Deployments, StatefulSets, and DaemonSets include CPU and memory columns.

## [0.4.2] - 2026-08-21

- Switching kubectl current-context no longer writes extra kubeconfig backup files.
- ConfigMap and Secret editors accept multiline values.

## [0.4.1] - 2026-08-21

- Staying on a resource table picks up cluster changes on the automatic refresh.
- Logs Follow tails new pod lines, and turning Follow off no longer mixes the live stream into the log text.

## [0.4.0] - 2026-08-20

- Resource table filters, sort, column widths, and search are kept per cluster.

## [0.3.0] - 2026-08-20

- The catalog radio sets kubectl current-context. The asterisk marker and Use for kubectl button are gone.

## [0.2.1] - 2026-08-20

- Resource table IP columns sort as addresses.

## [0.2.0] - 2026-08-20

- Port-forwards are listed under Network → Port Forwarding, can be restored after a reconnect, and open in the browser on double-click.
- Local and Remote labels make the two port fields explicit.
- Resource table footer and edit bars show only for a selected row that can use them.

## [0.1.0] - 2026-08-19

- First public release of MaksIT Cluster Console: catalog, navigator, tables, YAML apply, logs, exec, port-forward, Helm, Dapr, and a read-only local Ollama Chat tab.
