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

# Generate save-copy launcher
@'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0copy_save_helper.ps1"
pause
'@ | Set-Content (Join-Path $zipStageRoot "CopySave.bat") -Encoding ASCII

# Generate save-copy helper script
@'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$host.UI.RawUI.WindowTitle = 'STS2 Save Copy Tool'

# ── Helpers ─────────────────────────────────────────────────────────
function Write-Bi($en, $zh, $color) {
    if ($color) { Write-Host "  $en | $zh" -ForegroundColor $color }
    else        { Write-Host "  $en | $zh" }
}

function Read-Choice($prompt) {
    Write-Host ''
    Write-Host "  $prompt" -ForegroundColor White -NoNewline
    $val = Read-Host
    if ($null -eq $val) { return '' }
    return $val.Trim()
}

# ── Constants ───────────────────────────────────────────────────────
$SAVE_ROOT = Join-Path $env:APPDATA 'SlayTheSpire2\steam'

Write-Host '============================================'
Write-Host '  Copy Vanilla Save to Modded'
Write-Host '  复制原版存档到模组存档'
Write-Host '============================================'
Write-Host ''

# ── Locate save root ────────────────────────────────────────────────
if (-not (Test-Path $SAVE_ROOT)) {
    Write-Bi 'Save directory not found.' '未找到存档目录。' Red
    Write-Bi 'Please launch the game at least once first.' '请先至少启动一次游戏。' $null
    Write-Host "  $SAVE_ROOT"
    exit 1
}

# ── Detect Steam ID ─────────────────────────────────────────────────
$ids = @(Get-ChildItem $SAVE_ROOT -Directory | Where-Object { $_.Name -match '^\d+$' } | Select-Object -ExpandProperty Name)
if ($ids.Count -eq 0) {
    Write-Bi 'No Steam user saves found.' '未找到任何 Steam 用户存档。' Red
    exit 1
}

$steamId = $ids[0]
if ($ids.Count -gt 1) {
    Write-Bi 'Multiple Steam accounts detected:' '检测到多个 Steam 账号：' $null
    Write-Host ''
    for ($i = 0; $i -lt $ids.Count; $i++) {
        Write-Host "    $($i+1). $($ids[$i])"
    }
    $pick = Read-Choice "Select account | 请选择账号 [1-$($ids.Count)]: "
    $idx = [int]$pick - 1
    if ($idx -ge 0 -and $idx -lt $ids.Count) { $steamId = $ids[$idx] }
    else { Write-Bi 'Invalid selection.' '无效选择。' Red; exit 1 }
}

Write-Host "  Steam ID: $steamId" -ForegroundColor Cyan
Write-Host ''

# ── Profile info helper ─────────────────────────────────────────────
function Get-ProfileInfo($sid, $type, $slot) {
    $base = if ($type -eq 'normal') {
        Join-Path $SAVE_ROOT "$sid\profile$slot\saves"
    } else {
        Join-Path $SAVE_ROOT "$sid\modded\profile$slot\saves"
    }
    $progressFile = Join-Path $base 'progress.save'
    $hasData = Test-Path $progressFile
    $lastModified = $null
    if ($hasData) { $lastModified = (Get-Item $progressFile).LastWriteTime }
    return @{ Path = $base; HasData = $hasData; LastModified = $lastModified; Slot = $slot }
}

# ── Backup helper ────────────────────────────────────────────────────
function Do-Backup($sid, $type, $tag, $slot) {
    $src = if ($type -eq 'normal') {
        Join-Path $SAVE_ROOT "$sid\profile$slot\saves"
    } else {
        Join-Path $SAVE_ROOT "$sid\modded\profile$slot\saves"
    }
    if (-not (Test-Path $src)) { return }

    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backupDir = Join-Path $SAVE_ROOT "$sid\backups\${type}_p${slot}_${tag}_$timestamp"
    New-Item $backupDir -ItemType Directory -Force | Out-Null

    $files = Get-ChildItem $src -File -ErrorAction SilentlyContinue
    foreach ($f in $files) { Copy-Item $f.FullName (Join-Path $backupDir $f.Name) -Force }

    # Also backup history/ subdirectory
    $histDir = Join-Path $src 'history'
    if (Test-Path $histDir) {
        $bkHist = Join-Path $backupDir 'history'
        Copy-Item $histDir $bkHist -Recurse -Force
    }

    $label = if ($type -eq 'normal') { 'vanilla | 原版' } else { 'modded | 模组' }
    if ($files.Count -gt 0) {
        Write-Host "  [OK] Backed up $label slot $slot ($($files.Count) files)" -ForegroundColor Green
        Write-Host "  [OK] 已备份${label}槽位$slot ($($files.Count) 个文件)" -ForegroundColor Green
        Write-Host "       $backupDir" -ForegroundColor DarkGray
    }

    # Prune: keep max 20 backups per type
    $backupRoot = Join-Path $SAVE_ROOT "$sid\backups"
    if (Test-Path $backupRoot) {
        $typeBackups = Get-ChildItem $backupRoot -Directory |
            Where-Object { $_.Name -like "${type}_*" } |
            Sort-Object Name -Descending
        if ($typeBackups.Count -gt 20) {
            $typeBackups | Select-Object -Skip 20 | ForEach-Object {
                Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# ── Scan profiles ────────────────────────────────────────────────────
$normalInfos = @()
$moddedInfos = @()
for ($i = 1; $i -le 3; $i++) {
    $normalInfos += Get-ProfileInfo $steamId 'normal' $i
    $moddedInfos += Get-ProfileInfo $steamId 'modded' $i
}

# Check if any vanilla profile has data
$hasAnyNormal = $false
for ($i = 0; $i -lt 3; $i++) {
    if ($normalInfos[$i].HasData) { $hasAnyNormal = $true; break }
}
if (-not $hasAnyNormal) {
    Write-Bi 'No vanilla saves found. Please play the game without mods first.' '没有可复制的原版存档。请先运行一次原版游戏。' Red
    exit 1
}

# Auto-select: source = first non-empty vanilla, dest = first empty modded (or slot 1)
$srcSlot = 0
for ($i = 0; $i -lt 3; $i++) {
    if ($normalInfos[$i].HasData) { $srcSlot = $i + 1; break }
}
$dstSlot = 0
for ($i = 0; $i -lt 3; $i++) {
    if (-not $moddedInfos[$i].HasData) { $dstSlot = $i + 1; break }
}
if ($dstSlot -eq 0) { $dstSlot = 1 }

# ── Interactive selection loop ───────────────────────────────────────
while ($true) {
    Write-Host '  [Vanilla Saves - Source | 原版存档 - 来源]' -ForegroundColor Green
    for ($i = 0; $i -lt 3; $i++) {
        $p = $normalInfos[$i]
        $arrow = if ($i + 1 -eq $srcSlot) { ' << Source | 来源' } else { '' }
        $slotLabel = "Slot $($i+1) | 槽位$($i+1)"
        if ($p.HasData) {
            $ts = $p.LastModified.ToString('yyyy-MM-dd HH:mm')
            Write-Host "    ${slotLabel}: $ts" -ForegroundColor Green -NoNewline
            if ($arrow) { Write-Host $arrow -ForegroundColor Cyan } else { Write-Host '' }
        } else {
            Write-Host "    ${slotLabel}: (empty) | (空)" -ForegroundColor DarkGray
        }
    }

    Write-Host ''

    Write-Host '  [Modded Saves - Destination | 模组存档 - 目标]' -ForegroundColor Yellow
    for ($i = 0; $i -lt 3; $i++) {
        $p = $moddedInfos[$i]
        $arrow = if ($i + 1 -eq $dstSlot) { ' << Destination | 目标' } else { '' }
        $slotLabel = "Slot $($i+1) | 槽位$($i+1)"
        if ($p.HasData) {
            $ts = $p.LastModified.ToString('yyyy-MM-dd HH:mm')
            Write-Host "    ${slotLabel}: $ts" -ForegroundColor Yellow -NoNewline
            if ($arrow) { Write-Host $arrow -ForegroundColor Cyan } else { Write-Host '' }
        } else {
            Write-Host "    ${slotLabel}: (empty) | (空)" -ForegroundColor DarkGray -NoNewline
            if ($arrow) { Write-Host $arrow -ForegroundColor Cyan } else { Write-Host '' }
        }
    }

    Write-Host ''
    Write-Host '  ─────────────────────────────────────' -ForegroundColor DarkGray
    $dstNote = ''
    if ($moddedInfos[$dstSlot-1].HasData) { $dstNote = ' (will overwrite, auto-backup | 将覆盖，自动备份)' }
    Write-Host "  Plan | 计划: Vanilla Slot $srcSlot -> Modded Slot $dstSlot$dstNote" -ForegroundColor Cyan
    Write-Host ''
    Write-Host "    S. Change source slot | 更改来源槽位 (current | 当前: $srcSlot)"
    Write-Host "    D. Change destination slot | 更改目标槽位 (current | 当前: $dstSlot)"
    Write-Host '    Y. Confirm | 确认执行' -ForegroundColor Cyan
    Write-Host '    0. Cancel | 取消' -ForegroundColor DarkGray

    $choice = Read-Choice 'Select | 请选择 [S/D/Y/0]: '

    if ($choice -eq '0') { Write-Bi 'Cancelled.' '已取消。' $null; exit 0 }
    if ($choice -ieq 'Y') { break }

    if ($choice -ieq 'S') {
        $pick = Read-Choice 'Select source slot | 选择来源槽位 [1-3]: '
        $idx = [int]$pick
        if ($idx -ge 1 -and $idx -le 3) {
            if ($normalInfos[$idx-1].HasData) {
                $srcSlot = $idx
                Write-Bi "Source changed to: Vanilla Slot $srcSlot" "来源已更改为: 原版槽位$srcSlot" Green
            } else {
                Write-Bi "Vanilla Slot $idx is empty, cannot use as source." "原版槽位$idx 是空的，无法作为来源。" Yellow
            }
        } else {
            Write-Bi 'Please enter 1-3.' '请输入 1-3。' Yellow
        }
        continue
    }

    if ($choice -ieq 'D') {
        $pick = Read-Choice 'Select destination slot | 选择目标槽位 [1-3]: '
        $idx = [int]$pick
        if ($idx -ge 1 -and $idx -le 3) {
            $dstSlot = $idx
            if ($moddedInfos[$idx-1].HasData) {
                Write-Bi "Modded Slot $idx has data - will auto-backup before overwriting." "模组槽位$idx 已有存档，执行时将自动备份后覆盖。" Yellow
            }
            Write-Bi "Destination changed to: Modded Slot $dstSlot" "目标已更改为: 模组槽位$dstSlot" Green
        } else {
            Write-Bi 'Please enter 1-3.' '请输入 1-3。' Yellow
        }
        continue
    }
}

# ── Execute copy ─────────────────────────────────────────────────────
$src = $normalInfos[$srcSlot-1].Path
$dst = $moddedInfos[$dstSlot-1].Path

Write-Host ''

# Backup if target has data
if ($moddedInfos[$dstSlot-1].HasData) {
    Write-Bi "Backing up Modded Slot $dstSlot before overwriting..." "正在备份模组槽位$dstSlot 的现有存档..." $null
    Do-Backup $steamId 'modded' 'auto_before_copy' $dstSlot
    Write-Host ''
}

# Create target directory
if (-not (Test-Path $dst)) {
    New-Item $dst -ItemType Directory -Force | Out-Null
}

# Copy files
$copied = 0
$srcFiles = Get-ChildItem $src -File -ErrorAction SilentlyContinue
foreach ($f in $srcFiles) {
    Copy-Item $f.FullName (Join-Path $dst $f.Name) -Force
    Write-Host "    [OK] $($f.Name)" -ForegroundColor Green
    $copied++
}

# Copy subdirectories (e.g. history/)
$srcDirs = Get-ChildItem $src -Directory -ErrorAction SilentlyContinue
foreach ($d in $srcDirs) {
    $dstSubDir = Join-Path $dst $d.Name
    Copy-Item $d.FullName $dstSubDir -Recurse -Force
    $subCount = (Get-ChildItem $d.FullName -File -Recurse).Count
    Write-Host "    [OK] $($d.Name)/ ($subCount files | 个文件)" -ForegroundColor Green
    $copied += $subCount
}

Write-Host ''
if ($copied -gt 0) {
    Write-Host '============================================'
    Write-Bi "Copy complete! Vanilla Slot $srcSlot -> Modded Slot $dstSlot ($copied files)" "存档复制完成！原版槽位$srcSlot -> 模组槽位$dstSlot（共 $copied 个文件）" Green
    Write-Host '============================================'
    Write-Host ''
    Write-Bi 'Next time you launch the game with mods, select the corresponding save slot.' '下次启用模组进入游戏时，请选择对应槽位使用复制的进度。' $null
} else {
    Write-Bi 'No files were copied.' '没有文件被复制。' Yellow
}

Write-Host ''
'@ | Set-Content (Join-Path $zipStageRoot "copy_save_helper.ps1") -Encoding UTF8

Compress-Archive -Path (Join-Path $zipStageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $zipStageRoot -Recurse -Force
