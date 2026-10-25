# Shared banner renderer, using the same approved raster master as the app icon.
Add-Type -AssemblyName System.Drawing
$script:MarkAssets = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Beetle.Desktop/Assets'))

function Draw-Mark($g, [double]$x, [double]$y, [double]$size) {
    $source = [System.Drawing.Image]::FromFile((Join-Path $script:MarkAssets 'icon-master.png'))
    $state = $g.Save()
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($source, [single]$x, [single]$y, [single]$size, [single]$size)
    } finally { $g.Restore($state); $source.Dispose() }
}

function New-MarkBitmap([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $g.Clear([System.Drawing.Color]::Transparent); Draw-Mark $g 0 0 $size } finally { $g.Dispose() }
    $bitmap
}
