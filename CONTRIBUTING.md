# Contributing to MaksIT.ClusterConsole

Maintainer agent conventions: [AGENTS.md](AGENTS.md). Repo hygiene: **maksit-repo-maintenance**. C# style: **common/csharp** + repo-root [`.editorconfig`](.editorconfig).

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
3. Run `utils\Invoke-ReleasePackage.bat`.

## Commit format

```text
(type): description
```

Types: `(feature):`, `(bugfix):`, `(refactor):`, `(perf):`, `(test):`, `(docs):`, `(build):`, `(ci):`, `(style):`, `(revert):`, `(chore):`.

Lowercase description; no trailing period.

## License

By contributing, you agree that your contributions are licensed under the terms in [LICENSE.md](LICENSE.md) (Apache 2.0).
