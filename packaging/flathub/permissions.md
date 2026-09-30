# Flathub permissions

Finish args for the manifest. The GitHub bundle uses this set. Keep the Flathub set to what the app uses.

```text
--share=ipc
--share=network
--socket=wayland
--socket=x11
--socket=pulseaudio
--device=dri
--filesystem=home
```

Network reaches the cluster API. Home covers `~/.kube`, a `KUBECONFIG` path under the home directory, settings under `~/.config`, and volume download and upload. X11 and Wayland are both present because Linux runs through X11/XWayland; Avalonia's native Wayland path hangs on GNOME. DRI is for GL rendering. The app has no audio feature; include pulseaudio only when a local run needs it.

A kubeconfig and a reachable cluster are required after install. The metainfo says so. The submission is from the project that maintains this repository.

The pull request steps are in [submission.md](submission.md). Manifest values are in [manifest.md](manifest.md). Verification is in [verification.md](verification.md).
