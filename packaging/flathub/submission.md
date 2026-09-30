# Flathub submission

Open a pull request on [flathub/flathub](https://github.com/flathub/flathub) against the `new-pr` branch.

Title:

```text
Add com.maks_it.clusterconsole
```

Procedure: [Submission](https://docs.flathub.org/docs/for-app-authors/submission). Rules: [Requirements](https://docs.flathub.org/docs/for-app-authors/requirements). Metadata: [MetaInfo guidelines](https://docs.flathub.org/docs/for-app-authors/metainfo-guidelines). Runtimes: [Runtimes](https://docs.flathub.org/docs/for-app-authors/runtimes).

GitHub Releases keep the `FlatpakPack` bundle from the published `linux-x64` tree. Flathub compiles a release tag of this repository. Stable GitHub releases are the channel for the stable Flathub repository.

## Check

Build and install with `org.flatpak.Builder`, run `com.maks_it.clusterconsole`, and run `flatpak-builder-lint` on the manifest and on the local repo. Commands are on the submission page. AppStream warnings fail the linter.

Connect a kubeconfig, open a table, and confirm the window under X11/XWayland. Review comments stay on the same pull request. A test build is requested by commenting `bot, build`.

Sources are in [sources.md](sources.md). Manifest values are in [manifest.md](manifest.md). Finish args are in [permissions.md](permissions.md). Verification is in [verification.md](verification.md).
