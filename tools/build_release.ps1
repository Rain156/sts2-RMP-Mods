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

Compress-Archive -Path $zipModFolder -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $zipStageRoot -Recurse -Force
