<#
.SYNOPSIS
  Renders banner-image.png (2560x1280) for the README, in the layout the other Fezcode banners use:
  a small spaced header, the lowercase name, a byline, a one-line description, a dashed rule, the
  language tag and the repository URL, with artwork on the right.
.DESCRIPTION
  Uses the app's bundled DM Sans and Manrope and draws the approved raster mark (scripts/mark.ps1).
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'mark.ps1')
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$width = 2560; $height = 1280

$fonts = [System.Drawing.Text.PrivateFontCollection]::new()
$fonts.AddFontFile((Join-Path $MarkAssets 'Fonts/Manrope.ttf'))
$fonts.AddFontFile((Join-Path $MarkAssets 'Fonts/DMSans.ttf'))
function Family([string]$name) { $fonts.Families | Where-Object { $_.Name -eq $name } | Select-Object -First 1 }
# GDI sees the variable fonts' named instances; DM Sans' regular one is "DM Sans 14pt".
$manrope = Family 'Manrope ExtraBold'; $dmsans = Family 'DM Sans 14pt'
if (-not $manrope -or -not $dmsans) { throw "Bundled fonts not found (got: $(($fonts.Families | ForEach-Object Name) -join ', '))" }
function Rgb([string]$hex, [int]$alpha = 255) { [System.Drawing.Color]::FromArgb($alpha, (Get-MarkColor $hex)) }

$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Ground: the Carapace window colours, deepening toward the corners.
    $ground = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Point]::new(0, 0), [System.Drawing.Point]::new($width, $height), (Rgb '#0E1A17'), (Rgb '#0A0F0D'))
    $g.FillRectangle($ground, 0, 0, $width, $height); $ground.Dispose()

    # Artwork: the strip's light, as soft rings of colour radiating from the beetle.
    $cx = 2090; $cy = 600
    $glow = [System.Drawing.Drawing2D.GraphicsPath]::new(); $glow.AddEllipse($cx - 520, $cy - 520, 1040, 1040)
    $halo = [System.Drawing.Drawing2D.PathGradientBrush]::new($glow)
    $halo.CenterColor = Rgb '#35E0B0' 70; $halo.SurroundColors = @((Rgb '#35E0B0' 0))
    $g.FillPath($halo, $glow); $halo.Dispose(); $glow.Dispose()
    $ringColors = @('#35E0B0', '#3CC8FF', '#3C78FF', '#9A5AFF', '#E650DC', '#FF5A5A', '#FF8C28', '#FFD23C', '#78E646', '#35E0B0')
    for ($i = 0; $i -lt $ringColors.Count; $i++) {
        $r = 240 + $i * 40   # outermost ring stays clear of the text column on the left
        $alpha = [int](190 - $i * 15)
        $pen = [System.Drawing.Pen]::new((Rgb $ringColors[$i] $alpha), [single](4.5 - $i * 0.3))
        $g.DrawEllipse($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r); $pen.Dispose()
    }
    Draw-Mark $g ($cx - 190) ($cy - 190) 380

    # Type.
    $muted = [System.Drawing.SolidBrush]::new((Rgb '#8A9A92'))
    $bright = [System.Drawing.SolidBrush]::new((Rgb '#EAF3EE'))
    $accent = [System.Drawing.SolidBrush]::new((Rgb '#35E0B0'))
    $prose = [System.Drawing.SolidBrush]::new((Rgb '#A9B8B0'))
    $mono = [System.Drawing.Font]::new('Consolas', 34)
    $g.DrawString('l i g h t   /   f e z c o d e', $mono, $muted, 128, 104)
    $g.DrawString('ambient, on.', [System.Drawing.Font]::new($dmsans, 36, [System.Drawing.FontStyle]::Italic), $accent, 128, 180)
    $g.DrawString('beetle', [System.Drawing.Font]::new($manrope, 230, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel), $bright, 108, 410)
    $g.DrawString('a strip of light, by fezcode', [System.Drawing.Font]::new($dmsans, 50, [System.Drawing.FontStyle]::Italic), $muted, 124, 680)
    $description = "Control an ELK-BLEDOM LED strip from Windows:`ncolour, brightness, and an ambient mode`nthat follows your screen."
    $g.DrawString($description, [System.Drawing.Font]::new($dmsans, 44), $prose, 124, 800)

    $dash = [System.Drawing.Pen]::new((Rgb '#3B4F44'), 3); $dash.DashPattern = @(8, 6)
    $g.DrawLine($dash, 0, 1100, $width, 1100); $dash.Dispose()
    $g.DrawString('I N   C #', $mono, $muted, 128, 1140)
    $url = 'https://github.com/fezcode/Beetle'
    $urlSize = $g.MeasureString($url, $mono)
    $g.DrawString($url, $mono, $muted, $width - 128 - $urlSize.Width, 1140)
    foreach ($b in @($muted, $bright, $accent, $prose)) { $b.Dispose() }
    $mono.Dispose()
} finally { $g.Dispose() }
$out = Join-Path $root 'banner-image.png'
$bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose(); $fonts.Dispose()
Write-Host "Wrote $out"
