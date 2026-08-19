# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
