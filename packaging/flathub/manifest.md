# Flathub manifest

Values for `com.maks_it.clusterconsole.yml` (or `.json`) at the top of the Flathub checkout.

| Item | Value |
|------|--------|
| Application id | `com.maks_it.clusterconsole` |
| Domain | `maks-it.com` |
| Homepage | [https://maks-it.com/](https://maks-it.com/) |
| Project | `src/MaksIT.ClusterConsole.UI/MaksIT.ClusterConsole.UI.csproj` |
| Target | `net10.0` |
| Source | Release tag on `https://github.com/MAKS-IT-COM/maksit-cluster-console` (currently `v0.8.7`) |
| Runtime | `org.freedesktop.Platform` |
| SDK | `org.freedesktop.Sdk` |
| SDK extension | `org.freedesktop.Sdk.Extension.dotnet10` |
| Runtimes | `linux-x64`, `linux-arm64`, self-contained |
| Command | `maksit-cluster-console` |
| Apphost | `MaksIT.ClusterConsole.UI` |
| Project license | `Apache-2.0` |
| Metadata license | `CC0-1.0` |

Publish the UI project. It references Shared. Tests stay out of the package. The Flatpak command matches the desktop `Exec`. The published apphost name is `MaksIT.ClusterConsole.UI`.

Use the newest Freedesktop branch that `org.freedesktop.Sdk.Extension.dotnet10` publishes, and the branch Flathub requires on the day of submission. Extension usage is in the [extension README](https://github.com/flathub/org.freedesktop.Sdk.Extension.dotnet10). Avalonia native libraries follow the runtime identifier.

`dotnet publish` uses `--source ./nuget-sources` before `--source /usr/lib/sdk/dotnet10/nuget/packages`.

The pull request steps are in [submission.md](submission.md). Source files are in [sources.md](sources.md). Finish args are in [permissions.md](permissions.md). Verification is in [verification.md](verification.md).
