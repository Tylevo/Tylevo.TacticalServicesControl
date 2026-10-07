[CmdletBinding()]
param([string] $OutputDirectory)

# Original geometric artwork. This script never reads or edits an existing image.
# The PNG renderer and SVG exporter consume the same 96-unit geometry.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Finalize freshly rendered coverage as one exact solid color. GDI+ can round
# overlapping antialiased strokes by one RGB unit; this keeps the requested
# palette exact while retaining the alpha coverage of the original geometry.
if (-not ('TscGlyphSolidColor' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class TscGlyphSolidColor
{
    public static void FinalizeCoverage(Bitmap bitmap, Color color)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride <= 0) throw new InvalidOperationException("Unexpected generated bitmap stride.");
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                int i = y * data.Stride + x * 4;
                bool visible = pixels[i + 3] != 0;
                pixels[i] = visible ? color.B : (byte)0;
                pixels[i + 1] = visible ? color.G : (byte)0;
                pixels[i + 2] = visible ? color.R : (byte)0;
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
'@
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
$taskRoot = [IO.Path]::GetFullPath($OutputDirectory)
$utf8 = [Text.UTF8Encoding]::new($false)
$palette = [ordered]@{
    neutral_512 = '#dcd8c8'
    amber_512 = '#e8b967'
    mask_512 = '#ffffff'
}

function Poly([string] $Points) { @{ Kind = 'polygon'; Points = $Points } }
function Oval([double] $X, [double] $Y, [double] $W, [double] $H) {
    @{ Kind = 'ellipse'; X = $X; Y = $Y; W = $W; H = $H }
}
function Compound([object[]] $Contours) { @{ Kind = 'compound'; Contours = $Contours } }
function Stroke([string] $Points, [double] $Width = 3.2) {
    @{ Kind = 'line'; Points = $Points; Width = $Width }
}
function Ring([double] $X, [double] $Y, [double] $Diameter, [double] $Width = 3.2) {
    @{ Kind = 'ring'; X = $X; Y = $Y; W = $Diameter; H = $Diameter; Width = $Width }
}
function GlyphGroup([object[]] $Children, [double] $X = 0, [double] $Y = 0, [double] $Scale = 1) {
    @{ Kind = 'group'; Children = $Children; X = $X; Y = $Y; Scale = $Scale }
}

$a10 = @(
    (Compound @(
        (Poly '48,7 44,18 43,43 12,46 12,54 43,54 44,73 29,79 29,84 44,81 46,89 50,89 52,81 67,84 67,79 52,73 53,54 84,54 84,46 53,43 52,18')
    )),
    (Compound @((Oval 31 57 10 18), (Oval 34 60 4 6))),
    (Compound @((Oval 55 57 10 18), (Oval 58 60 4 6)))
)
$helicopter = @(
    (Stroke '12,21 70,21' 3.2),
    (Stroke '41,21 41,32' 3.2),
    (Compound @(
        (Poly '14,43 25,32 43,31 55,34 61,39 76,38 84,28 88,28 85,42 65,45 58,52 25,52 14,47'),
        (Poly '25,36 34,35 33,43 18,43'),
        (Poly '38,35 44,36 49,39 49,44 38,44')
    )),
    (Stroke '27,52 25,57' 3.2),
    (Stroke '53,52 55,57' 3.2),
    (Compound @((Oval 22.5 55 5 5))),
    (Compound @((Oval 52.5 55 5 5)))
)
$radar = @(
    (Ring 17 17 62 3.2),
    (Ring 30 30 36 3.2),
    (Stroke '48,48 64,23' 3.2),
    (Compound @((Oval 43.5 43.5 9 9))),
    (Compound @((Oval 29 31 7 7))),
    (Compound @((Oval 62 52 7 7))),
    (Compound @((Oval 35 66 7 7)))
)
$focusedRadar = @(
    (Ring 20 20 56 3.2),
    (Ring 33 33 30 3.2),
    (Stroke '48,48 65,28' 3.2),
    (Compound @((Oval 44 44 8 8))),
    (Compound @((Oval 57 32 7 7))),
    (Stroke '12,28 12,12 28,12' 3.2),
    (Stroke '68,12 84,12 84,28' 3.2),
    (Stroke '12,68 12,84 28,84' 3.2),
    (Stroke '68,84 84,84 84,68' 3.2)
)
$icons = [ordered]@{
    a10_strafe = $a10
    double_pass = @((GlyphGroup $a10 -3 -2 0.74), (GlyphGroup $a10 27 25 0.74))
    extraction = @((GlyphGroup $helicopter), (Stroke '48,86 48,67' 3.6), (Stroke '40,75 48,67 56,75' 3.6))
    priority_exfil = @(
        (GlyphGroup $helicopter),
        (Stroke '48,54 48,63' 2.8),
        (Stroke '32,73 48,63 64,73' 2.8),
        (Compound @((Poly '32,73 64,73 64,89 32,89'), (Poly '35,76 61,76 61,86 35,86'))),
        (Stroke '48,74 48,88' 2.8)
    )
    uav_recon = $radar
    focused_sweep = $focusedRadar
}

function Number([double] $Value) { $Value.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }
function Points([string] $Text) {
    $result = [Collections.Generic.List[Drawing.PointF]]::new()
    foreach ($pair in $Text.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
        $parts = $pair.Split(',')
        $x = [single]::Parse($parts[0], [Globalization.CultureInfo]::InvariantCulture)
        $y = [single]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
        $result.Add([Drawing.PointF]::new($x, $y))
    }
    return ,$result.ToArray()
}

function Draw-Shapes([Drawing.Graphics] $Graphics, [object[]] $Shapes, [Drawing.Color] $Color) {
    foreach ($shape in $Shapes) {
        if ($shape.Kind -eq 'group') {
            $state = $Graphics.Save()
            try {
                $Graphics.TranslateTransform([single] $shape.X, [single] $shape.Y)
                $Graphics.ScaleTransform([single] $shape.Scale, [single] $shape.Scale)
                Draw-Shapes $Graphics $shape.Children $Color
            } finally { $Graphics.Restore($state) }
        } elseif ($shape.Kind -eq 'compound') {
            $path = [Drawing.Drawing2D.GraphicsPath]::new([Drawing.Drawing2D.FillMode]::Alternate)
            $brush = [Drawing.SolidBrush]::new($Color)
            try {
                foreach ($contour in $shape.Contours) {
                    if ($contour.Kind -eq 'polygon') {
                        $path.AddPolygon((Points $contour.Points))
                    } else {
                        $path.AddEllipse([single] $contour.X, [single] $contour.Y, [single] $contour.W, [single] $contour.H)
                    }
                }
                $Graphics.FillPath($brush, $path)
            } finally { $brush.Dispose(); $path.Dispose() }
        } else {
            $pen = [Drawing.Pen]::new($Color, [single] $shape.Width)
            try {
                $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
                $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
                $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
                if ($shape.Kind -eq 'line') { $Graphics.DrawLines($pen, (Points $shape.Points)) }
                else { $Graphics.DrawEllipse($pen, [single] $shape.X, [single] $shape.Y, [single] $shape.W, [single] $shape.H) }
            } finally { $pen.Dispose() }
        }
    }
}

function Draw-Icon([Drawing.Graphics] $Graphics, [object[]] $Shapes, [Drawing.Color] $Color,
    [single] $X, [single] $Y, [single] $Size) {
    $state = $Graphics.Save()
    try {
        $Graphics.TranslateTransform($X, $Y)
        $Graphics.ScaleTransform($Size / 96.0, $Size / 96.0)
        Draw-Shapes $Graphics $Shapes $Color
    } finally { $Graphics.Restore($state) }
}

function Svg-Shapes([object[]] $Shapes, [string] $Hex) {
    $elements = [Collections.Generic.List[string]]::new()
    foreach ($shape in $Shapes) {
        if ($shape.Kind -eq 'group') {
            $children = Svg-Shapes $shape.Children $Hex
            $elements.Add('<g transform="translate(' + (Number $shape.X) + ' ' + (Number $shape.Y) + ') scale(' + (Number $shape.Scale) + ')">' + $children + '</g>')
        } elseif ($shape.Kind -eq 'compound') {
            $paths = [Collections.Generic.List[string]]::new()
            foreach ($contour in $shape.Contours) {
                if ($contour.Kind -eq 'polygon') { $paths.Add('M' + $contour.Points.Replace(' ', ' L') + ' Z') }
                else {
                    $rx = [double] $contour.W / 2
                    $ry = [double] $contour.H / 2
                    $cy = [double] $contour.Y + $ry
                    $left = Number $contour.X
                    $right = Number ($contour.X + $contour.W)
                    $arc = ' A' + (Number $rx) + ',' + (Number $ry) + ' 0 1,0 '
                    $paths.Add('M' + $left + ',' + (Number $cy) + $arc + $right + ',' + (Number $cy) + $arc + $left + ',' + (Number $cy) + ' Z')
                }
            }
            $elements.Add('<path fill="' + $Hex + '" fill-rule="evenodd" d="' + ($paths -join ' ') + '"/>')
        } elseif ($shape.Kind -eq 'line') {
            $elements.Add('<polyline fill="none" stroke="' + $Hex + '" stroke-width="' + (Number $shape.Width) + '" stroke-linecap="round" stroke-linejoin="round" points="' + $shape.Points + '"/>')
        } else {
            $elements.Add('<ellipse fill="none" stroke="' + $Hex + '" stroke-width="' + (Number $shape.Width) + '" cx="' + (Number ($shape.X + $shape.W / 2)) + '" cy="' + (Number ($shape.Y + $shape.H / 2)) + '" rx="' + (Number ($shape.W / 2)) + '" ry="' + (Number ($shape.H / 2)) + '"/>')
        }
    }
    return $elements -join "`n"
}

function New-Canvas([int] $Width, [int] $Height) {
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    return $bitmap
}
function Configure-Graphics([Drawing.Graphics] $Graphics) {
    $Graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $Graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
}

$files = [Collections.Generic.List[object]]::new()
foreach ($variant in $palette.Keys) {
    $pngDirectory = Join-Path $taskRoot ('generated/' + $variant)
    $svgDirectory = Join-Path $taskRoot ('source/' + $variant)
    [void] [IO.Directory]::CreateDirectory($pngDirectory)
    [void] [IO.Directory]::CreateDirectory($svgDirectory)
    $color = [Drawing.ColorTranslator]::FromHtml($palette[$variant])
    foreach ($name in $icons.Keys) {
        $pngPath = Join-Path $pngDirectory ($name + '.png')
        $svgPath = Join-Path $svgDirectory ($name + '.svg')
        $bitmap = New-Canvas 512 512
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            Configure-Graphics $graphics
            $graphics.Clear([Drawing.Color]::Transparent)
            Draw-Icon $graphics $icons[$name] $color 0 0 512
            [TscGlyphSolidColor]::FinalizeCoverage($bitmap, $color)
            $bitmap.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
        $svg = '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 96 96" role="img"><title>' + $name + '</title>' + "`n" + (Svg-Shapes $icons[$name] $palette[$variant]) + "`n</svg>`n"
        [IO.File]::WriteAllText($svgPath, $svg, $utf8)
        $files.Add([pscustomobject]@{
            name = $name; variant = $variant; color = $palette[$variant]; width = 512; height = 512
            png = 'generated/' + $variant + '/' + $name + '.png'
            sha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash
            svg = 'source/' + $variant + '/' + $name + '.svg'
            svgSha256 = (Get-FileHash -LiteralPath $svgPath -Algorithm SHA256).Hash
        })
    }
}

# Review sheet rendered directly from the same vectors at each target size.
$previewPath = Join-Path $taskRoot 'native-icons-preview.png'
$preview = New-Canvas 1140 1060
$previewGraphics = [Drawing.Graphics]::FromImage($preview)
$titleFont = [Drawing.Font]::new('Arial', 22, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$labelFont = [Drawing.Font]::new('Arial', 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$nameFont = [Drawing.Font]::new('Arial', 15, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$textBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#dcd8c8'))
$mutedBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#969284'))
$dividerPen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#313635'), 1)
try {
    Configure-Graphics $previewGraphics
    $previewGraphics.Clear([Drawing.ColorTranslator]::FromHtml('#121515'))
    $previewGraphics.DrawString('TSC / NATIVE SERVICE GLYPHS', $titleFont, $textBrush, 24, 20)
    $previewGraphics.DrawString('Original vector geometry. Transparent icons. Actual target-size previews.', $labelFont, $mutedBrush, 24, 53)
    $columns = @(
        @{ X = 286; Size = 52; Color = '#dcd8c8'; Label = '52 px / ivory' },
        @{ X = 406; Size = 72; Color = '#dcd8c8'; Label = '72 px / ivory' },
        @{ X = 562; Size = 128; Color = '#dcd8c8'; Label = '128 px / ivory' },
        @{ X = 740; Size = 52; Color = '#e8b967'; Label = '52 px / amber' },
        @{ X = 860; Size = 72; Color = '#e8b967'; Label = '72 px / amber' },
        @{ X = 1016; Size = 128; Color = '#e8b967'; Label = '128 px / amber' }
    )
    foreach ($column in $columns) {
        $previewGraphics.DrawString($column.Label, $labelFont, $mutedBrush, [single] ($column.X - 48), 93)
    }
    $row = 0
    foreach ($name in $icons.Keys) {
        $centerY = 197 + $row * 150
        $previewGraphics.DrawLine($dividerPen, 24, $centerY - 74, 1116, $centerY - 74)
        $previewGraphics.DrawString($name.ToUpperInvariant().Replace('_', ' '), $nameFont, $textBrush, 24, $centerY - 9)
        foreach ($column in $columns) {
            Draw-Icon $previewGraphics $icons[$name] ([Drawing.ColorTranslator]::FromHtml($column.Color)) `
                ([single] ($column.X - $column.Size / 2)) ([single] ($centerY - $column.Size / 2)) $column.Size
        }
        $row++
    }
    $previewGraphics.DrawString('White mask variants are included in the manifest. No backgrounds, gradients, shadows, or raster source images.', $labelFont, $mutedBrush, 24, 1030)
    $preview.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $dividerPen.Dispose(); $mutedBrush.Dispose(); $textBrush.Dispose()
    $nameFont.Dispose(); $labelFont.Dispose(); $titleFont.Dispose()
    $previewGraphics.Dispose(); $preview.Dispose()
}
$manifest = [ordered]@{
    schemaVersion = 1
    design = 'Original simplified service glyphs drawn from deterministic 96-unit code geometry.'
    renderer = 'System.Drawing GDI+; 512x512 Format32bppArgb, transparent background, antialiasing.'
    source = 'Generate-NativeIcons.ps1'
    sourceSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    preview = 'native-icons-preview.png'
    previewSizes = @(52, 72, 128)
    previewBackground = '#121515'
    files = $files.ToArray()
}
[IO.File]::WriteAllText((Join-Path $taskRoot 'manifest.json'), ($manifest | ConvertTo-Json -Depth 8) + "`n", $utf8)
Write-Output ('Generated ' + $files.Count + ' PNGs, ' + $files.Count + ' SVG sources, and ' + $previewPath)
