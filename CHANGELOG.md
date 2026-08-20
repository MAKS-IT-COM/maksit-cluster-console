# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
