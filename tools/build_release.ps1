$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$godot = Join-Path $root "libs\Godot_v4.5.1-stable_win64_console.exe"
$releaseDir = Join-Path $root "build\RemoveMultiplayerPlayerLimit"
$dllSource = Join-Path $root ".godot\mono\temp\bin\Debug\RemoveMultiplayerPlayerLimit.dll"
$pckSource = Join-Path $root "build\RemoveMultiplayerPlayerLimit.pck"
$manifestPath = Join-Path $root "mod_manifest.json"
$buildRoot = Join-Path $root "build"

& $dotnet build (Join-Path $root "RemoveMultiplayerPlayerLimit.csproj") -c Debug
& $godot --headless --path $root --script "res://tools/build_pck.gd"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Get-ChildItem -Path $releaseDir -Force | Remove-Item -Recurse -Force

Copy-Item $manifestPath -Destination (Join-Path $releaseDir "mod_manifest.json") -Force
Copy-Item $dllSource -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.dll") -Force
Copy-Item $pckSource -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.pck") -Force

if (Test-Path (Join-Path $root "mod_image.png")) {
	Copy-Item (Join-Path $root "mod_image.png") -Destination (Join-Path $releaseDir "mod_image.png") -Force
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version
$modFolderName = if ([string]::IsNullOrWhiteSpace([string]$manifest.pck_name)) { [string]$manifest.name } else { [string]$manifest.pck_name }
if ([string]::IsNullOrWhiteSpace($version)) { throw "mod_manifest.json missing version field" }
if ([string]::IsNullOrWhiteSpace($modFolderName)) { throw "mod_manifest.json missing name/pck_name field" }

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
