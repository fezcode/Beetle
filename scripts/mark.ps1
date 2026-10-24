# Shared by create-app-icon.ps1 and create-banner.ps1: draws Beetle.Desktop/Assets/beetle.svg (its
# rect, line and ellipse shapes, in the colours written in the file) with System.Drawing.
Add-Type -AssemblyName System.Drawing
$script:MarkAssets = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Beetle.Desktop/Assets'))
[xml]$script:MarkSvg = Get-Content -LiteralPath (Join-Path $script:MarkAssets 'beetle.svg') -Raw
$script:MarkCulture = [Globalization.CultureInfo]::InvariantCulture

function Get-MarkNumber($node, [string]$name) { [double]::Parse($node.GetAttribute($name), $script:MarkCulture) }
function Get-MarkColor([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# Draws the 64x64 mark into $g at ($x, $y), scaled to $size pixels.
function Draw-Mark($g, [double]$x, [double]$y, [double]$size) {
    $state = $g.Save()
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TranslateTransform($x, $y)
        $g.ScaleTransform($size / 64.0, $size / 64.0)
        foreach ($node in $script:MarkSvg.svg.ChildNodes) {
            if ($node.NodeType -ne 'Element') { continue }
            switch ($node.LocalName) {
                'rect' {
                    $rx = Get-MarkNumber $node 'x'; $ry = Get-MarkNumber $node 'y'
                    $w = Get-MarkNumber $node 'width'; $h = Get-MarkNumber $node 'height'; $d = 2 * (Get-MarkNumber $node 'rx')
                    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
                    $path.AddArc($rx, $ry, $d, $d, 180, 90); $path.AddArc($rx + $w - $d, $ry, $d, $d, 270, 90)
                    $path.AddArc($rx + $w - $d, $ry + $h - $d, $d, $d, 0, 90); $path.AddArc($rx, $ry + $h - $d, $d, $d, 90, 90)
                    $path.CloseFigure()
                    $fill = [System.Drawing.SolidBrush]::new((Get-MarkColor $node.fill))
                    $pen = [System.Drawing.Pen]::new((Get-MarkColor $node.stroke), (Get-MarkNumber $node 'stroke-width'))
                    try { $g.FillPath($fill, $path); $g.DrawPath($pen, $path) } finally { $fill.Dispose(); $pen.Dispose(); $path.Dispose() }
                }
                'line' {
                    $pen = [System.Drawing.Pen]::new((Get-MarkColor $node.stroke), (Get-MarkNumber $node 'stroke-width'))
                    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                    try { $g.DrawLine($pen, (Get-MarkNumber $node 'x1'), (Get-MarkNumber $node 'y1'), (Get-MarkNumber $node 'x2'), (Get-MarkNumber $node 'y2')) } finally { $pen.Dispose() }
                }
                'ellipse' {
                    $erx = Get-MarkNumber $node 'rx'; $ery = Get-MarkNumber $node 'ry'
                    $fill = [System.Drawing.SolidBrush]::new((Get-MarkColor $node.fill))
                    try { $g.FillEllipse($fill, (Get-MarkNumber $node 'cx') - $erx, (Get-MarkNumber $node 'cy') - $ery, 2 * $erx, 2 * $ery) } finally { $fill.Dispose() }
                }
            }
        }
    } finally { $g.Restore($state) }
}

# A square bitmap of the mark at $size pixels; the caller disposes it.
function New-MarkBitmap([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try { Draw-Mark $g 0 0 $size } finally { $g.Dispose() }
    $bitmap
}
