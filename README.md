# MaksIT.ClusterConsole

![Line Coverage](https://img.shields.io/badge/Line%20Coverage-52.7%25-yellowgreen)
![Branch Coverage](https://img.shields.io/badge/Branch%20Coverage-43.9%25-yellowgreen)
![Method Coverage](https://img.shields.io/badge/Method%20Coverage-60.2%25-green)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/License-Apache%202.0-blue)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux-0078D6)

Desktop console for Kubernetes clusters. Native Avalonia app on Windows and Linux: browse resources, apply YAML, follow logs, exec into pods, and reach workload UIs through localhost.

Cluster access uses the official Kubernetes .NET client and the same kubeconfig and RBAC as any other API client. Contexts are edited in-process; the kubeconfig file on disk is the source of truth.

See [LICENSE.md](LICENSE.md) (Apache 2.0). Changes: [CHANGELOG.md](CHANGELOG.md). Contributing: [CONTRIBUTING.md](CONTRIBUTING.md).

If you find this project useful, please consider supporting its development:

[<img src="https://cdn.buymeacoffee.com/buttons/v2/default-blue.png" alt="Buy Me A Coffee" style="height: 60px; width: 217px;">](https://www.buymeacoffee.com/maksitcom)

## Highlights

Capabilities that are first-class in ClusterConsole, not afterthoughts:

- **Port-forward, then open the UI** — forwards live under **Network → Port Forwarding**. Double-click a live row to open `http://127.0.0.1:{port}` in the default browser. Enabled forwards persist, restore on reconnect, survive pod recreation (owner or stable labels), and can rebind the local port without recreating the tunnel.
- **Local Ollama chat on the selection** — diagnose the highlighted resource with an on-machine model. The assistant can read cluster issues, YAML, logs, and events. Nothing is sent to a cloud AI API. Chat cannot apply, restart, or delete.
- **Dapr in the navigator** — Components, Configurations, Subscriptions, Resiliency, HTTPEndpoints, sidecars, and control-plane pods as catalog views, not a generic CRD dump.
- **Volume files** — browse, edit, download, and upload files on persistent volumes and claims from the desktop.
- **Limits you can fix** — overview shows container CPU and memory against node capacity and can patch limits that oversubscribe the node.
- **Connections stay in the app** — wizard to add or update a context (token, client certificate, or basic auth). A catalog radio sets kubectl current-context.

## Features

- **Contexts** — catalog of kubeconfig contexts; radio selects kubectl current-context
- **Navigator** — Cluster, Nodes, Applications, Workloads, Config, Network, Storage, Namespaces, Events, Helm, Dapr, Access Control, Custom Resources
- **Resource tables** — list and refresh any catalogued type; per-column filters and row sort persisted per cluster
- **Inspect and apply** — YAML view, apply, create, delete; force-delete (grace period 0 and strip finalizers), including namespaces whose objects are already gone
- **Workloads** — scale, restart, CronJob trigger; node cordon and drain
- **Pods** — follow logs, exec
- **Applications** — one row per instance and namespace from standard application labels
- **Helm** — releases discovered from cluster secrets
- **Metrics** — CPU and memory columns when the metrics API is available

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A kubeconfig (`KUBECONFIG` or `~/.kube/config`) with permission to the target cluster
- Windows or Linux
- Optional: a local [Ollama](https://ollama.com) daemon for Chat

## Getting started

From `src/` so `global.json` applies:

```powershell
cd src
dotnet build MaksIT.ClusterConsole.slnx
dotnet run --project MaksIT.ClusterConsole.UI
```

Connect a context from the catalog, pick a navigator item, then use the table, details pane, and footer actions for the selected row.

## Configuration

Defaults live in `src/MaksIT.ClusterConsole.Shared/appsettings.json` (copied next to the UI). Notable keys under `Configuration`:

| Key | Role |
|-----|------|
| `OllamaEndpoint` | Chat API, default `http://127.0.0.1:11434` |
| `OllamaModel` | Chat model, default `qwen3:8b` |
| `PortForwards` | Enabled localhost forwards; restored when the cluster reconnects |
| `Layout` | Window and pane sizes, last navigator item, per-cluster table layout (`Tables`) |

Port-forwards are saved when you start them in the UI. Chat cannot apply, restart, or delete.

Pull the default Chat model once:

```bash
ollama pull qwen3:8b
```

## Tests

```powershell
utils\Invoke-TestEngine.bat
```

From `src/`:

```powershell
dotnet test MaksIT.ClusterConsole.Tests
```

Tests use kubeconfig fixtures and do not require a live cluster. Coverage shields at the top of this file are maintained by the test engine (**CoverageBadges**).

## Release

1. Update [CHANGELOG.md](CHANGELOG.md) and bump `<Version>` in [src/Directory.Build.props](src/Directory.Build.props).
2. Tag `v{version}` on `main`.
3. Run `utils\Invoke-ReleasePackage.bat`.

## Solution layout

```text
utils/                              # RepoUtils test and release engines
src/
  MaksIT.ClusterConsole.slnx
  MaksIT.ClusterConsole.Client/     # Kubernetes API client
  MaksIT.ClusterConsole.Shared/     # catalog, workspace, configuration
  MaksIT.ClusterConsole.UI/         # Avalonia desktop host
  MaksIT.ClusterConsole.Tests/
```

## Scope

ClusterConsole is a desktop operator console for the Kubernetes API. It is not a CLI, a cluster installer, or a replacement for admission, GitOps, or secret-management systems. Helm listing and Dapr views cover objects in the cluster; they do not install charts or administer Dapr building blocks.

## License

Apache 2.0 — see [LICENSE.md](LICENSE.md).

© Maksym Sadovnychyy (MAKS-IT)
