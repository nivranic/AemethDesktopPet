# Build a Windows ICO resource from the existing pet atlas; no downloaded artwork.
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required.' }
Add-Type -AssemblyName System.Drawing
$atlasPath=Join-Path $PSScriptRoot '..\assets\aemeath-mini\spritesheet.png'
$atlas=[Drawing.Bitmap]::FromFile([IO.Path]::GetFullPath($atlasPath))
$iconPath=Join-Path $PSScriptRoot 'pet.ico'
try {
    $entries=@()
    foreach($size in @(16,32,48,64,128)) {
        $bitmap=[Drawing.Bitmap]::new($size,$size)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $stream=[IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
            $width=[int][Math]::Round($size*192/208)
            $graphics.DrawImage($atlas,[Drawing.Rectangle]::new([int](($size-$width)/2),0,$width,$size),0,0,192,208,[Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $entries+=@{size=$size;bytes=$stream.ToArray()}
        } finally { $stream.Dispose();$graphics.Dispose();$bitmap.Dispose() }
    }
    $output=[IO.File]::Create($iconPath)
    $writer=[IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$entries.Count)
        $offset=6+16*$entries.Count
        foreach($entry in $entries) {
            $writer.Write([byte]$entry.size);$writer.Write([byte]$entry.size)
            $writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32)
            $writer.Write([uint32]$entry.bytes.Length);$writer.Write([uint32]$offset)
            $offset+=$entry.bytes.Length
        }
        foreach($entry in $entries) { $writer.Write([byte[]]$entry.bytes) }
    } finally { $writer.Dispose() }
} finally { $atlas.Dispose() }
Get-Item -LiteralPath $iconPath | Select-Object FullName,Length
