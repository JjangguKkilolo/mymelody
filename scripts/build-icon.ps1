[CmdletBinding()]
param()

# Package the imagegen artwork into standard Windows ICO resolutions, preserving alpha.
# This performs size/format conversion only; the source illustration stays unchanged.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$brandDirectory = Join-Path $workspaceRoot 'src/MyMelody.App/Assets/Brand'
$sourcePath = Join-Path $brandDirectory 'app-icon.png'
$iconPath = Join-Path $brandDirectory 'app.ico'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$source = [Drawing.Bitmap]::new($sourcePath)
$frames = [Collections.Generic.List[byte[]]]::new()
try {
    if ($source.Width -ne $source.Height) { throw 'The icon source must be square.' }
    foreach ($corner in @(@(0, 0), @(($source.Width - 1), 0), @(0, ($source.Height - 1)), @(($source.Width - 1), ($source.Height - 1)))) {
        if ($source.GetPixel($corner[0], $corner[1]).A -ne 0) { throw 'The generated icon must have transparent corners.' }
    }
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size), 0, 0, $source.Width, $source.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally {
            $stream.Dispose(); $attributes.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
        }
    }
    $output = [IO.File]::Create($iconPath)
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    } finally { $writer.Dispose(); $output.Dispose() }
    [pscustomobject]@{ Icon = $iconPath; Sizes = $sizes -join ', '; Bytes = (Get-Item -LiteralPath $iconPath).Length }
} finally { $source.Dispose() }
