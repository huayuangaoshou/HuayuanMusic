param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Assets\app_raw.png'),
    [string]$PngOutput = (Join-Path $PSScriptRoot '..\src\Assets\icon-1024.png'),
    [string]$IcoOutput = (Join-Path $PSScriptRoot '..\src\Assets\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    if (-not (Test-Path -LiteralPath $IcoOutput -PathType Leaf)) {
        throw 'app_raw.png is missing and there is no existing app.ico to preserve.'
    }
    Write-Output 'ICON_SOURCE_MISSING_EXISTING_ICO_PRESERVED'
    exit 0
}

$sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)
$rendered = [System.Collections.Generic.List[System.Drawing.Bitmap]]::new()
$frames = [System.Collections.Generic.List[byte[]]]::new()
try {
    $middleX = [int][Math]::Floor($sourceImage.Width / 2)
    $middleY = [int][Math]::Floor($sourceImage.Height / 2)

    function Test-SubjectPixel([int]$X, [int]$Y) {
        $pixel = $sourceImage.GetPixel($X, $Y)
        $maximum = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
        $minimum = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
        return (($maximum - $minimum) -ge 12)
    }
    function Find-Edge([int]$Fixed, [int]$Limit, [bool]$Horizontal, [bool]$Reverse) {
        if ($Reverse) { $range = ($Limit - 1)..0 } else { $range = 0..($Limit - 1) }
        foreach ($step in $range) {
            $x = if ($Horizontal) { $step } else { $Fixed }
            $y = if ($Horizontal) { $Fixed } else { $step }
            if (Test-SubjectPixel $x $y) { return $step }
        }
        throw 'Unable to locate the coloured icon boundary from an image edge midpoint.'
    }

    $left = Find-Edge $middleY $sourceImage.Width $true $false
    $right = Find-Edge $middleY $sourceImage.Width $true $true
    $top = Find-Edge $middleX $sourceImage.Height $false $false
    $bottom = Find-Edge $middleX $sourceImage.Height $false $true
    $side = [Math]::Min(($right - $left + 1), ($bottom - $top + 1)) - 4
    if ($side -lt 64) { throw 'Detected icon bounds are unexpectedly small.' }
    $centerX = ($left + $right) / 2.0
    $centerY = ($top + $bottom) / 2.0
    $crop = [System.Drawing.Rectangle]::new(
        [int][Math]::Round($centerX - $side / 2.0) + 2,
        [int][Math]::Round($centerY - $side / 2.0) + 2,
        $side, $side)
    if ($crop.X -lt 0 -or $crop.Y -lt 0 -or $crop.Right -gt $sourceImage.Width -or $crop.Bottom -gt $sourceImage.Height) {
        throw 'Detected square crop falls outside the source image.'
    }

    function New-RoundedIcon([int]$Size) {
        $scale = 4
        $largeSize = $Size * $scale
        $bitmap = [System.Drawing.Bitmap]::new($largeSize, $largeSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $diameter = [single]($largeSize * 0.44)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($largeSize - $diameter, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($largeSize - $diameter, $largeSize - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $largeSize - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $graphics.SetClip($path)
        $destination = [System.Drawing.Rectangle]::new(0, 0, $largeSize, $largeSize)
        $graphics.DrawImage($sourceImage, $destination, $crop, [System.Drawing.GraphicsUnit]::Pixel)
        $graphics.Dispose()
        $path.Dispose()

        $small = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $downsample = [System.Drawing.Graphics]::FromImage($small)
        $downsample.Clear([System.Drawing.Color]::Transparent)
        $downsample.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $downsample.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $downsample.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $downsample.DrawImage($bitmap, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
        $downsample.Dispose()
        $bitmap.Dispose()
        return $small
    }

    $icon1024 = New-RoundedIcon 1024
    $directory = Split-Path -Parent $PngOutput
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $icon1024.Save($PngOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon1024.Dispose()

    foreach ($size in @(16, 32, 48, 64, 128, 256)) {
        $frame = New-RoundedIcon $size
        $rendered.Add($frame)
        $memory = [System.IO.MemoryStream]::new()
        $frame.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($memory.ToArray())
        $memory.Dispose()
    }

    $ico = [System.IO.File]::Open($IcoOutput, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $writer = [System.IO.BinaryWriter]::new($ico)
        $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        for ($i = 0; $i -lt $frames.Count; $i++) {
            $dimension = if ($rendered[$i].Width -eq 256) { [byte]0 } else { [byte]$rendered[$i].Width }
            $writer.Write($dimension); $writer.Write($dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([UInt16]1); $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frameBytes in $frames) { $writer.Write($frameBytes) }
        $writer.Flush()
    } finally { $ico.Dispose() }

    Write-Output ("ICON_CREATED crop={0},{1},{2},{2} sizes=16,32,48,64,128,256 cornerAlpha={3}" -f $crop.X, $crop.Y, $crop.Width, $rendered[-1].GetPixel(0,0).A)
} finally {
    foreach ($frame in $rendered) { $frame.Dispose() }
    $sourceImage.Dispose()
}
