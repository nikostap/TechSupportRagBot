Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'TechSupportRagBot/wwwroot'
$sourcePath = Join-Path $web 'images/class-engineering-transparent.png'
$source = [System.Drawing.Image]::FromFile($sourcePath)
$crop = [System.Drawing.Rectangle]::new(180, 60, 1040, 700)

function New-LogoBitmap([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $width = $size * 0.94
        $height = $width * $crop.Height / $crop.Width
        $destination = [System.Drawing.RectangleF]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height)
        $graphics.DrawImage($source, $destination, $crop, [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

try {
    $entries = @()
    foreach ($size in @(16, 32, 48)) {
        $bitmap = New-LogoBitmap $size
        try {
            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $entries += [PSCustomObject]@{ Size = $size; Bytes = $stream.ToArray() }
            }
            finally { $stream.Dispose() }
            if ($size -eq 32) {
                $bitmap.Save((Join-Path $web 'images/class-engineering-mark-32.png'), [System.Drawing.Imaging.ImageFormat]::Png)
            }
        }
        finally { $bitmap.Dispose() }
    }

    $touch = New-LogoBitmap 180
    try { $touch.Save((Join-Path $web 'images/apple-touch-icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $touch.Dispose() }

    $file = [System.IO.File]::Create((Join-Path $web 'favicon.ico'))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$entries.Count)
        $offset = 6 + 16 * $entries.Count
        foreach ($entry in $entries) {
            $writer.Write([byte]$entry.Size)
            $writer.Write([byte]$entry.Size)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$entry.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $entry.Bytes.Length
        }
        foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Bytes) }
    }
    finally { $writer.Dispose() }
}
finally { $source.Dispose() }
