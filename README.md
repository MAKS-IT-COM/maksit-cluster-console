# MaksIT.ClusterConsole

![Line Coverage](https://img.shields.io/badge/Line%20Coverage-50.9%25-yellowgreen)
![Branch Coverage](https://img.shields.io/badge/Branch%20Coverage-41.5%25-yellowgreen)
![Method Coverage](https://img.shields.io/badge/Method%20Coverage-56.7%25-yellowgreen)

Cross-platform Kubernetes desktop console (Avalonia). Lens-style catalog, navigator, resource tables, YAML apply, logs, exec, port-forward, Helm releases, and Dapr CRDs. Talks to the cluster through the official **KubernetesClient** NuGet package and the same kubeconfig/RBAC as kubectl.

See [LICENSE.md](LICENSE.md) (Apache 2.0). Changes: [CHANGELOG.md](CHANGELOG.md). Contributing: [CONTRIBUTING.md](CONTRIBUTING.md).

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A kubeconfig (`KUBECONFIG` or `~/.kube/config`)
- Windows or Linux

## Build

From `src/` so `global.json` applies:

```powershell
cd src
dotnet build MaksIT.ClusterConsole.slnx
```

Run the UI:

```powershell
dotnet run --project MaksIT.ClusterConsole.UI
```

## Tests

```powershell
utils\Invoke-TestEngine.bat
```

Or from `src/`:

```powershell
dotnet test MaksIT.ClusterConsole.Tests
```

## Local Ollama chat

Default model is **`qwen3:8b`** (~5.2GB) — the strongest Qwen chat/reasoning tag that still fits an RTX 3060 12GB with KV cache headroom. Endpoint `http://127.0.0.1:11434` (override `Configuration:OllamaEndpoint` / `OllamaModel` in `appsettings.json`).

```bash
ollama pull qwen3:8b
```

Fallback if tool calling is weak: `qwen2.5:7b`. Do not use `qwen3:14b` or `deepseek-r1:14b` as the default on 12GB (stretch only).

Open a resource table, pick a row, open **Chat**, then ask e.g. `What is wrong with this pod?`. The assistant can read issues, YAML, logs, and events. It cannot apply, restart, or delete.

## Release

1. Update [CHANGELOG.md](CHANGELOG.md) and bump `<Version>` in [src/Directory.Build.props](src/Directory.Build.props).
2. Tag `v{version}` on `main`.
3. Run `utils\Invoke-ReleasePackage.bat`.

## What it is

- Catalog of kubeconfig contexts (`*` marks kubectl `current-context`)
- Connections editor and wizard to add/upsert a context (token, cert, k3s data, or basic auth) and switch kubectl `current-context`
- Navigator: icon categories with collapsible sub-items (Cluster, Nodes, Applications, Workloads, Config, Network, Storage, Namespaces, Events, Helm, Dapr, Access Control, Custom Resources)
- Generic GVR browser (list/watch-by-refresh, YAML, apply, delete) with per-column header filters (type to filter rows and the value list; checkboxes; double-click a value to keep only that one). Namespace scope is the Namespace column filter (persisted per context).
- Workload scale/restart, node cordon/drain, CronJob trigger
- Force delete (grace period 0 + strip finalizers); force-delete namespace from the Namespaces view, including orphaned sandboxes whose Namespace object is already gone
- Pod logs (follow), exec, port-forward dock
- Helm releases from secrets (`owner=helm`)
- Applications view: one row per instance/namespace from `app.kubernetes.io/instance` (or `name` if instance is missing)
- Dapr Components/Configurations/Subscriptions/Resiliency/HTTPEndpoints, sidecars, control-plane pods
- CPU/MEM columns when `metrics.k8s.io` is available
- Overview resource-limits table: inspect and patch container CPU/memory limits when they exceed node capacity
- **Chat** tab (local Ollama): diagnose the selected resource with read-only cluster tools

## What it is not

kubectl CLI wrapper, Lens extensions, Vault users/scopes, k9s plugins, Dapr state/JetStream admin. Kubeconfig editing is in-process (same file kubectl uses), not a kubectl.exe wrapper.
