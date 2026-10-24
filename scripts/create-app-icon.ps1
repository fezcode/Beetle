<#
.SYNOPSIS
  Renders Beetle.Desktop/Assets/beetle.svg into beetle.png (256 px) and a multi-size beetle.ico.
.DESCRIPTION
  The SVG is the single source for the mark (see scripts/mark.ps1), so the Windows icon matches the
  in-app mark. Re-run after editing the SVG.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'mark.ps1')

function Png-Bytes($bitmap) {
    $ms = [System.IO.MemoryStream]::new()
    try { $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); , $ms.ToArray() } finally { $ms.Dispose() }
}

$large = New-MarkBitmap 256
try { $large.Save((Join-Path $MarkAssets 'beetle.png'), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $large.Dispose() }

# A PNG-backed ICO keeps alpha. Several sizes, each drawn from the vector rather than scaled down
# from 256 px, keep Explorer, the taskbar and shortcuts crisp.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = foreach ($s in $sizes) { $b = New-MarkBitmap $s; try { , (Png-Bytes $b) } finally { $b.Dispose() } }
$stream = [System.IO.File]::Create((Join-Path $MarkAssets 'beetle.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256 in an ICO directory
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
Write-Host "Wrote beetle.png and beetle.ico ($($sizes -join ', ') px) to $MarkAssets"
