# Contributing to MaksIT.ClusterConsole

C# style follows the repo-root [`.editorconfig`](.editorconfig).

## Development setup

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- PowerShell 7+ (RepoUtils under `utils/`)

### Build

```powershell
cd src
dotnet build MaksIT.ClusterConsole.slnx
```

### Tests

```powershell
utils\Invoke-TestEngine.bat
```

Coverage shields in `README.md` are rewritten by **CoverageBadges**.

### Release

1. Update [CHANGELOG.md](CHANGELOG.md) and bump `<Version>` in [src/Directory.Build.props](src/Directory.Build.props).
2. Commit on `main`, tag `v{version}` on HEAD (`v1.2.3` or SemVer prerelease such as `v0.1.0-alpha.1`, `v0.1.0-beta.1`, `v0.1.0-rc.1`). GitHub marks hyphenated versions as prerelease.
3. Run `utils\Invoke-ReleasePackage.bat`. That run publishes the portable zip (win-x64), Windows setup exe, and Flatpak (Flatpak via WSL Debian on Windows). Publishing the GitHub Release starts [macOS release assets](.github/workflows/macos-release.yml), which attaches unsigned `osx-arm64` and `osx-x64` DMGs.

## Microsoft Store (MSIX)

`MsixPack` writes `releases/maksit-cluster-console-{version}.msix` from the win-x64 publish. It is a full-trust desktop package (`runFullTrust`), x64, language English. Upload that file on an **MSIX** product in Partner Center. The Store re-signs it. An EXE/MSI product listing cannot take this file.

Partner Center package identity for this product:

| Field | Value |
|-------|--------|
| Package/Identity/Name | `MAKS-IT.ClusterConsole` |
| Package/Identity/Publisher | `CN=FCC8C0E7-6D5F-4028-B8EE-903B88C0C8F9` |
| PublisherDisplayName | `MAKS-IT` |
| Package Family Name | `MAKS-IT.ClusterConsole_pt3s39h1tn26a` |
| Store ID | `9MX86PTHBNN4` |

Those name, publisher, and publisher display strings are `packageName`, `publisher`, and `publisherDisplayName` in `utils/engines/release/scriptSettings.json`. A placeholder publisher `CN=PartnerCenter` stops `MsixPack` until it is replaced.

The `.msix` is not a GitHub release asset.

### System requirements (Properties)

Partner Center → **Properties** → **System requirements**. A blank cell stays unset. Minimum is what the Store may warn on; Recommended does not warn.

The app is a win-x64 desktop console (tables, YAML, terminal). It does not use a camera, microphone, radio, gamepad, or a specific GPU.

| Feature | Minimum | Recommended |
|---------|---------|-------------|
| Touch screen | | |
| Keyboard | Minimum | |
| Mouse | Minimum | |
| Camera | | |
| NFC HCE | | |
| NFC Proximity | | |
| Bluetooth LE | | |
| Telephony | | |
| Microphone | | |
| Xbox controller or gamepad | | |
| Windows Mixed Reality motion controllers | | |
| Windows Mixed Reality immersive headset | | |
| Memory | 2 GB | 4 GB |
| DirectX | Not specified | Not specified |
| Video memory | Not specified | Not specified |
| Processor | x64 | Not specified |
| Graphics | Not specified | Not specified |

## GitHub setup exe

The GitHub Windows installer stays the WiX Burn `setup.exe`. Its switches are `/quiet /norestart` (install), `/repair /quiet /norestart` (repair), and `/uninstall /quiet /norestart` (uninstall). Signing that exe for an EXE/MSI Store listing needs a Trusted Root Authenticode certificate and the Burn order: payload PEs, then the MSI, then `wix burn detach` / sign the engine / `wix burn reattach` / sign `setup.exe` ([WiX signing](https://docs.firegiant.com/wix/tools/signing/)).

## Commit format

```text
(type): description
```

Types: `(feature):`, `(bugfix):`, `(refactor):`, `(perf):`, `(test):`, `(docs):`, `(build):`, `(ci):`, `(style):`, `(revert):`, `(chore):`.

Lowercase description; no trailing period.

## License

By contributing, you agree that your contributions are licensed under the terms in [LICENSE.md](LICENSE.md) (Apache 2.0).
