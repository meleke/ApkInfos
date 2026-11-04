# ----------------------------------------------------------------------------
# ApkInfos - APK / AAB 信息查看工具
# 文件：publish.ps1
# 说明：一键发布：Windows exe/zip + macOS .app zip（自包含、单文件、部分裁剪、压缩）
# 用法：powershell -ExecutionPolicy Bypass -File publish.ps1 [-Runtimes win-x64,osx-arm64,osx-x64]
# 作者：Wade.liuyu (meleke_ly@126.com)
# 创建：2026-11-04
# 许可：Apache License 2.0
# ----------------------------------------------------------------------------
param(
    [string[]]$Runtimes = @('win-x64', 'osx-arm64', 'osx-x64'),
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

# 通过 -File 调用时数组参数会以 "a,b,c" 单个字符串传入，这里统一拆分
$Runtimes = @($Runtimes | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$project = Join-Path $root 'src\ApkInfos.App\ApkInfos.App.csproj'
$dist = Join-Path $root 'dist'

if (Test-Path $dist)
{
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Force $dist | Out-Null

function Write-MacZip([string]$appDir, [string]$zipPath)
{
    # 手动打 zip，为 macOS 可执行文件写入 unix 权限（0755），否则解压后无法运行
    $baseDir = Split-Path $appDir -Parent
    $fs = [IO.File]::Open($zipPath, 'Create')
    $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try
    {
        foreach ($item in Get-ChildItem $appDir -Recurse)
        {
            $rel = $item.FullName.Substring($baseDir.Length + 1).Replace('\', '/')
            if ($item.PSIsContainer)
            {
                $entry = $zip.CreateEntry($rel + '/')
                $entry.ExternalAttributes = [int](0x41ED -shl 16)
                continue
            }

            $entry = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
            $isExec = $rel -match '/Contents/MacOS/[^/.]+$'
            $mode = if ($isExec) { 0x81ED } else { 0x81A4 }
            $entry.ExternalAttributes = [int]($mode -shl 16)
            $src = [IO.File]::OpenRead($item.FullName)
            $dst = $entry.Open()
            $src.CopyTo($dst)
            $dst.Dispose()
            $src.Dispose()
        }
    }
    finally
    {
        $zip.Dispose()
        $fs.Dispose()
    }

    Set-ZipUnixHost $zipPath
}

function Write-WinZip([string]$exePath, [string]$zipPath)
{
    # zip 内只有一个 exe，文件名与 exe 一致（如 ApkInfos-win-x64.exe）
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try
    {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $exePath, (Split-Path $exePath -Leaf), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    finally
    {
        $zip.Dispose()
    }
}

function Set-ZipUnixHost([string]$zipPath)
{
    # Windows 上生成的 zip「version made by」平台为 FAT，macOS 解压时会忽略 unix 权限位；
    # 这里把中央目录每个条目的平台字节改为 3（Unix），使 0755 生效
    $bytes = [IO.File]::ReadAllBytes($zipPath)
    $eocd = -1
    for ($i = $bytes.Length - 22; $i -ge 0; $i--)
    {
        if ([BitConverter]::ToUInt32($bytes, $i) -eq 0x06054b50)
        {
            $eocd = $i
            break
        }
    }
    if ($eocd -lt 0)
    {
        throw "EOCD not found: $zipPath"
    }

    $count = [BitConverter]::ToUInt16($bytes, $eocd + 10)
    $pos = [int][BitConverter]::ToUInt32($bytes, $eocd + 16)
    for ($n = 0; $n -lt $count; $n++)
    {
        if ([BitConverter]::ToUInt32($bytes, $pos) -ne 0x02014b50)
        {
            throw "Bad central directory entry at $pos"
        }

        $bytes[$pos + 5] = 3
        $nameLen = [BitConverter]::ToUInt16($bytes, $pos + 28)
        $extraLen = [BitConverter]::ToUInt16($bytes, $pos + 30)
        $commentLen = [BitConverter]::ToUInt16($bytes, $pos + 32)
        $pos += 46 + $nameLen + $extraLen + $commentLen
    }
    [IO.File]::WriteAllBytes($zipPath, $bytes)
}

foreach ($rid in $Runtimes)
{
    Write-Host "==== Publishing $rid ====" -ForegroundColor Cyan
    $pub = Join-Path $dist "publish\$rid"
    dotnet publish $project -c $Configuration -r $rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=true `
        -p:TrimMode=partial `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:Version=$Version `
        -o $pub
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed: $rid"
    }

    if ($rid -like 'win-*')
    {
        # exe 直接上传 GitHub 易被网络或安全软件拦截，发布时上传 zip；exe 保留在 dist/ 便于本地使用
        $exe = Join-Path $dist "ApkInfos-$rid.exe"
        Copy-Item (Join-Path $pub 'ApkInfos.exe') $exe
        Write-WinZip $exe (Join-Path $dist "ApkInfos-$rid.zip")
        continue
    }

    # 组装 macOS .app 目录结构
    $app = Join-Path $dist "$rid\ApkInfos.app"
    $macos = Join-Path $app 'Contents\MacOS'
    $res = Join-Path $app 'Contents\Resources'
    New-Item -ItemType Directory -Force $macos, $res | Out-Null
    Copy-Item (Join-Path $pub '*') $macos -Recurse
    Copy-Item (Join-Path $root 'src\ApkInfos.App\Assets\app.icns') (Join-Path $res 'app.icns')

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>ApkInfos</string>
    <key>CFBundleDisplayName</key>
    <string>ApkInfos</string>
    <key>CFBundleIdentifier</key>
    <string>com.apkinfos.app</string>
    <key>CFBundleExecutable</key>
    <string>ApkInfos</string>
    <key>CFBundleIconFile</key>
    <string>app.icns</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$Version</string>
    <key>CFBundleVersion</key>
    <string>$Version</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@
    [IO.File]::WriteAllText((Join-Path $app 'Contents\Info.plist'), $plist, (New-Object Text.UTF8Encoding($false)))
    Write-MacZip $app (Join-Path $dist "ApkInfos-$rid.zip")
}

Remove-Item (Join-Path $dist 'publish') -Recurse -Force

Write-Host ''
Write-Host '==== Output sizes ====' -ForegroundColor Cyan
foreach ($f in Get-ChildItem $dist -File)
{
    '{0,-28} {1,8:N1} MB' -f $f.Name, ($f.Length / 1MB)
}
foreach ($d in Get-ChildItem $dist -Directory)
{
    $size = (Get-ChildItem $d.FullName -Recurse -File | Measure-Object Length -Sum).Sum
    '{0,-28} {1,8:N1} MB' -f "$($d.Name)/ApkInfos.app", ($size / 1MB)
}
