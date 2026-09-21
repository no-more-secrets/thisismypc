# Regenerate the installer's wordmark mask from the logo SVG.
# Output: src\ThisIsMyPC.Installer\Assets\wordmark.alpha, an 8-byte header
# (int32 width, int32 height) followed by a raw deflate stream of 8-bit alpha,
# top-down. The installer tints the mask at draw time, so only coverage is kept.
[CmdletBinding()]
param([int]$Width = 480)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$svg = Join-Path $repo 'assets\ThisIsMyPC-Logo-Black_v1.svg'
$assets = Join-Path $repo 'src\ThisIsMyPC.Installer\Assets'
$output = Join-Path $assets 'wordmark.alpha'
$png = Join-Path ([System.IO.Path]::GetTempPath()) 'thisismypc-wordmark.png'

$inkscape = Get-Command inkscape -ErrorAction SilentlyContinue
$inkscapePath = if ($inkscape) { $inkscape.Source } else { 'C:\Program Files\Inkscape\bin\inkscape.exe' }
if (-not (Test-Path $inkscapePath)) { throw 'Inkscape is required to render the wordmark SVG.' }

& $inkscapePath $svg --export-type=png --export-area-drawing --export-width=$Width --export-filename=$png
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $png)) { throw 'Inkscape did not export the wordmark.' }

Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::FromFile($png)
try {
    $height = $bitmap.Height
    $alpha = New-Object byte[] ($bitmap.Width * $height)
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $alpha[$y * $bitmap.Width + $x] = $bitmap.GetPixel($x, $y).A
        }
    }
    New-Item -ItemType Directory -Force $assets | Out-Null
    $file = [System.IO.File]::Create($output)
    try {
        $writer = New-Object System.IO.BinaryWriter($file)
        $writer.Write([int]$bitmap.Width)
        $writer.Write([int]$height)
        $writer.Flush()
        $deflate = New-Object System.IO.Compression.DeflateStream($file, [System.IO.Compression.CompressionLevel]::Optimal, $true)
        $deflate.Write($alpha, 0, $alpha.Length)
        $deflate.Dispose()
    }
    finally { $file.Dispose() }
    Write-Host "Wrote $output ($($bitmap.Width)x$height, $((Get-Item $output).Length) bytes)."
}
finally {
    $bitmap.Dispose()
    Remove-Item $png -ErrorAction SilentlyContinue
}
