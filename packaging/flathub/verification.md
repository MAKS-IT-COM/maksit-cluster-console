# Flathub verification

After the app is published, open the Flathub developer portal and copy the verification token it shows for this app. Place that token on its own line in `https://maks-it.com/.well-known/org.flathub.VerifiedApps.txt`.

The portal generates the token. The file does not list the application id. HTTPS is required. A line that starts with `#` is a comment.

Login verification does not apply to `com.maks_it`. The domain from the id is `maks-it.com`.

The pull request steps are in [submission.md](submission.md). Manifest values are in [manifest.md](manifest.md). Finish args are in [permissions.md](permissions.md).
