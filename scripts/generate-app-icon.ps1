# Builds production vpn-route-icon.ico from assets/branding/vpn-route-icon-source.png
# Standalone square icon only (no wordmark). Tight crop, multi-size ICO for Windows shell.
param(
    [string]$SourcePng,
    [string]$OutputIco,
    [string]$AppCopyIco,
    [int]$CanvasSize = 256,
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256),
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
if (-not $SourcePng) {
    $SourcePng = Join-Path $root "assets\branding\vpn-route-icon-source.png"
}
if (-not $OutputIco) {
    $OutputIco = Join-Path $root "assets\branding\vpn-route-icon.ico"
}
if (-not $AppCopyIco) {
    $AppCopyIco = Join-Path $root "src\SelectiveVpnRouter.App\vpn-route-icon.ico"
}

if (-not (Test-Path $SourcePng)) {
    throw "Source PNG not found: $SourcePng"
}

function Test-ContentPixel([System.Drawing.Color]$c) {
    if ($c.A -lt 16) {
        return $false
    }

    # Trim outer black letterbox; keep navy squircle and graphic content.
    return ($c.R + $c.G + $c.B) -gt 24
}

function Get-TightBounds([System.Drawing.Bitmap]$bmp) {
    $minX = $bmp.Width
    $minY = $bmp.Height
    $maxX = 0
    $maxY = 0
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            if (Test-ContentPixel $bmp.GetPixel($x, $y)) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        throw "Could not detect icon content bounds in $SourcePng"
    }

    return [pscustomobject]@{ MinX = $minX; MinY = $minY; MaxX = $maxX; MaxY = $maxY }
}

function New-TightCanvas([System.Drawing.Image]$src, [int]$size) {
    $srcBmp = New-Object System.Drawing.Bitmap $src
    try {
        $b = Get-TightBounds $srcBmp
        $cropW = $b.MaxX - $b.MinX + 1
        $cropH = $b.MaxY - $b.MinY + 1
        $side = [Math]::Max($cropW, $cropH)
        $pad = [Math]::Max(1, [int][Math]::Round($side * 0.02))
        $canvas = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $dest = [Math]::Max(1, $size - (2 * $pad))
        $offset = [int](($size - $dest) / 2)
        $srcRect = New-Object System.Drawing.Rectangle $b.MinX, $b.MinY, $cropW, $cropH
        $destRect = New-Object System.Drawing.Rectangle $offset, $offset, $dest, $dest
        $g.DrawImage($srcBmp, $destRect, $srcRect, [Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        return $canvas
    }
    finally {
        $srcBmp.Dispose()
    }
}

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

$src = [System.Drawing.Image]::FromFile($SourcePng)
$tight256 = $null
try {
    $tight256 = New-TightCanvas $src $CanvasSize
    $previewPng = Join-Path (Split-Path $OutputIco) "vpn-route-icon-tight-256.png"
    $tight256.Save($previewPng, [Drawing.Imaging.ImageFormat]::Png)

    $images = New-Object System.Collections.Generic.List[object]
    foreach ($size in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.DrawImage($tight256, 0, 0, $size, $size)
        $g.Dispose()
        $pair = Get-BitmapBytes $bmp
        $images.Add([pscustomobject]@{ Bitmap = $bmp; Data = $pair[0]; Size = $pair[1] })
    }

    $dir = Split-Path $OutputIco
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $fs = [IO.File]::Create($OutputIco)
    $bw = New-Object IO.BinaryWriter $fs
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$images.Count)
    $offset = 6 + (16 * $images.Count)
    foreach ($img in $images) {
        $w = if ($img.Bitmap.Width -ge 256) { [byte]0 } else { [byte]$img.Bitmap.Width }
        $h = if ($img.Bitmap.Height -ge 256) { [byte]0 } else { [byte]$img.Bitmap.Height }
        $bw.Write($w)
        $bw.Write($h)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$img.Size)
        $bw.Write([UInt32]$offset)
        $offset += $img.Size
    }
    foreach ($img in $images) { $bw.Write($img.Data) }
    $bw.Close()
    $fs.Close()
    foreach ($img in $images) { $img.Bitmap.Dispose() }

    Copy-Item -Path $OutputIco -Destination $AppCopyIco -Force
    if ($Quiet) {
        $detail = @(
            "Created $OutputIco",
            "Copied to $AppCopyIco",
            "Preview PNG: $previewPng",
            "ICO sizes: $($Sizes -join ', ')"
        )
        foreach ($line in $detail) {
            if (Get-Command Write-SvrUpdateDetail -ErrorAction SilentlyContinue) {
                Write-SvrUpdateDetail $line
            }
            elseif (Get-Command Write-SvrUpdateLogLine -ErrorAction SilentlyContinue) {
                Write-SvrUpdateLogLine $line
            }
        }
    }
    else {
        Write-Host "Created $OutputIco"
        Write-Host "Copied to $AppCopyIco"
        Write-Host "Preview PNG: $previewPng"
        Write-Host "ICO sizes: $($Sizes -join ', ')"
    }
}
finally {
    if ($tight256) { $tight256.Dispose() }
    $src.Dispose()
}
