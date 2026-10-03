#requires -Version 7.0
#requires -PSEdition Core

<#
.SYNOPSIS
    Pins Cluster Console to one window size and shell proportions for screenshots.

.DESCRIPTION
    Writes WindowWidth, WindowHeight, position, WindowState, CatalogWidth,
    NavigatorWidth, DetailsWidth, and HelmHistoryHeight in
    %AppData%\MaksIT\Cluster Console\settings.json.

    The app reads that file when it opens and writes it again when it closes.
    Close Cluster Console before running this script. The first run keeps a
    backup next to settings.json. -Restore copies that backup back.

    Sizes are Avalonia device-independent pixels. At 100% Windows display scale
    they match physical pixels. A higher scale makes the captured bitmap larger.

    The resource details column starts at 420 in the view. Leave that splitter
    alone while shooting so every frame keeps the same split.

    For an automatic tour of the main screens, run Invoke-Screenshots.ps1.
    That script applies this layout, launches the app, and restores your settings.

.PARAMETER Width
    Window width. Default 1600.

.PARAMETER Height
    Window height. Default 900.

.PARAMETER CatalogWidth
    Left catalog pane. Default 248.

.PARAMETER NavigatorWidth
    Cluster navigator pane. Default 228.

.PARAMETER DetailsWidth
    Saved details-pane width. Default 420.

.PARAMETER HelmHistoryHeight
    Helm history band height. Default 220.

.PARAMETER X
    Window left edge in screen pixels. Default 80.

.PARAMETER Y
    Window top edge in screen pixels. Default 48.

.PARAMETER Restore
    Put the backup back and remove it.

.EXAMPLE
    pwsh -File .\utils\tools\Set-ScreenshotLayout.ps1

.EXAMPLE
    pwsh -File .\utils\tools\Set-ScreenshotLayout.ps1 -Restore
#>

[CmdletBinding()]
param(
    [int]$Width = 1600,
    [int]$Height = 900,
    [int]$CatalogWidth = 248,
    [int]$NavigatorWidth = 228,
    [int]$DetailsWidth = 420,
    [int]$HelmHistoryHeight = 220,
    [int]$X = 80,
    [int]$Y = 48,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

$settingsDir = Join-Path $env:APPDATA "MaksIT\Cluster Console"
$settingsPath = Join-Path $settingsDir "settings.json"
$backupPath = Join-Path $settingsDir "settings.screenshot-backup.json"

function Assert-AppClosed {
    $running = Get-Process -Name "MaksIT.ClusterConsole.UI" -ErrorAction SilentlyContinue

    if ($running) {
        Write-Error "Close Cluster Console first. It overwrites settings.json when it exits."
    }
}

function Write-Settings([System.Text.Json.Nodes.JsonNode]$Root) {
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    $json = $Root.ToJsonString($options) + [Environment]::NewLine
    [System.IO.File]::WriteAllText($settingsPath, $json)
}

Assert-AppClosed

if ($Restore) {
    if (-not (Test-Path -LiteralPath $backupPath)) {
        Write-Error "No screenshot backup at $backupPath"
    }

    Copy-Item -LiteralPath $backupPath -Destination $settingsPath -Force
    Remove-Item -LiteralPath $backupPath -Force
    Write-Host "Restored $settingsPath"
    exit 0
}

if ($Width -lt 960 -or $Height -lt 640) {
    Write-Error "Window must be at least 960 by 640."
}

if ($CatalogWidth -lt 120 -or $NavigatorWidth -lt 120 -or $DetailsWidth -lt 180) {
    Write-Error "Pane widths are below the shell minimums (catalog 120, navigator 120, details 180)."
}

if (-not (Test-Path -LiteralPath $settingsPath)) {
    Write-Error "Settings file not found: $settingsPath. Start Cluster Console once, then close it and run this script."
}

if (-not (Test-Path -LiteralPath $backupPath)) {
    Copy-Item -LiteralPath $settingsPath -Destination $backupPath
    Write-Host "Backup: $backupPath"
}

$root = [System.Text.Json.Nodes.JsonNode]::Parse([System.IO.File]::ReadAllText($settingsPath))
$config = $root["Configuration"]

if ($null -eq $config) {
    Write-Error "settings.json has no Configuration object."
}

$configuration = $config.AsObject()

if ($null -eq $configuration["Layout"]) {
    $configuration["Layout"] = [System.Text.Json.Nodes.JsonObject]::new()
}

$layout = $configuration["Layout"].AsObject()
$layout["WindowWidth"] = [System.Text.Json.Nodes.JsonValue]::Create([double]$Width)
$layout["WindowHeight"] = [System.Text.Json.Nodes.JsonValue]::Create([double]$Height)
$layout["WindowX"] = [System.Text.Json.Nodes.JsonValue]::Create($X)
$layout["WindowY"] = [System.Text.Json.Nodes.JsonValue]::Create($Y)
$layout["WindowState"] = [System.Text.Json.Nodes.JsonValue]::Create("Normal")
$layout["CatalogWidth"] = [System.Text.Json.Nodes.JsonValue]::Create([double]$CatalogWidth)
$layout["NavigatorWidth"] = [System.Text.Json.Nodes.JsonValue]::Create([double]$NavigatorWidth)
$layout["DetailsWidth"] = [System.Text.Json.Nodes.JsonValue]::Create([double]$DetailsWidth)
$layout["HelmHistoryHeight"] = [System.Text.Json.Nodes.JsonValue]::Create([double]$HelmHistoryHeight)

Write-Settings $root

Write-Host "Screenshot layout written to $settingsPath"
Write-Host "  Window ${Width}x${Height} at ${X},${Y} (Normal)"
Write-Host "  Catalog $CatalogWidth, navigator $NavigatorWidth, details $DetailsWidth, Helm history $HelmHistoryHeight"
Write-Host "Start Cluster Console and take the shots. Do not resize the window or the splitters."
Write-Host "When you are done, close the app and run this script with -Restore."
