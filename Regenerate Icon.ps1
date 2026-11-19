[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$sourcePath = Join-Path $projectRoot "textures\RhythiaSquircle.png"
$outputPath = Join-Path $projectRoot "textures\icon.ico"
$temporaryPath = "$outputPath.tmp"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Logo image not found: $sourcePath"
}

Add-Type -AssemblyName System.Drawing

$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $pngImages = New-Object "System.Collections.Generic.List[byte[]]"
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
        )
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

                $scale = [Math]::Min($size / $sourceImage.Width, $size / $sourceImage.Height)
                $width = [int][Math]::Round($sourceImage.Width * $scale)
                $height = [int][Math]::Round($sourceImage.Height * $scale)
                $x = [int][Math]::Floor(($size - $width) / 2)
                $y = [int][Math]::Floor(($size - $height) / 2)
                $graphics.DrawImage($sourceImage, $x, $y, $width, $height)
            }
            finally {
                $graphics.Dispose()
            }

            $memoryStream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngImages.Add([byte[]]$memoryStream.ToArray())
            }
            finally {
                $memoryStream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

$directoryEntrySize = 16
$imageOffset = 6 + ($directoryEntrySize * $pngImages.Count)
$fileStream = [System.IO.File]::Open(
    $temporaryPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None
)
try {
    $writer = New-Object System.IO.BinaryWriter($fileStream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$pngImages.Count)

        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $dimension = if ($size -eq 256) { 0 } else { [byte]$size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$pngImages[$index].Length)
            $writer.Write([UInt32]$imageOffset)
            $imageOffset += $pngImages[$index].Length
        }

        foreach ($pngImage in $pngImages) {
            $writer.Write($pngImage)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}

Move-Item -LiteralPath $temporaryPath -Destination $outputPath -Force
Write-Output "Generated icon: $outputPath"
