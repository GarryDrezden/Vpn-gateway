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

# Deep base fills tuned for 16x16 tray readability (reference-aligned, not copied from upload/).
$variants = @(
    @{
        Name = "vpn-route-tray-ready.ico"
        Background = [Drawing.Color]::FromArgb(255, 26, 82, 196)
        Dot = [Drawing.Color]::FromArgb(255, 140, 190, 255)
    }
    @{
        Name = "vpn-route-tray-active.ico"
        Background = [Drawing.Color]::FromArgb(255, 6, 132, 88)
        Dot = [Drawing.Color]::FromArgb(255, 120, 230, 170)
    }
    @{
        Name = "vpn-route-tray-error.ico"
        Background = [Drawing.Color]::FromArgb(255, 204, 42, 42)
        Dot = [Drawing.Color]::FromArgb(255, 255, 170, 170)
    }
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

function Get-TrayLuminance([int]$r, [int]$g, [int]$b) {
    return (0.2126 * $r) + (0.7152 * $g) + (0.0722 * $b)
}

function Get-TrayChroma([int]$r, [int]$g, [int]$b) {
    $max = [Math]::Max($r, [Math]::Max($g, $b))
    $min = [Math]::Min($r, [Math]::Min($g, $b))
    return $max - $min
}

function Test-TrayForegroundPixel([int]$r, [int]$g, [int]$b, [int]$a) {
    if ($a -lt 24) {
        return $false
    }

    $lum = Get-TrayLuminance $r $g $b
    $chroma = Get-TrayChroma $r $g $b

    if ($lum -ge 150 -and $chroma -le 50) {
        return $true
    }

    if ($b -gt 150 -and $g -gt 110 -and $r -lt 130) {
        return $true
    }

    if ($r -gt 150 -and $g -gt 65 -and $g -lt 190 -and $b -lt 130) {
        return $true
    }

    if ($lum -ge 58 -and $lum -le 145 -and $b -ge ($r + 8) -and $b -ge ($g + 4) -and $chroma -ge 12) {
        return $true
    }

    if ($lum -ge 175) {
        return $true
    }

    return $false
}

function Get-TrayBackgroundShade([System.Drawing.Color]$base, [double]$shade, [double]$rowFactor) {
    $factor = $shade * $rowFactor
    $r = [int][Math]::Max(0, [Math]::Min(255, [Math]::Round($base.R * $factor)))
    $g = [int][Math]::Max(0, [Math]::Min(255, [Math]::Round($base.G * $factor)))
    $b = [int][Math]::Max(0, [Math]::Min(255, [Math]::Round($base.B * $factor)))
    return [Drawing.Color]::FromArgb(255, $r, $g, $b)
}

function New-ScaledTraySource([System.Drawing.Image]$source, [int]$size) {
    $canvas = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    return $canvas
}

function New-RecoloredTrayBitmap([System.Drawing.Bitmap]$scaled, [System.Drawing.Color]$background) {
    $height = $scaled.Height
    $width = $scaled.Width
    $canvas = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $baseFillLum = 42.0

    for ($y = 0; $y -lt $height; $y++) {
        $rowFactor = 1.06 - (0.14 * ($y / [Math]::Max(1, ($height - 1))))
        for ($x = 0; $x -lt $width; $x++) {
            $c = $scaled.GetPixel($x, $y)
            if ($c.A -lt 8) {
                $canvas.SetPixel($x, $y, [Drawing.Color]::FromArgb(0, 0, 0, 0))
                continue
            }

            if (Test-TrayForegroundPixel $c.R $c.G $c.B $c.A) {
                $canvas.SetPixel($x, $y, $c)
                continue
            }

            $lum = Get-TrayLuminance $c.R $c.G $c.B
            $shade = [Math]::Max(0.78, [Math]::Min(1.12, $lum / $baseFillLum))
            $fill = Get-TrayBackgroundShade $background $shade $rowFactor
            $alpha = [Math]::Max($c.A, 220)
            $canvas.SetPixel($x, $y, [Drawing.Color]::FromArgb($alpha, $fill.R, $fill.G, $fill.B))
        }
    }

    return $canvas
}

function New-TrayBitmap16([System.Drawing.Image]$source, [System.Drawing.Color]$background, [System.Drawing.Color]$dotColor) {
    $workSize = 64
    $outputSize = 16
    $scaled = New-ScaledTraySource $source $workSize
    try {
        $recolored = New-RecoloredTrayBitmap $scaled $background
        try {
            $final = New-Object System.Drawing.Bitmap $outputSize, $outputSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($final)
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($recolored, 0, 0, $outputSize, $outputSize)
            $brush = New-Object System.Drawing.SolidBrush $dotColor
            $g.FillEllipse($brush, 10, 10, 4, 4)
            $brush.Dispose()
            $g.Dispose()
            return $final
        }
        finally {
            $recolored.Dispose()
        }
    }
    finally {
        $scaled.Dispose()
    }
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

$src = [System.Drawing.Image]::FromFile($PreviewPng)
try {
    foreach ($variant in $variants) {
        $bmp = New-TrayBitmap16 $src $variant.Background $variant.Dot
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
