# Flathub sources

Upstream files stay in this repository. The Flathub build installs them from the release tag.

| Path | Role |
|------|------|
| [data/com.maks_it.clusterconsole.metainfo.xml](../../data/com.maks_it.clusterconsole.metainfo.xml) | AppStream. Id `com.maks_it.clusterconsole`. |
| [data/com.maks_it.clusterconsole.desktop](../../data/com.maks_it.clusterconsole.desktop) | Launcher. `Exec=maksit-cluster-console`. `Terminal=false`. |
| [src/MaksIT.ClusterConsole.UI/Assets/icon.svg](../../src/MaksIT.ClusterConsole.UI/Assets/icon.svg) | Icon, installed as `share/icons/hicolor/scalable/apps/com.maks_it.clusterconsole.svg`. |
| [LICENSE.md](../../LICENSE.md) | Apache-2.0. Install to `$FLATPAK_DEST/share/licenses/com.maks_it.clusterconsole/` because the filename is `LICENSE.md`. |

Screenshot URLs in the metainfo use tag `v0.8.3`. The `0.8.5` release entry points at tag `v0.8.5`. On a newer release, point `<image>` and `<release>` at that tag or at a commit.

## Pull request

| File | Where |
|------|--------|
| `com.maks_it.clusterconsole.yml` or `.json` | Top of the Flathub checkout. Values are in [manifest.md](manifest.md). |
| NuGet sources | Generated, then committed in the pull request. The build has no network. |
| `flathub.json` | Only when one architecture is dropped. Both `x86_64` and `aarch64` stay in scope while both build. |

Generate the NuGet list with [flatpak-builder-tools/dotnet](https://github.com/flatpak/flatpak-builder-tools/tree/master/dotnet) or [Nickvision.FlatpakGenerator](https://github.com/nickvisionapps/flatpakgenerator).

The pull request steps are in [submission.md](submission.md). Finish args are in [permissions.md](permissions.md). Verification is in [verification.md](verification.md).
