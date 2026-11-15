<#
.SYNOPSIS
  Tekent het DonkiePane-icoon en schrijft src\DonkiePane.App\Assets\DonkiePane.ico (alle formaten) en een voorbeeld-png.

.DESCRIPTION
  Een donkere antracietkleurige tegel met vier panes (2 x 2), waarvan er een theedoekrood is.
  Elk formaat wordt apart getekend, zodat de kleine versies scherp blijven.
  Het script is bewust zuiver ASCII, zodat Windows PowerShell 5.1 het niet beschadigt.

.EXAMPLE
  .\scripts\make-icon.ps1
#>
param(
    [string]$OutIco = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\DonkiePane.App\Assets\DonkiePane.ico'),
    [string]$OutPreview = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\DonkiePane.App\Assets\DonkiePane-256.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size
    # Tegel: bij kleine formaten bijna de hele ruimte en geen rand.
    $inset = if ($size -le 24) { 0.0 } else { $s * 0.03 }
    $tile = New-RoundedPath $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.2)
    $top = [System.Drawing.Color]::FromArgb(255, 0x2E, 0x31, 0x36)
    $bottom = [System.Drawing.Color]::FromArgb(255, 0x17, 0x18, 0x1A)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $s)), $top, $bottom)
    $g.FillPath($bg, $tile)
    if ($size -gt 24) {
        $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), [single][Math]::Max(1, $s / 128))
        $g.DrawPath($edge, $tile)
    }

    # Vier panes. Kleine formaten: kleinere marge en kleinere spleet, dan blijft het leesbaar.
    $margin = if ($size -le 24) { $s * 0.17 } else { $s * 0.21 }
    $gap = if ($size -le 24) { $s * 0.09 } else { $s * 0.07 }
    $cell = ($s - 2 * $margin - $gap) / 2
    $radius = $cell * 0.22
    $red = [System.Drawing.Color]::FromArgb(255, 0xC8, 0x2D, 0x35)
    $redDark = [System.Drawing.Color]::FromArgb(255, 0x9E, 0x1E, 0x25)
    $grey = [System.Drawing.Color]::FromArgb(255, 0x6A, 0x70, 0x7B)
    $greyDark = [System.Drawing.Color]::FromArgb(255, 0x4A, 0x4F, 0x57)

    for ($row = 0; $row -lt 2; $row++) {
        for ($col = 0; $col -lt 2; $col++) {
            $x = $margin + $col * ($cell + $gap)
            $y = $margin + $row * ($cell + $gap)
            $path = New-RoundedPath $x $y $cell $cell $radius
            $isRed = ($row -eq 0 -and $col -eq 0)
            $c1 = if ($isRed) { $red } else { $grey }
            $c2 = if ($isRed) { $redDark } else { $greyDark }
            $rect = New-Object System.Drawing.RectangleF($x, $y, $cell, $cell)
            $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 90.0)
            $g.FillPath($brush, $path)
            $brush.Dispose(); $path.Dispose()
        }
    }
    $g.Dispose()
    return $bmp
}

# Kleine formaten als gewone bitmap (DIB) opslaan: dat lezen alle Windows-onderdelen en ook .NET goed.
# Alleen 256 gaat als png (dat is de gewone afspraak voor dat formaat).
function ConvertTo-IconDib([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $n, $n)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($n * $n * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40); $bw.Write([Int32]$n); $bw.Write([Int32]($n * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]($n * $n * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $n - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $n * 4, $n * 4) }   # onderste rij eerst
    $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $n)))                                  # AND-masker: alles zichtbaar
    $bw.Flush()
    return ,$ms.ToArray()   # met de komma, anders pakt PowerShell de bytes uit elkaar
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , @{ Size = $size; Data = $ms.ToArray() }
    } else {
        $pngs += , @{ Size = $size; Data = (ConvertTo-IconDib $bmp) }
    }
    if ($size -eq 256) { $bmp.Save($OutPreview, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose(); $ms.Dispose()
}

# Een .ico met png-afbeeldingen: kop, een regel per formaat, dan de gegevens.
$dir = Split-Path -Parent $OutIco
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$p.Data.Length); $w.Write([UInt32]$offset)
    $offset += $p.Data.Length
}
foreach ($p in $pngs) { $w.Write($p.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
Write-Host "Icoon geschreven: $OutIco ($($pngs.Count) formaten)"
