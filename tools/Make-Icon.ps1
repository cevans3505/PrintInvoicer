# Renders src\PrintInvoicer.App\Assets\Icon.xaml into a multi-size PrintInvoicer.ico (PNG-compressed entries).
# Windows PowerShell 5.1 runs in STA mode by default, which WPF rendering needs.
param(
    [string]$PreviewDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$assets = Join-Path $PSScriptRoot '..\src\PrintInvoicer.App\Assets'
$xaml = [IO.File]::ReadAllText((Join-Path $assets 'Icon.xaml'))
$dict = [Windows.Markup.XamlReader]::Parse($xaml)
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Render([Windows.Media.Drawing]$drawing, [int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
    $dc.DrawDrawing($drawing)
    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

$images = foreach ($s in $sizes) {
    $drawing = if ($s -le 24) { $dict['Small'] } else { $dict['Full'] }
    [pscustomobject]@{ Size = $s; Png = (Render $drawing $s) }
}

# ICO: 6-byte header, 16-byte directory entry per image, then the PNG data.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$img.Png.Length); $w.Write([uint32]$offset)
    $offset += $img.Png.Length
}
foreach ($img in $images) { $w.Write($img.Png) }
$w.Flush()

$ico = Join-Path $assets 'PrintInvoicer.ico'
[IO.File]::WriteAllBytes($ico, $out.ToArray())
Write-Host "Wrote $ico ($($images.Count) sizes)"

if ($PreviewDir) {
    New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
    foreach ($img in $images) { [IO.File]::WriteAllBytes((Join-Path $PreviewDir "icon-$($img.Size).png"), $img.Png) }
}
