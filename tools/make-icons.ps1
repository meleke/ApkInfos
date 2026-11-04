# ----------------------------------------------------------------------------
# ApkInfos - APK / AAB 信息查看工具
# 文件：make-icons.ps1
# 说明：生成应用图标：app.png / app.ico（Windows）/ app.icns（macOS）
# 用法：powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1
# 作者：Wade.liuyu (meleke_ly@126.com)
# 创建：2026-11-04
# 许可：Apache License 2.0
# ----------------------------------------------------------------------------
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\ApkInfos.App\Assets')
)

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size)
{
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角矩形背景（绿色渐变）
    $r = [single]($size * 0.22)
    $pad = [single]($size * 0.04)
    $w = [single]($size - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, $r, $r, 180, 90)
    $path.AddArc($pad + $w - $r, $pad, $r, $r, 270, 90)
    $path.AddArc($pad + $w - $r, $pad + $w - $r, $r, $r, 0, 90)
    $path.AddArc($pad, $pad + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, $w, $w
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 61, 220, 132)), ([System.Drawing.Color]::FromArgb(255, 7, 120, 90)), 45
    $g.FillPath($brush, $path)

    # 文字 "APK" 与下方 "info"
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $font1 = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * 0.28)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $font2 = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * 0.14)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $white = [System.Drawing.Brushes]::White
    $g.DrawString('APK', $font1, $white, (New-Object System.Drawing.RectangleF 0, ([single]($size * 0.16)), $size, ([single]($size * 0.45))), $sf)
    $g.DrawString('info', $font2, $white, (New-Object System.Drawing.RectangleF 0, ([single]($size * 0.56)), $size, ([single]($size * 0.25))), $sf)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    return ,$ms.ToArray()
}

function Write-U16LE($bw, [int]$v) { $bw.Write([uint16]$v) }
function Write-U32BE($bw, [uint32]$v)
{
    $bytes = [BitConverter]::GetBytes($v)
    [Array]::Reverse($bytes)
    $bw.Write($bytes)
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

# app.png
$png512 = New-IconPng 512
[IO.File]::WriteAllBytes((Join-Path $OutDir 'app.png'), $png512)

# app.ico：内嵌 PNG
$icoSizes = @(16, 32, 48, 256)
$icoPngs = @()
foreach ($s in $icoSizes)
{
    $icoPngs += ,(New-IconPng $s)
}
$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $fs
Write-U16LE $bw 0
Write-U16LE $bw 1
Write-U16LE $bw $icoSizes.Count
$offset = 6 + 16 * $icoSizes.Count
for ($i = 0; $i -lt $icoSizes.Count; $i++)
{
    $s = $icoSizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    Write-U16LE $bw 1
    Write-U16LE $bw 32
    $bw.Write([uint32]$icoPngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $icoPngs[$i].Length
}
foreach ($p in $icoPngs)
{
    $bw.Write($p)
}
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $fs.ToArray())

# app.icns：ic07(128) / ic08(256) / ic09(512) 均为 PNG
$icns = @(@('ic07', (New-IconPng 128)), @('ic08', (New-IconPng 256)), @('ic09', $png512))
$body = New-Object System.IO.MemoryStream
$bwb = New-Object System.IO.BinaryWriter $body
foreach ($e in $icns)
{
    $bwb.Write([Text.Encoding]::ASCII.GetBytes($e[0]))
    Write-U32BE $bwb ([uint32]($e[1].Length + 8))
    $bwb.Write($e[1])
}
$bwb.Flush()
$all = New-Object System.IO.MemoryStream
$bwa = New-Object System.IO.BinaryWriter $all
$bwa.Write([Text.Encoding]::ASCII.GetBytes('icns'))
Write-U32BE $bwa ([uint32]($body.Length + 8))
$bwa.Write($body.ToArray())
$bwa.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'app.icns'), $all.ToArray())

Write-Output "Icons written to $OutDir"
