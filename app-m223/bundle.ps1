param(
    [string]$Dotnet = 'D:\AI\Tools\dotnet10\dotnet.exe',
    [string]$InnoCompiler = '',
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $PSScriptRoot 'src'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$release = Join-Path $artifacts 'release-build'
$publishDir = Join-Path $release 'app'

function Step([string]$name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------
# 1. Icon: generate/verify the transparent rounded app.ico
# ---------------------------------------------------------------
Step '1/3 generate transparent rounded icon'
$iconResult = & (Join-Path $PSScriptRoot 'tools\Build-AppIcon.ps1')
if (-not $iconResult) { throw "Icon build failed (no output)" }
Write-Host $iconResult

# ---------------------------------------------------------------
# 2. Build: offline self-contained Release publish (locked-mode)
# ---------------------------------------------------------------
Step '2/3 offline self-contained Release publish'
$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $root '.p1-work/_shared/nuget-packages'

$props = @(
    '-p:RuntimeIdentifier=win-x64',
    '-p:SelfContained=true', '-p:NuGetAudit=false',
    '-p:UsedAvaloniaProducts=', '-p:UseSharedCompilation=false',
    '-p:GenerateSonnetLanguageData=false'
)

# Wipe the publish directory first so stale runtime data (profiles, cookies,
# caches, logs, sqlite) can never leak into the installer or the release asset.
# Uses the .NET Directory API directly (instead of Remove-Item) so the wipe is
# a single atomic directory removal, not a per-file bulk delete.
if (Test-Path -LiteralPath $publishDir) {
    [System.IO.Directory]::Delete($publishDir, $true)
}
$null = New-Item -ItemType Directory -Force -Path $publishDir
Push-Location $PSScriptRoot
try {
    & $Dotnet restore src/Preview.csproj --configfile NuGet.Config --locked-mode @props |
        Tee-Object -FilePath (Join-Path $artifacts 'release-restore.log')
    if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE)" }

    & $Dotnet publish src/Preview.csproj -c Release --no-restore -o $publishDir @props |
        Tee-Object -FilePath (Join-Path $artifacts 'release-publish.log')
    if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}

# ---------------------------------------------------------------
# 3. Inno Setup: compile the one-click installer (optional)
# ---------------------------------------------------------------
Step '3/3 compile Inno Setup installer'
if ($SkipInstaller) {
    Write-Host 'Skipped installer build (-SkipInstaller)' -ForegroundColor Yellow
} else {
    $isccCandidates = @()
    if ($InnoCompiler) { $isccCandidates += $InnoCompiler }
    $isccCandidates += 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    $isccCandidates += 'C:\Program Files\Inno Setup 6\ISCC.exe'
    $isccCandidates += "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    $iscc = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    if (-not $iscc) {
        Write-Host "Inno Setup 6 not found, skipping installer. Expected output:`n  $release\HuayuanMusic-Setup-v1.0.0.exe" -ForegroundColor Yellow
    } else {
        Write-Host "Using ISCC: $iscc"
        Push-Location $PSScriptRoot
        try {
            & $iscc installer.iss
            if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)" }
        } finally {
            Pop-Location
        }
    }
}

Write-Host "`nDone. Output directory: $release" -ForegroundColor Green
Get-ChildItem -Path $release -Recurse -File | ForEach-Object {
    "{0,10:N0}  {1}" -f $_.Length, $_.FullName
}
