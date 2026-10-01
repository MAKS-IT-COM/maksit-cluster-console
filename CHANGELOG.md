# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.8.5] - 2026-10-01

### Added

- Helm Charts lists each chart version installed in the cluster, from the release secrets, instead of an empty page.
- Helm releases show revision history, user-supplied values, the rendered manifest, and a diff between two revisions.
- Drain asks before it changes the node. The dialog is a table: teal Will move, amber Will remain, including anything a PodDisruptionBudget would refuse. Long text wraps and the row grows. Drain continues only after you accept. Cancel leaves the node as it is. A denied eviction is not deleted.
- Longhorn volumes and nodes, and CloudNativePG clusters, appear in the navigator when those APIs are installed.
- Cluster issues include LoadBalancer services that are pending or unreachable, and PersistentVolumeClaims that are Pending.
- Hint explains the kind on screen. After an upgrade, What's New lists additions since the version you last opened.

### Changed

- The README and the Microsoft Store listing describe the console's actions, larger local Ollama models for repairs, and cluster changes that wait for approval.
- The catalog radio writes kubectl `current-context` for the command line. This app connects the row you click and does not read that field.

## [0.8.4] - 2026-09-30

### Added

- Flathub submission documents are in `packaging/flathub`.
- Microsoft Store text for the `runFullTrust` capability is in `packaging/microsoft-store/restricted-capabilities.md`.

### Changed

- Client and UI code is grouped by area: cluster session, kubeconfig, terminal, charts, editor, grid, and the shell, connection, and storage windows.
- AppStream includes screenshots, an age rating, and the kubeconfig step after install.

## [0.8.3] - 2026-09-29

### Added

- Pod Terminal opens a live shell. Attach follows a container's output. Debug adds an ephemeral container.
- Deployments, StatefulSets, and DaemonSets can pause, resume, show rollout history, and undo.
- PersistentVolumeClaims can be resized. Storage classes and volumes can switch reclaim policy between Delete and Retain. A class change is delete-and-recreate; the dialog explains the gap for new claims.
- Certificate signing requests can be approved or denied. A ServiceAccount token is created and shown once.
- Node details include an Images tab. Cached images are marked Used or Unused from the pods on that node.
- The navigator adds admission policies, CSI objects, API services, flow schemas, priority levels, and dynamic resource allocation types. Custom resource tables use the CRD `additionalPrinterColumns`.
- A list can send a label selector with the API request.
- YAML apply falls back to create or replace when the API server rejects an apply patch.
- Settings → AI sets the local Ollama endpoint and model. Chat stays read-only unless the agent is enabled; restart, pod delete, scale, and apply then wait for a confirmation.
- Help → About shows the product, license, and contact.

### Fixed

- YAML highlighting covers the whole document. Long values no longer drop color partway through a line, and later lines stay colored when you switch resources or open the YAML tab.

### Changed

- The README lists every feature under Features. Screenshots stay with the features that have one.
- Microsoft Store product features and the listing description match that list.

## [0.8.2] - 2026-09-28

### Added

- Microsoft Store package is a full-trust x64 MSIX (`MAKS-IT.ClusterConsole`). The WiX setup exe remains the GitHub installer. The MSIX is not a GitHub release asset.

## [0.8.1] - 2026-09-27

### Fixed

- Windows setup keeps a single desktop shortcut across upgrades. An icon that is already there stays when the checkbox is off, and a checked box no longer leaves a second `.lnk` or a `(2)` copy.

## [0.8.0] - 2026-09-26

### Added

- Help → Logs shows the app log and crash reports, and an unhandled error opens a copyable debug window instead of closing the app.
- YAML view, volume files, and ConfigMap/Secret values highlight as JSON or YAML. A data key or file name uses its extension (`.json`, `.yaml`, `.yml`); a key without one stays plain. The resource YAML tab is always YAML.
- Services table includes Status: Active when the load-balancer address is assigned, Pending when none is set, and Unreachable when a BGP or requested address is not the one in `status.loadBalancer.ingress` (or Cilium IPAM reports it unsatisfied).

### Fixed

- Applying YAML for a LoadBalancer service (for example a Cilium BGP service whose `last-applied-configuration` annotation contains a newline) no longer crashes. Multiline and quoted strings are written as valid YAML.

## [0.7.0] - 2026-09-12

### Added

- macOS GitHub Release assets for all Macs: `osx-arm64` (Apple Silicon) and `osx-x64` (Intel) DMGs, built on `macos-latest` when a release is published. Unsigned until Apple notarization (first launch: Open from the context menu).

### Changed

- Linux Flatpak app id is lowercase `com.maks_it.clusterconsole`. Uninstall the old `com.maks_it.ClusterConsole` id before installing a new bundle. AppStream and the desktop file live in `data/`. Avalonia 12.1.2.

### Fixed

- Linux Flatpak GNOME app icon uses X11/XWayland (`UsePlatformDetect` only). Avalonia 12.1.2 native Wayland still hangs on GNOME `xdg_toplevel.configure(0, 0)`.
- Flatpak AppStream version is the same shared release version as zip and MSI (`DotNetReleaseVersion` from the UI csproj / `Directory.Build.props`), so `flatpak info` matches the bundle file name.

## [0.6.2] - 2026-09-03

### Changed

- Operator settings (layout, port-forwards, Chat) are written to `%AppData%/MaksIT/Cluster Console/settings.json` (WiX `installFolderName`). Shipped `appsettings.json` next to the exe keeps host logging only; a leftover `Configuration` block is copied once into the user file.
- Synced RepoUtils: ContainerRegistry JSON catalog (PascalCase Harbor / InCluster keys) and `RepoUtilsSecrets` pack slots instead of per-plugin `*Secret` env names.

## [0.6.1] - 2026-08-30

### Changed

- Windows setup is per-machine: install path `C:\Program Files\MaksIT\Cluster Console`, Start Menu folder **MaksIT**, shortcuts named **Cluster Console**. The bootstrapper install-folder box expands Program Files instead of showing a raw `[ProgramFiles6432Folder]` token.

## [0.6.0] - 2026-08-30

### Fixed

- Footer actions that can run independently now apply to every selected table row (Restart, Scale, Delete, Force delete, Force delete namespace, Cordon, Uncordon, Drain, Trigger, Stop port-forward), not only the current row.

### Changed

- Dark UI uses the MAKS.IT origami blues (`#33A5CF` highlight, `#006199` accent). Window/installer/Flatpak icon is a faceted cluster of nodes with a console chevron (not the brand M). GitHub release assets are siblings: portable `maksit-cluster-console-{version}.zip` (win-x64 only), Windows setup `maksit-cluster-console-{version}.exe`, and `maksit-cluster-console-{version}.flatpak`. The installer and Flatpak are not packed inside the zip. On Windows the Flatpak bundle is built via WSL Debian.

## [0.5.1] - 2026-08-24

### Added

- Release packaging can resolve enabled plugin `*Secret` values from **MaksIT Vault** when `useVault` is set in `scriptSettings.json` (PowerShell module or HTTP API; `Shared` application fallback).

### Changed

- Copying resource-table rows includes column headers, so Ctrl+C pastes into Excel as a TSV table.

### Fixed

- Copying resource-table rows no longer pastes empty quoted cells after CPU/memory tooltips moved columns to templates.

## [0.5.0] - 2026-08-22

### Added

- **Applications** table shows live **CPU** (% of cluster allocatable) and **Memory** (k8s `Mi`/`Gi`) from summed pod metrics when metrics-server is available.
- **Deployments**, **StatefulSets**, and **DaemonSets** tables include **CPU** and **Memory** columns (millicores / `MiB`, summed from owned pods).
- Applications metric **tooltips**: CPU in millicores; memory as `MiB` plus rounded `MB`.
- Applications pod attribution matches instance labels, direct workload owners, and **ReplicaSet → Deployment** owners.
- Transient Kubernetes API errors (for example HTTP/2 `ResponseEnded`) retry automatically; the client uses HTTP/1.1 for list traffic.

### Changed

- Applications memory uses k8s compact units (`512.0MiB`, `<1Mi`) instead of Task Manager `MB` labels; CPU stays as cluster %.
- DataGrid row separators, headers, and zebra rows are tuned for the dark theme (less harsh white lines).

### Fixed

- Pod metrics memory was misread when stored as `MiB`-style strings, which made Applications **Memory** show `0 MB` while CPU looked correct.
- Pod metrics now sum **all** container usages in a pod (not only the last container).

## [0.4.2] - 2026-08-21

### Fixed

- Switching kubectl current-context only patches `current-context` and never writes sibling `config.bak.*` files (Lens treats those as extra kubeconfigs). Blank cluster/user names reuse existing context entries (or match the context name) instead of creating `{name}-cluster` / `{name}-user` orphans. Structural kubeconfig saves also skip `.bak` siblings for the same reason.
- ConfigMap and Secret Data editors accept multiline values and multiline paste via a dedicated value box under the key list.

## [0.4.1] - 2026-08-21

### Fixed

- Staying on Pods (or any resource table) now picks up cluster changes on the automatic refresh. The grid previously only updated after leaving and returning to the view.
- Logs **Follow** tails new pod lines instead of hanging on the snapshot request, and turning Follow off no longer races the live stream into the log text.

## [0.4.0] - 2026-08-20

### Added

- Resource table column filters, row sort, widths, and search are stored per cluster in `appsettings.json` (`Configuration:Layout:Tables`).

### Fixed

- Switching clusters no longer resets table column filters or sort order.

## [0.3.0] - 2026-08-20

### Changed

- Catalog radios set kubectl current-context. The asterisk marker and Connections **Use for kubectl** button are gone.

## [0.2.1] - 2026-08-20

### Fixed

- Resource table IP columns (**Cluster IP**, **External IP**) sort as addresses, not text (`10.1.1.2` before `10.1.1.10`).

## [0.2.0] - 2026-08-20

### Added

- Status-bar messages for port-forward start, stop, restore, and failure.
- Local / Remote labels on the port-forward bar so the two port fields are explicit.
- Rebind the host port of an existing forward from **Network → Port Forwarding** (Local field + Rebind).
- Double-click a live port-forward in **Network → Port Forwarding** to open `http://127.0.0.1:{local}` in the default browser.

### Changed

- Active port-forwards are listed in **Network → Port Forwarding** (stop from the table). They are no longer shown in the status bar.
- Resource table footer, YAML/Data edit bars, and workload limit apply buttons show only for a selected row that can use them. Empty toolbars are hidden.
- Enabled port-forwards are saved in `appsettings.json` (`Configuration:PortForwards`) and restored when a cluster reconnects.

### Fixed

- Service port-forward resolves a backend pod from `spec.selector` and maps the service port to `targetPort`, matching kubectl.
- Service related-pods follow `spec.selector` only, so Helm siblings (for example Longhorn CSI vs UI) are not mixed; named `targetPort` is resolved on a pod that actually declares it.
- Port-forward tunnels use the Kubernetes port-forward multiplex protocol (`StreamType.PortForward`, channel 0, one stream per local connection) instead of exec stdin/stdout.
- Port Forwarding **Remote** is the requested service/pod port (for example `80`), not the mapped container port (`8000` for Longhorn UI). Open `http://127.0.0.1:{local}`.
- Port-forward listens on both IPv4 and IPv6 loopback so `localhost` works on Windows. The IPv6 listener is IPv6-only so it does not collide with IPv4 on Windows.
- Port-forward opens multiplex streams before starting the demuxer, then copies with blocking socket I/O, matching the Kubernetes C# client port-forward example.
- Persisted port-forwards re-resolve a Running pod by Service/workload owner or stable labels after replica recreation, including on each new local connection.

## [0.1.1] - 2026-08-19

### Fixed

- Release engine reads `<Version>` from `src/Directory.Build.props` when the UI csproj has none.
- Warnings no longer treat k3s `EtcdIsVoter=True` as unhealthy; only `False` (learner / non-voter) is raised.

## [0.1.0] - 2026-08-19

### Added

- First public release of **MaksIT.ClusterConsole**, an Avalonia Kubernetes desktop console (catalog, navigator, tables, YAML apply, logs, exec, port-forward), licensed under **Apache 2.0**.
- Cluster access through the official **KubernetesClient** and the same kubeconfig/RBAC as kubectl, including an in-process connections editor.
- Helm releases, Dapr CRDs, Applications view, force-delete, volume file browse, resource-limit patches, and a read-only local Ollama **Chat** tab.

See [README.md](README.md) for the full feature list.

