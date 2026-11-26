param([string]$Archive)

$ErrorActionPreference = 'Stop'
$fontUri = 'https://hyperos.mi.com/font-download/MiSans.zip'
$licenseUri = 'https://hyperos.mi.com/font-download/MiSans%E5%AD%97%E4%BD%93%E7%9F%A5%E8%AF%86%E4%BA%A7%E6%9D%83%E8%AE%B8%E5%8F%AF%E5%8D%8F%E8%AE%AE.pdf'
$archiveSha256 = 'B6AA1FC827035922612DF8EDF36E5609BCA1C5441E25CD57572204569B7B81D9'
$target = Join-Path $PSScriptRoot 'Assets/Fonts'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temporary
try {
    if (!$Archive) {
        $Archive = Join-Path $temporary 'MiSans.zip'
        Invoke-WebRequest $fontUri -TimeoutSec 90 -OutFile $Archive
    }
    $Archive = (Resolve-Path -LiteralPath $Archive).Path
    if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $archiveSha256) {
        throw 'The official MiSans archive has changed. Verify the new archive and license before updating its checksum.'
    }

    $license = Join-Path $temporary 'MiSans-OFFICIAL-LICENSE.pdf'
    Invoke-WebRequest $licenseUri -TimeoutSec 45 -OutFile $license
    $licenseBytes = [IO.File]::ReadAllBytes($license)
    if ($licenseBytes.Length -lt 1000 -or [Text.Encoding]::ASCII.GetString($licenseBytes, 0, 5) -ne '%PDF-') {
        throw 'The official license response is not a PDF.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $fontNames = 'MiSans-Regular.ttf', 'MiSans-Demibold.ttf'
        foreach ($name in $fontNames) {
            $entry = $zip.GetEntry("MiSans/ttf/$name")
            if (!$entry) { throw "Missing official archive entry: $name" }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $temporary $name))
        }
        foreach ($name in $fontNames + 'MiSans-OFFICIAL-LICENSE.pdf') {
            Copy-Item -LiteralPath (Join-Path $temporary $name) -Destination (Join-Path $target $name) -Force
        }
    } finally { $zip.Dispose() }
} finally {
    Get-ChildItem -LiteralPath $temporary -File | Remove-Item
    Remove-Item -LiteralPath $temporary
}
Write-Output 'MiSans Regular, Demibold and the Xiaomi license are ready for the offline build.'
