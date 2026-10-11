# Generates 16x16 tray status ICO variants into artifacts/branding/tray/ (non-tracked).
param(
    [string]$PreviewPng,
    [string]$OutputDir,
    [switch]$Quiet,
    [switch]$SkipIfUpToDate
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "branding-asset-paths.ps1")
Add-Type -AssemblyName System.Drawing

$paths = Get-SvrBrandingAssetPaths -RepoRoot (Get-SvrBrandingRepoRoot -FromScriptRoot $PSScriptRoot)
if (-not $PreviewPng) { $PreviewPng = $paths.PreviewPng }
if (-not $OutputDir) { $OutputDir = (Join-Path $paths.GeneratedDir "tray") }

$variants = @(
    @{ Name = "vpn-route-tray-ready.ico"; Color = [Drawing.Color]::FromArgb(255, 15, 107, 255) }
    @{ Name = "vpn-route-tray-active.ico"; Color = [Drawing.Color]::FromArgb(255, 5, 150, 105) }
    @{ Name = "vpn-route-tray-error.ico"; Color = [Drawing.Color]::FromArgb(255, 220, 38, 38) }
)

function Get-TrayGenerationInputPaths {
    param([string]$RepoRoot, [string]$PreviewPath)
    @(
        $PreviewPath
        (Join-Path $RepoRoot "assets\branding\vpn-route-icon-source.png")
        (Join-Path $RepoRoot "scripts\generate-tray-icons.ps1")
        (Join-Path $RepoRoot "scripts\generate-app-icon.ps1")
        (Join-Path $RepoRoot "scripts\branding-asset-paths.ps1")
    )
}

function Test-TrayOutputsCurrent {
    param([string]$RepoRoot, [string]$PreviewPath, [string]$Dir, [object[]]$Files)
    foreach ($file in $Files) {
        $path = Join-Path $Dir $file.Name
        if (-not (Test-Path -LiteralPath $path)) { return $false }
    }
    $maxInput = Get-SvrBrandingGenerationInputMaxTimeUtc -InputPaths (Get-TrayGenerationInputPaths -RepoRoot $RepoRoot -PreviewPath $PreviewPath)
    if ($null -eq $maxInput) { return $false }
    foreach ($file in $Files) {
        $path = Join-Path $Dir $file.Name
        if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $maxInput) { return $false }
    }
    return $true
}

if (-not (Test-Path -LiteralPath $PreviewPng)) {
    throw "Tray icon preview not found (run generate-app-icon first): $PreviewPng"
}

if ($SkipIfUpToDate -and (Test-TrayOutputsCurrent -RepoRoot $paths.RepoRoot -PreviewPath $PreviewPng -Dir $OutputDir -Files $variants)) {
    if (-not $Quiet) { Write-Host "Tray icons up to date: $OutputDir" }
    return
}

function Write-SingleSizeIco {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Path)
    function Get-BitmapBytes([System.Drawing.Bitmap]$bmp) {
        $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $stride = $data.Stride
            $bytes = New-Object byte[] ($stride * $bmp.Height)
            [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
            $row = $bmp.Width * 4
            $andMaskRow = [int]([Math]::Ceiling($bmp.Width / 32.0) * 4)
            $imageSize = $row * $bmp.Height
            $andMaskSize = $andMaskRow * $bmp.Height
            $result = New-Object byte[] (40 + $imageSize + $andMaskSize)
            [BitConverter]::GetBytes([uint32]40).CopyTo($result, 0)
            [BitConverter]::GetBytes([int32]$bmp.Width).CopyTo($result, 4)
            [BitConverter]::GetBytes([int32]($bmp.Height * 2)).CopyTo($result, 8)
            [BitConverter]::GetBytes([uint16]1).CopyTo($result, 12)
            [BitConverter]::GetBytes([uint16]32).CopyTo($result, 14)
            [BitConverter]::GetBytes([uint32]($imageSize + $andMaskSize)).CopyTo($result, 20)
            $offset = 40
            for ($y = $bmp.Height - 1; $y -ge 0; $y--) {
                [Array]::Copy($bytes, $y * $stride, $result, $offset, $row)
                $offset += $row
            }
            return ,@($result, (40 + $imageSize + $andMaskSize))
        }
        finally {
            $bmp.UnlockBits($data)
        }
    }

    $pair = Get-BitmapBytes $Bitmap
    $data = $pair[0]
    $size = $pair[1]
    $dir = Split-Path $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $fs = [IO.File]::Create($Path)
    $bw = New-Object IO.BinaryWriter $fs
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]1)
    $w = if ($Bitmap.Width -ge 256) { [byte]0 } else { [byte]$Bitmap.Width }
    $h = if ($Bitmap.Height -ge 256) { [byte]0 } else { [byte]$Bitmap.Height }
    $bw.Write($w)
    $bw.Write($h)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$size)
    $bw.Write([UInt32]22)
    $bw.Write($data)
    $bw.Close()
    $fs.Close()
}

function New-TrayBitmap([System.Drawing.Image]$source, [System.Drawing.Color]$indicatorColor) {
    $size = 16
    $canvas = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $brush = New-Object System.Drawing.SolidBrush $indicatorColor
    $g.FillEllipse($brush, 10, 10, 5, 5)
    $g.Dispose()
    $brush.Dispose()
    return $canvas
}

$src = [System.Drawing.Image]::FromFile($PreviewPng)
try {
    foreach ($variant in $variants) {
        $bmp = New-TrayBitmap $src $variant.Color
        try {
            $out = Join-Path $OutputDir $variant.Name
            Write-SingleSizeIco -Bitmap $bmp -Path $out
        }
        finally {
            $bmp.Dispose()
        }
    }
}
finally {
    $src.Dispose()
}

if ($Quiet) {
    if (Get-Command Write-SvrUpdateDetail -ErrorAction SilentlyContinue) {
        Write-SvrUpdateDetail "Created tray icons in $OutputDir"
    }
}
else {
    Write-Host "Created tray icons in $OutputDir"
}
