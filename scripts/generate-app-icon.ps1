Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$pngPath = Join-Path $root "src\SelectiveVpnRouter.App\SelectiveVpnRouter.icon.png"
$icoPath = Join-Path $root "src\SelectiveVpnRouter.App\SelectiveVpnRouter.ico"
$sizes = @(16, 32, 48, 64, 128, 256)

if (-not (Test-Path $pngPath)) {
    throw "Source PNG not found: $pngPath"
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
    } finally { $bmp.UnlockBits($data) }
}

$src = [System.Drawing.Image]::FromFile($pngPath)
$images = New-Object System.Collections.Generic.List[object]
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $pair = Get-BitmapBytes $bmp
    $images.Add([pscustomobject]@{ Bitmap = $bmp; Data = $pair[0]; Size = $pair[1] })
}
$src.Dispose()

$fs = [IO.File]::Create($icoPath)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$images.Count)
$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $w = if ($img.Bitmap.Width -ge 256) { [byte]0 } else { [byte]$img.Bitmap.Width }
    $h = if ($img.Bitmap.Height -ge 256) { [byte]0 } else { [byte]$img.Bitmap.Height }
    $bw.Write($w); $bw.Write($h); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$img.Size); $bw.Write([UInt32]$offset)
    $offset += $img.Size
}
foreach ($img in $images) { $bw.Write($img.Data) }
$bw.Close(); $fs.Close()
foreach ($img in $images) { $img.Bitmap.Dispose() }
Write-Host "Created $icoPath from $pngPath"