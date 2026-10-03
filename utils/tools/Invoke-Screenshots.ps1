#requires -Version 7.0
#requires -PSEdition Core

<#
.SYNOPSIS
    Opens Cluster Console and saves PNGs of the main screens.

.DESCRIPTION
    Applies the screenshot window layout, launches the UI with --screenshots,
    and writes one folder of PNGs plus manifest.json. Closes the app when the
    tour finishes. Unless -KeepLayout is set, settings.json is put back to the
    bytes from before this script ran, so the tour does not leave a different
    page or window size behind.

    Close Cluster Console first. The capture uses a live kubeconfig context.
    Grids are shown unsorted and unfiltered, across all namespaces. Column widths stay as saved.

    Each view produces <id>.png for the whole window.
    catalog.png is the catalog column, saved once.
    An empty resource table is skipped.

    With no -Views, the tour opens every navigator page except discovered custom resources, then Connections,
    Settings → AI, volume files, and the YAML, logs, chat, data, images,
    and Helm detail tabs. -Views limits the run to those navigator ids.
    welcome is the shell before a cluster is opened.

    PNGs are 96 DPI device pixels, so a 1600x900 window is a 1600x900 file.

.PARAMETER Context
    Kubeconfig context to open. Omit to use the saved active context, or the
    first context in the catalog.

.PARAMETER Views
    Comma-separated navigator ids. Omit for the default tour.

.PARAMETER OutDir
    Folder for the PNGs. Relative paths are under the repo root.
    Default assets/screenshots.

.PARAMETER SettleMs
    Pause after each view loads, so grids and charts can paint. Default 600.

.PARAMETER Exe
    Path to MaksIT.ClusterConsole.UI.exe. Omit to use the newest build under src.

.PARAMETER Build
    Build the UI project before launching.

.PARAMETER SkipLayout
    Do not rewrite window size and pane widths before launch.

.PARAMETER KeepLayout
    Leave settings.json as the tour finished. By default this script restores
    the file it copied at startup.

.EXAMPLE
    pwsh -File .\utils\tools\Invoke-Screenshots.ps1 -Context staging

.EXAMPLE
    pwsh -File .\utils\tools\Invoke-Screenshots.ps1 -Context staging -Views "overview,pods,services"
#>

[CmdletBinding()]
param(
    [string]$Context,
    [string]$Views,
    [string]$OutDir = "assets/screenshots",
    [int]$SettleMs = 600,
    [string]$Exe,
    [switch]$Build,
    [switch]$SkipLayout,
    [switch]$KeepLayout,
    [int]$Width = 1600,
    [int]$Height = 900
)

$ErrorActionPreference = "Stop"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = Join-Path $repo "src\MaksIT.ClusterConsole.UI\MaksIT.ClusterConsole.UI.csproj"
$layoutScript = Join-Path $PSScriptRoot "Set-ScreenshotLayout.ps1"
$settingsPath = Join-Path $env:APPDATA "MaksIT\Cluster Console\settings.json"

function Resolve-ConsoleExe {
    if (-not [string]::IsNullOrWhiteSpace($Exe)) {
        if (-not (Test-Path -LiteralPath $Exe)) {
            Write-Error "Exe not found: $Exe"
        }

        return (Resolve-Path -LiteralPath $Exe).Path
    }

    $bin = Join-Path $repo "src\MaksIT.ClusterConsole.UI\bin"
    $found = @()

    if (Test-Path -LiteralPath $bin) {
        $found = @(Get-ChildItem -Path $bin -Filter "MaksIT.ClusterConsole.UI.exe" -Recurse -File -ErrorAction SilentlyContinue)
    }

    $newest = $found | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $stale = $newest -and (Test-UiSourcesNewer $newest.FullName)

    if ($stale) {
        Write-Host "UI sources are newer than $($newest.FullName). Rebuilding."
    }

    if ($Build -or $found.Count -eq 0 -or $stale) {
        & dotnet build $project -c Debug --nologo

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed."
        }

        $found = @(Get-ChildItem -Path $bin -Filter "MaksIT.ClusterConsole.UI.exe" -Recurse -File)
    }

    if ($found.Count -eq 0) {
        Write-Error "Could not find MaksIT.ClusterConsole.UI.exe. Pass -Build or -Exe."
    }

    return ($found | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Test-UiSourcesNewer([string]$exePath) {
    $exeTime = (Get-Item -LiteralPath $exePath).LastWriteTimeUtc
    $src = Join-Path $repo "src"

    foreach ($file in (Get-ChildItem -Path $src -Recurse -File)) {
        if ($file.FullName -match '\\(bin|obj)\\') {
            continue
        }

        if ($file.Extension -notin ".cs", ".axaml") {
            continue
        }

        if ($file.LastWriteTimeUtc -gt $exeTime) {
            return $true
        }
    }

    return $false
}

if ($SettleMs -lt 0) {
    Write-Error "SettleMs must be zero or greater."
}

if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $repo $OutDir
}

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$snapshot = $null

if ((Test-Path -LiteralPath $settingsPath) -and -not $KeepLayout) {
    $snapshot = Join-Path ([System.IO.Path]::GetTempPath()) ("cluster-console-settings-" + [guid]::NewGuid().ToString("n") + ".json")
    Copy-Item -LiteralPath $settingsPath -Destination $snapshot
}

$code = 1

try {
    if (-not $SkipLayout) {
        & $layoutScript -Width $Width -Height $Height
    }

    $exePath = Resolve-ConsoleExe
    $argList = @("--screenshots", $OutDir, "--settle-ms", "$SettleMs")

    if (-not [string]::IsNullOrWhiteSpace($Context)) {
        $argList += @("--context", $Context)
    }

    if (-not [string]::IsNullOrWhiteSpace($Views)) {
        $argList += @("--views", $Views)
    }

    Write-Host "Launching $exePath"
    Write-Host "Saving screenshots to $OutDir"
    $proc = Start-Process -FilePath $exePath -ArgumentList $argList -Wait -PassThru
    $code = $proc.ExitCode
}
finally {
    if ($snapshot -and (Test-Path -LiteralPath $snapshot)) {
        $running = Get-Process -Name "MaksIT.ClusterConsole.UI" -ErrorAction SilentlyContinue

        if (-not $running) {
            Copy-Item -LiteralPath $snapshot -Destination $settingsPath -Force
            Remove-Item -LiteralPath $snapshot -Force
            Write-Host "Restored $settingsPath"
        }
        else {
            Write-Warning "Cluster Console is still running, so settings were left in place. Snapshot: $snapshot"
        }
    }
}

$manifestPath = Join-Path $OutDir "manifest.json"

if (Test-Path -LiteralPath $manifestPath) {
    Write-Host (Get-Content -LiteralPath $manifestPath -Raw)
}

if ($code -ne 0) {
    Write-Error "Cluster Console exited with code $code."
}

Write-Host "Screenshots are in $OutDir"
