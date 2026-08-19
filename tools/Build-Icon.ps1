param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\Moniswitch.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 64.0
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 25, 27, 23))
    $frame = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 240, 240, 233), [single](3.5 * $scale))
    $signal = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 111, 67), [single](4.5 * $scale))
    $frame.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $signal.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $signal.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $signal.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $radius = [single](10 * $scale)
    $diameter = [single](20 * $scale)
    $bounds = [System.Drawing.RectangleF]::new(0, 0, $size - 0.5, $size - 0.5)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($bounds.Left, $bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($bounds.Right - $diameter, $bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($bounds.Right - $diameter, $bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($bounds.Left, $bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($background, $path)

    foreach ($x in @(6, 25, 44)) {
        $graphics.DrawRectangle(
            $frame,
            [single]($x * $scale),
            [single](10 * $scale),
            [single](14 * $scale),
            [single](38 * $scale))
    }

    $points = @(
        [System.Drawing.PointF]::new([single](11 * $scale), [single](29 * $scale)),
        [System.Drawing.PointF]::new([single](24 * $scale), [single](29 * $scale)),
        [System.Drawing.PointF]::new([single](31 * $scale), [single](21 * $scale)),
        [System.Drawing.PointF]::new([single](39 * $scale), [single](37 * $scale)),
        [System.Drawing.PointF]::new([single](47 * $scale), [single](29 * $scale)),
        [System.Drawing.PointF]::new([single](56 * $scale), [single](29 * $scale))
    )
    $arrow = @(
        [System.Drawing.PointF]::new([single](50 * $scale), [single](23 * $scale)),
        [System.Drawing.PointF]::new([single](56 * $scale), [single](29 * $scale)),
        [System.Drawing.PointF]::new([single](50 * $scale), [single](35 * $scale))
    )
    $graphics.DrawLines($signal, $points)
    $graphics.DrawLines($signal, $arrow)

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())

    $stream.Dispose()
    $path.Dispose()
    $signal.Dispose()
    $frame.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)

$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
for ($index = 0; $index -lt $images.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$index].Length
}

foreach ($image in $images) {
    $writer.Write($image)
}

$writer.Dispose()
$file.Dispose()
Write-Output $resolvedOutput
