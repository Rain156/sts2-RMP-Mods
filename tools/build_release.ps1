$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$godot = "F:\Dev\Remove Multiplayer PlayerLimit\Godot 4.5.1\Godot_v4.5.1-stable_win64_console.exe"
$buildRoot = Join-Path $root "build"
$releaseDir = Join-Path $root "build\RemoveMultiplayerPlayerLimit"
$dllSource = Join-Path $root ".godot\mono\temp\bin\Debug\RemoveMultiplayerPlayerLimit.dll"
$pckSource = Join-Path $root "build\RemoveMultiplayerPlayerLimit.pck"
$manifestPathBeta = Join-Path $root "RemoveMultiplayerPlayerLimit.json"

& $dotnet build (Join-Path $root "RemoveMultiplayerPlayerLimit.csproj") -c Debug
& $godot --headless --path $root --script "res://tools/build_pck.gd"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Get-ChildItem -Path $releaseDir -Force | Remove-Item -Recurse -Force
Get-ChildItem -Path $buildRoot -Filter "sts2-RMP-*.zip" -File -ErrorAction SilentlyContinue | Remove-Item -Force

Copy-Item $dllSource -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.dll") -Force
Copy-Item $pckSource -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.pck") -Force
Copy-Item $manifestPathBeta -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.json") -Force

$manifest = Get-Content $manifestPathBeta -Raw | ConvertFrom-Json
$version = [string]$manifest.version
$modFolderName = if ([string]::IsNullOrWhiteSpace([string]$manifest.pck_name)) { [string]$manifest.name } else { [string]$manifest.pck_name }
if ([string]::IsNullOrWhiteSpace($version)) { throw "RemoveMultiplayerPlayerLimit.json missing version field" }
if ([string]::IsNullOrWhiteSpace($modFolderName)) { throw "RemoveMultiplayerPlayerLimit.json missing name/pck_name field" }

$zipName = "sts2-RMP-$version.zip"
$zipPath = Join-Path $buildRoot $zipName
$zipStageRoot = Join-Path $buildRoot "_zip_stage"
$zipModFolder = Join-Path $zipStageRoot $modFolderName

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
if (Test-Path $zipStageRoot) { Remove-Item $zipStageRoot -Recurse -Force }

New-Item -ItemType Directory -Force -Path $zipModFolder | Out-Null
Copy-Item (Join-Path $releaseDir "*") -Destination $zipModFolder -Recurse -Force

# Generate one-click installer scripts into zip stage root
@'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0helper.ps1"
pause
'@ | Set-Content (Join-Path $zipStageRoot "Install.bat") -Encoding ASCII

@'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$host.UI.RawUI.WindowTitle = 'Remove Multiplayer Player Limit - Installer'

Write-Host '============================================'
Write-Host '  Remove Multiplayer Player Limit'
Write-Host '  One-Click Installer | 一键安装程序'
Write-Host '============================================'
Write-Host ''

# ── Validate mod files exist next to this script ──────────────────────
$src = $PSScriptRoot
$modFolder = Join-Path $src 'RemoveMultiplayerPlayerLimit'
$dll  = Join-Path $modFolder 'RemoveMultiplayerPlayerLimit.dll'
$pck  = Join-Path $modFolder 'RemoveMultiplayerPlayerLimit.pck'
$json = Join-Path $modFolder 'RemoveMultiplayerPlayerLimit.json'

$missing = @()
if (-not (Test-Path $dll))  { $missing += 'RemoveMultiplayerPlayerLimit.dll' }
if (-not (Test-Path $pck))  { $missing += 'RemoveMultiplayerPlayerLimit.pck' }
if (-not (Test-Path $json)) { $missing += 'RemoveMultiplayerPlayerLimit.json' }

if ($missing.Count -gt 0) {
    Write-Host '[ERROR] Missing mod files:' -ForegroundColor Red
    Write-Host '[错误] 缺少以下模组文件：' -ForegroundColor Red
    foreach ($f in $missing) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Please make sure this script is in the same folder as the'
    Write-Host '"RemoveMultiplayerPlayerLimit" directory from the release zip.'
    Write-Host '请确保本脚本与 RemoveMultiplayerPlayerLimit 文件夹在同一目录下。'
    exit 1
}

Write-Host 'Searching for Slay the Spire 2 installation...'
Write-Host '正在搜索「杀戮尖塔 2」安装目录，请稍候...'
Write-Host ''

# ── Detect Steam install path from Windows Registry ──────────────────
$sp = $null
try { $sp = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -EA Stop).InstallPath } catch {}
if (-not $sp) {
    try { $sp = (Get-ItemProperty 'HKCU:\SOFTWARE\Valve\Steam' -EA Stop).SteamPath } catch {}
}

# ── Parse libraryfolders.vdf to find all Steam library paths ─────────
$gp = $null
if ($sp) {
    $vdf = Join-Path $sp 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($line in Get-Content $vdf) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $p = $Matches[1].Replace('\\', '\')
                $c = Join-Path $p 'steamapps\common\Slay the Spire 2'
                if (Test-Path $c) { $gp = $c; break }
            }
        }
    }
    # Fallback: check the main Steam directory itself
    if (-not $gp) {
        $c = Join-Path $sp 'steamapps\common\Slay the Spire 2'
        if (Test-Path $c) { $gp = $c }
    }
}

# ── Install ──────────────────────────────────────────────────────────
if ($gp) {
    Write-Host "Found game directory | 找到游戏目录：" -ForegroundColor Green
    Write-Host "  $gp" -ForegroundColor Green
    Write-Host ''

    $dest = Join-Path $gp 'mods\RemoveMultiplayerPlayerLimit'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item (Join-Path $modFolder '*') -Destination $dest -Recurse -Force

    Write-Host '============================================'
    Write-Host '  Installation successful!' -ForegroundColor Green
    Write-Host '  安装成功！' -ForegroundColor Green
    Write-Host '============================================'
    Write-Host ''
    Write-Host 'The mod will be enabled automatically when you launch the game.'
    Write-Host '启动游戏后模组将自动生效。'
    Write-Host ''
    Write-Host 'Installed to | 安装路径：'
    Write-Host "  $dest"
} else {
    Write-Host '============================================'
    Write-Host '  Auto-detection failed' -ForegroundColor Red
    Write-Host '  自动安装失败' -ForegroundColor Red
    Write-Host '============================================'
    Write-Host ''
    Write-Host 'Could not locate "Slay the Spire 2" automatically.'
    Write-Host '未能自动找到「杀戮尖塔 2」安装目录。'
    Write-Host ''
    Write-Host 'Please copy the "RemoveMultiplayerPlayerLimit" folder manually to:'
    Write-Host '请手动将 RemoveMultiplayerPlayerLimit 文件夹复制到：'
    Write-Host ''
    Write-Host '  <Slay the Spire 2>\mods\RemoveMultiplayerPlayerLimit\'
    Write-Host ''
    Write-Host 'Example | 示例路径：'
    Write-Host '  D:\Steam\steamapps\common\Slay the Spire 2\mods\RemoveMultiplayerPlayerLimit\'
    Write-Host ''
    Write-Host 'Tip: In Steam, right-click the game > Manage > Browse Local Files'
    Write-Host '提示：在 Steam 中右键游戏 > 管理 > 浏览本地文件'
}

Write-Host ''
'@ | Set-Content (Join-Path $zipStageRoot "helper.ps1") -Encoding UTF8

Compress-Archive -Path (Join-Path $zipStageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $zipStageRoot -Recurse -Force
