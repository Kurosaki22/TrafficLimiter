# Generates app.ico (multi-size, PNG-compressed) and assets/icon.png.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Add-Arrow($g, $color, [float]$cx, [float]$top, [float]$bottom, [bool]$down) {
    # Arrow in 256-unit design space: rounded shaft + triangular head
    $shaftW = 26; $headW = 78; $headH = 52
    $brush = New-Object System.Drawing.SolidBrush $color
    if ($down) {
        $shaft = New-RoundedRect ($cx - $shaftW / 2) $top $shaftW ($bottom - $top - $headH + 14) 8
        $head = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($cx - $headW / 2), ($bottom - $headH)),
            (New-Object System.Drawing.PointF ($cx + $headW / 2), ($bottom - $headH)),
            (New-Object System.Drawing.PointF $cx, $bottom))
    } else {
        $shaft = New-RoundedRect ($cx - $shaftW / 2) ($top + $headH - 14) $shaftW ($bottom - $top - $headH + 14) 8
        $head = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($cx - $headW / 2), ($top + $headH)),
            (New-Object System.Drawing.PointF ($cx + $headW / 2), ($top + $headH)),
            (New-Object System.Drawing.PointF $cx, $top))
    }
    $g.FillPath($brush, $shaft)
    $g.FillPolygon($brush, $head)
    $brush.Dispose()
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    # Rounded square background with a dark blue gradient
    $bg = New-RoundedRect 8 8 240 240 54
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, 256, 256),
        ([System.Drawing.Color]::FromArgb(255, 34, 48, 88)), ([System.Drawing.Color]::FromArgb(255, 12, 17, 32)), 90.0
    $g.FillPath($grad, $bg)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(48, 255, 255, 255)), 3
    $g.DrawPath($pen, $bg)

    # Download (blue) and upload (amber) arrows
    Add-Arrow $g ([System.Drawing.Color]::FromArgb(255, 59, 130, 246)) 90 52 176 $true
    Add-Arrow $g ([System.Drawing.Color]::FromArgb(255, 245, 158, 11)) 166 80 204 $false

    # Violet "limit" bar below the arrows
    $limit = New-RoundedRect 58 206 140 16 8
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 139, 92, 246))), $limit)

    $g.Dispose()
    return $bmp
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @($s, $ms.ToArray())
    if ($s -eq 256) {
        New-Item -ItemType Directory -Force -Path (Join-Path $root 'assets') | Out-Null
        $bmp.Save((Join-Path $root 'assets\icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
}

# Write the .ico container (PNG-compressed entries, supported since Windows Vista)
$out = Join-Path $root 'TrafficLimiter.App\app.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
    $s = $e[0]; $data = $e[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($e in $pngs) { $bw.Write([byte[]]$e[1]) }
$bw.Close()
"Wrote $out ($($pngs.Count) sizes) and assets\icon.png"
