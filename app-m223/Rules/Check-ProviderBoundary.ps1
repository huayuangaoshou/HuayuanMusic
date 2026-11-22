param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$contracts = Join-Path $root 'Contracts'
$mock = Join-Path $root 'Mock'

# CI guard, not an operating-system sandbox. Run for EVERY new provider source change.
$forbidden = @(
    '\bKugouAvaloniaPlayer\b',
    '\b(?:PlaybackQueueManager|PlayerViewModel|IPlaybackCommands|AppSqliteStore|AppDatabase|KgSessionManager|KugouSessionPersistence)\b',
    '\bMicrosoft\.Data\.Sqlite\b',
    '\busing\s+System\.(?:Net|IO|Reflection|Diagnostics|Security)(?:\.[A-Za-z_]+)*\s*;',
    '\b(?:File|Directory|FileStream|HttpClient|WebRequest|Socket|TcpClient|Process|Registry)\s*[.(]',
    '\b(?:System\.(?:IO|Net|Reflection|Diagnostics)|System\.Runtime\.InteropServices)\s*\.',
    '\b(?:DllImport|NativeLibrary|Assembly\.Load|Activator\.CreateInstance|dynamic)\b',
    '\b(?:Environment\s*\.|AppDomain\s*\.)'
)
function Test-ForbiddenSource([string]$contents) {
    # Comments are not executable code. This guard intentionally errs on the side of false positives.
    $code = (($contents -split "\r?\n") | Where-Object { $_ -notmatch '^\s*//' }) -join [Environment]::NewLine
    foreach($pattern in $forbidden) { if([regex]::IsMatch($code,$pattern)) { return $pattern } }
    return ''
}
$checks = 0
function Pass([bool]$ok,[string]$label) {
    if(-not $ok) { throw "BOUNDARY_FAIL: $label" }
    $script:checks++
    Write-Output ("BOUNDARY_PASS " + $script:checks + " " + $label)
}
if($SelfTest) {
    foreach($bad in @(
        'using KugouAvaloniaPlayer.Services;',
        'AppSqliteStore.SaveValue("session","x","y");',
        'new HttpClient();',
        'File.ReadAllText("secret");',
        'using Microsoft.Data.Sqlite;',
        'System.Reflection.Assembly.Load("x");',
        'PlayerViewModel model;',
        'new Process();'
    )) { Pass ((Test-ForbiddenSource $bad).Length -gt 0) ('reject forbidden: '+$bad) }
    Pass ((Test-ForbiddenSource 'public sealed class SafeMock : IProviderModule {}').Length -eq 0) 'accept safe contract reference'
}
foreach($project in @(
    (Join-Path $contracts 'Provider.Contracts.csproj'),
    (Join-Path $mock 'Provider.Mock.csproj')
)) {
    $content = Get-Content -LiteralPath $project -Raw
    Pass (-not [regex]::IsMatch($content,'<PackageReference\b','IgnoreCase')) ('no NuGet packages: '+(Split-Path $project -Leaf))
    $refs = [regex]::Matches($content,'<ProjectReference\s+Include="([^"]+)"')
    if($project -like '*Contracts.csproj') {
        Pass ($refs.Count -eq 0) 'contract assembly has no project dependencies'
    } else {
        Pass ($refs.Count -eq 1) 'mock provider has only one project dependency'
        $actual = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $project -Parent) $refs[0].Groups[1].Value))
        $expected = [System.IO.Path]::GetFullPath((Join-Path $contracts 'Provider.Contracts.csproj'))
        Pass ($actual -eq $expected) 'mock only references isolated contract'
    }
}
$csFiles = @((Get-ChildItem -LiteralPath $contracts,$mock -Filter '*.cs' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](?:obj|bin)[\\/]' }))
Pass ($csFiles.Count -ge 2) 'contract/mock sources exist'
foreach($file in $csFiles) {
    $match = Test-ForbiddenSource (Get-Content -LiteralPath $file.FullName -Raw)
    Pass ([string]::IsNullOrEmpty($match)) ('deny core/network/db/credential use: '+$file.Name+ ' match='+$match)
}
Write-Output ('BOUNDARY_RESULT=PASS CHECKS='+$checks+' SELFTEST='+$SelfTest.ToString().ToUpperInvariant())
