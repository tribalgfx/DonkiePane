<#
.SYNOPSIS
  Tekent het DonkiePane-icoon en schrijft src\DonkiePane.App\Assets\DonkiePane.ico (alle formaten) en een voorbeeld-png.

.DESCRIPTION
  Een tegel met diagonaal verloop in theedoekrood en een witte P met ezeloren (Pane + Donkie), in dezelfde familie als de andere Donkie-apps.
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
    # Tegel met diagonaal verloop (zelfde familie als de andere Donkie-apps, maar in theedoekrood).
    $inset = if ($size -le 24) { 0.0 } else { $s * 0.03 }
    $tile = New-RoundedPath $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.22)
    $c1 = [System.Drawing.Color]::FromArgb(255, 0xFF, 0x93, 0x6B)
    $c2 = [System.Drawing.Color]::FromArgb(255, 0xD6, 0x2F, 0x5A)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.RectangleF(0, 0, $s, $s)), $c1, $c2, 45.0)
    $g.FillPath($gradient, $tile)

    # Het glyph: een witte "P" (van Pane) met ezeloren. Alles in een pad, zodat de schaduw niet dubbel valt.
    function New-Glyph([single]$s, [single]$dy, [bool]$small) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding

        # Oren: twee lange ellipsen, naar buiten gedraaid.
        $earW = if ($small) { 0.14 } else { 0.115 }
        foreach ($side in -1, 1) {
            $ear = New-Object System.Drawing.Drawing2D.GraphicsPath
            $cx = (0.5 + $side * 0.16) * $s
            $cy = (0.285 + $dy) * $s
            $ear.AddEllipse([single]($cx - $earW * $s / 2), [single]($cy - 0.15 * $s), [single]($earW * $s), [single](0.30 * $s))
            $m = New-Object System.Drawing.Drawing2D.Matrix
            $m.RotateAt([single]($side * 17), (New-Object System.Drawing.PointF([single]$cx, [single]$cy)))
            $ear.Transform($m)
            $path.AddPath($ear, $false)
        }

        # Steel en kom van de P.
        $stem = New-RoundedPath ([single](0.30 * $s)) ([single]((0.38 + $dy) * $s)) ([single](0.20 * $s)) ([single](0.44 * $s)) ([single](0.035 * $s))
        $bowl = New-RoundedPath ([single](0.30 * $s)) ([single]((0.38 + $dy) * $s)) ([single](0.43 * $s)) ([single](0.29 * $s)) ([single](0.13 * $s))
        $path.AddPath($stem, $false)
        $path.AddPath($bowl, $false)
        return , $path
    }

    $small = $size -le 24
    # Zachte schaduw: een paar doorzichtige lagen, steeds iets lager.
    if ($size -ge 32) {
        foreach ($layer in 1, 2, 3) {
            $shadowPath = New-Glyph $s ([single](0.006 * $layer)) $small
            $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(22, 60, 0, 20))), $shadowPath)
        }
    }
    $glyph = New-Glyph $s 0 $small
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xFF, 0xF6, 0xF3))), $glyph)

    # Sleuf in de kom (de snuit): gevuld met hetzelfde verloop, zodat het lijkt of de tegel erdoorheen schijnt.
    $hole = New-RoundedPath ([single](0.465 * $s)) ([single](0.465 * $s)) ([single](0.145 * $s)) ([single](0.095 * $s)) ([single](0.047 * $s))
    $g.FillPath($gradient, $hole)

    # Donkere lok tussen de oren.
    $tuft = New-Object System.Drawing.RectangleF([single](0.435 * $s), [single](0.335 * $s), [single](0.15 * $s), [single](0.085 * $s))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x3B, 0x1F, 0x3A))), $tuft)
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
