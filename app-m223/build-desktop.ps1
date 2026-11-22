param(
    [string]$Dotnet = 'D:\AI\Tools\dotnet10\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$run = Join-Path $repo '.p1-work/m223-desktop-live'
$null = New-Item -ItemType Directory -Force -Path $run
$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $repo '.p1-work/_shared/nuget-packages'
$props = @(
    '-p:PublishTrimmed=false', '-p:RuntimeIdentifier=win-x64',
    '-p:SelfContained=true', "-p:ArtifactsPath=$run/artifacts-self-contained",
    '-p:UseArtifactsOutput=true', '-p:NuGetAudit=false',
    '-p:UsedAvaloniaProducts=', '-p:UseSharedCompilation=false',
    '-p:GenerateSonnetLanguageData=false'
)
Push-Location $PSScriptRoot
try {
    & $Dotnet restore src/Preview.csproj --configfile NuGet.Config --locked-mode @props |
        Tee-Object -FilePath "$run/desktop-restore.log"
    if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE)" }

    & $Dotnet build src/Preview.csproj -c Release --no-restore -o "$run/app" @props |
        Tee-Object -FilePath "$run/desktop-build.log"
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}
