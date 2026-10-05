# Flathub permissions

Finish args for the manifest. The GitHub bundle uses this set. Keep the Flathub set to what the app uses.

```text
--share=ipc
--share=network
--socket=wayland
--socket=x11
--device=dri
--filesystem=home
```

Network reaches the cluster API. Settings stay in the sandbox config directory. Home covers a kubeconfig anywhere under the home directory, including `~/.kube` and certificate files the kubeconfig names, plus files chosen in the open and save dialogs. X11 and Wayland stay together until Avalonia's native Wayland path no longer hangs on GNOME. Linux runs through X11/XWayland. DRI is for GL rendering. The app has no audio, so the sandbox does not take pulseaudio.

A kubeconfig and a reachable cluster are required after install. `--share=network` uses this computer's network, including localhost, so a local cluster API, local Ollama, and a port-forward opened in the browser stay reachable. A kubeconfig login that starts `aws`, `gcloud`, or `kubelogin` cannot start that program from the sandbox. A certificate or token in the kubeconfig is enough. The metainfo says so. The submission is from the project that maintains this repository.

The pull request steps are in [submission.md](submission.md). Manifest values are in [manifest.md](manifest.md). Verification is in [verification.md](verification.md).
