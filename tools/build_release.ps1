param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Sts2AssemblyPath = "",
    [string]$SteamworksAssemblyPath = ""
)

function Fail($Message) {
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
    exit 1
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$StepName
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        Fail "$StepName failed with exit code $LASTEXITCODE"
    }
}

function Wait-ForPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,
        [int]$RetryCount = 50,
        [int]$DelayMilliseconds = 200
    )

    for ($i = 0; $i -lt $RetryCount; $i++) {
        if (Test-Path -LiteralPath $LiteralPath) {
            return $true
        }

        Start-Sleep -Milliseconds $DelayMilliseconds
    }

    return $false
}

function Remove-PathWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,
        [int]$RetryCount = 30,
        [int]$DelayMilliseconds = 300
    )

    if (-not (Test-Path -LiteralPath $LiteralPath)) {
        return
    }

    for ($i = 0; $i -lt $RetryCount; $i++) {
        try {
            Remove-Item -LiteralPath $LiteralPath -Recurse -Force -ErrorAction Stop
            return
        } catch {
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }

    Fail "Failed to remove $LiteralPath after multiple retries."
}

function Resolve-GodotPath {
    param([string]$Root)

    if ($env:GODOT_PATH -and (Test-Path $env:GODOT_PATH)) {
        return $env:GODOT_PATH
    }

    $candidates = @(
        (Join-Path $Root "libs\Godot_v4.5.1-stable_win64_console.exe"),
        (Join-Path $Root "libs\Godot_v4.5-stable_win64_console.exe"),
        (Join-Path $Root "libs\Godot_v4.5.1-stable_win64.exe"),
        (Join-Path $Root "libs\Godot_v4.5-stable_win64.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    foreach ($commandName in @("godot4", "godot")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Source
        }
    }

    Fail "Godot 4.5.x was not found. Put it under libs/ or set GODOT_PATH."
}

function Resolve-SteamLibraryRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    $steamPath = $null
    try { $steamPath = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction Stop).InstallPath } catch {}
    if (-not $steamPath) {
        try { $steamPath = (Get-ItemProperty 'HKCU:\SOFTWARE\Valve\Steam' -ErrorAction Stop).SteamPath } catch {}
    }

    if ($steamPath -and (Test-Path -LiteralPath $steamPath)) {
        $roots.Add($steamPath)

        $libraryVdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryVdf) {
            foreach ($line in Get-Content -LiteralPath $libraryVdf) {
                if ($line -match '"path"\s+"([^"]+)"') {
                    $libraryPath = $Matches[1].Replace('\\', '\')
                    if (Test-Path -LiteralPath $libraryPath) {
                        $roots.Add($libraryPath)
                    }
                }
            }
        }
    }

    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        foreach ($candidate in @(
            (Join-Path $drive.Root 'SteamLibrary'),
            (Join-Path $drive.Root 'Steam')
        )) {
            if (Test-Path -LiteralPath $candidate) {
                $roots.Add($candidate)
            }
        }
    }

    return $roots | Select-Object -Unique
}

function Get-Sts2DllCandidatesForGamePath {
    param([string]$GamePath)

    return @(
        (Join-Path $GamePath 'data_sts2_windows_x86_64\sts2.dll'),
        (Join-Path $GamePath 'data_sts2_linux_x86_64\sts2.dll'),
        (Join-Path $GamePath 'data_sts2_macos_x86_64\sts2.dll'),
        (Join-Path $GamePath 'SlayTheSpire2.app\Contents\MacOS\data_sts2_macos_x86_64\sts2.dll'),
        (Join-Path $GamePath 'sts2.dll')
    )
}

function Get-SteamworksDllCandidatesForGamePath {
    param([string]$GamePath)

    return @(
        (Join-Path $GamePath 'data_sts2_windows_x86_64\Steamworks.NET.dll'),
        (Join-Path $GamePath 'data_sts2_linux_x86_64\Steamworks.NET.dll'),
        (Join-Path $GamePath 'data_sts2_macos_x86_64\Steamworks.NET.dll'),
        (Join-Path $GamePath 'SlayTheSpire2.app\Contents\MacOS\data_sts2_macos_x86_64\Steamworks.NET.dll'),
        (Join-Path $GamePath 'Steamworks.NET.dll')
    )
}

function Resolve-Sts2AssemblyPath {
    param(
        [string]$Root,
        [string]$ExplicitPath
    )

    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidates.Add($ExplicitPath)
    }

    if ($env:Sts2AssemblyPath) {
        $candidates.Add($env:Sts2AssemblyPath)
    }
    if ($env:STS2_ASSEMBLY_PATH) {
        $candidates.Add($env:STS2_ASSEMBLY_PATH)
    }

    foreach ($gamePath in @($env:STS2GamePath, $env:STS2_GAME_PATH)) {
        if (-not [string]::IsNullOrWhiteSpace($gamePath)) {
            foreach ($candidate in Get-Sts2DllCandidatesForGamePath -GamePath $gamePath) {
                $candidates.Add($candidate)
            }
        }
    }

    foreach ($libraryRoot in Resolve-SteamLibraryRoots) {
        $gamePath = Join-Path $libraryRoot 'steamapps\common\Slay the Spire 2'
        foreach ($candidate in Get-Sts2DllCandidatesForGamePath -GamePath $gamePath) {
            $candidates.Add($candidate)
        }
    }

    $candidates.Add((Join-Path $Root 'libs\sts2.dll'))

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    Fail "sts2.dll was not found. Set STS2GamePath or Sts2AssemblyPath."
}

function Resolve-SteamworksAssemblyPath {
    param(
        [string]$Root,
        [string]$ExplicitPath
    )

    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidates.Add($ExplicitPath)
    }

    if ($env:SteamworksAssemblyPath) {
        $candidates.Add($env:SteamworksAssemblyPath)
    }
    if ($env:STEAMWORKS_ASSEMBLY_PATH) {
        $candidates.Add($env:STEAMWORKS_ASSEMBLY_PATH)
    }

    foreach ($gamePath in @($env:STS2GamePath, $env:STS2_GAME_PATH)) {
        if (-not [string]::IsNullOrWhiteSpace($gamePath)) {
            foreach ($candidate in Get-SteamworksDllCandidatesForGamePath -GamePath $gamePath) {
                $candidates.Add($candidate)
            }
        }
    }

    foreach ($libraryRoot in Resolve-SteamLibraryRoots) {
        $gamePath = Join-Path $libraryRoot 'steamapps\common\Slay the Spire 2'
        foreach ($candidate in Get-SteamworksDllCandidatesForGamePath -GamePath $gamePath) {
            $candidates.Add($candidate)
        }
    }

    $candidates.Add((Join-Path $Root 'libs\Steamworks.NET.dll'))

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    Fail "Steamworks.NET.dll was not found. Set STS2GamePath or SteamworksAssemblyPath."
}

function Write-MinimalProjectFile {
    param([string]$ProjectPath)

    @'
; Auto-generated by tools/build_release.ps1
config_version=5

[application]

config/name="Remove Multiplayer PlayerLimit"
config/features=PackedStringArray("4.5", "Forward Plus")
'@ | Set-Content -LiteralPath $ProjectPath -Encoding UTF8
}

function Write-ConfigTemplate {
    param([string]$DestinationPath)

    @'
[macos]
tls_workaround=true

[multiplayer]
difficulty_scaling=true
'@ | Set-Content -LiteralPath $DestinationPath -Encoding ASCII
}

function New-PackProject {
    param(
        [string]$Root,
        [string]$PackProjectPath
    )

    if (Test-Path $PackProjectPath) {
        Remove-Item -LiteralPath $PackProjectPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $PackProjectPath | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PackProjectPath "tools") | Out-Null

    Write-MinimalProjectFile (Join-Path $PackProjectPath "project.godot")
    Copy-Item -LiteralPath (Join-Path $Root "RemoveMultiplayerPlayerLimit.json") -Destination (Join-Path $PackProjectPath "RemoveMultiplayerPlayerLimit.json") -Force
    Copy-Item -LiteralPath (Join-Path $Root "RemoveMultiplayerPlayerLimit") -Destination (Join-Path $PackProjectPath "RemoveMultiplayerPlayerLimit") -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $Root "tools\build_pck.gd") -Destination (Join-Path $PackProjectPath "tools\build_pck.gd") -Force
}

function Get-ModMetadata {
    param([string]$ManifestPath)

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $version = [string]$manifest.version
    $folderName = if ([string]::IsNullOrWhiteSpace([string]$manifest.pck_name)) {
        [string]$manifest.name
    } else {
        [string]$manifest.pck_name
    }

    if ([string]::IsNullOrWhiteSpace($version)) {
        Fail "Manifest is missing the version field."
    }

    if ([string]::IsNullOrWhiteSpace($folderName)) {
        Fail "Manifest is missing the name/pck_name field."
    }

    return @{
        Version = $version
        FolderName = $folderName
    }
}

$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:DOTNET_PATH) { $env:DOTNET_PATH } else { "dotnet" }
$godot = Resolve-GodotPath -Root $root
$sts2Assembly = Resolve-Sts2AssemblyPath -Root $root -ExplicitPath $Sts2AssemblyPath
$steamworksAssembly = Resolve-SteamworksAssemblyPath -Root $root -ExplicitPath $SteamworksAssemblyPath

$buildRoot = Join-Path $root "build"
$packProject = Join-Path $buildRoot "_pack_project"
$releaseDir = Join-Path $buildRoot "RemoveMultiplayerPlayerLimit"
$manifestPath = Join-Path $root "RemoveMultiplayerPlayerLimit.json"
$csprojPath = Join-Path $root "RemoveMultiplayerPlayerLimit.csproj"
$dllSource = Join-Path $root ".godot\mono\temp\bin\$Configuration\RemoveMultiplayerPlayerLimit.dll"
$tempPckPath = Join-Path $packProject "build\RemoveMultiplayerPlayerLimit.pck"
$finalPckPath = Join-Path $buildRoot "RemoveMultiplayerPlayerLimit.pck"

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  RMP Build System  |  Current Mod Build" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Root          : $root"
Write-Host "  Configuration : $Configuration"
Write-Host "  Dotnet        : $dotnet"
Write-Host "  Godot         : $godot"
Write-Host "  sts2.dll      : $sts2Assembly"
Write-Host "  Steamworks    : $steamworksAssembly"
Write-Host ""

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

Write-Host "[1/5] Building DLL..." -ForegroundColor Yellow
Invoke-External -FilePath $dotnet -Arguments @("build", $csprojPath, "-c", $Configuration, "/p:Sts2AssemblyPath=$sts2Assembly", "/p:SteamworksAssemblyPath=$steamworksAssembly") -StepName "dotnet build"
if (-not (Test-Path $dllSource)) {
    Fail "Built DLL was not found at $dllSource"
}
Write-Host "  DLL built successfully." -ForegroundColor Green
Write-Host ""

Write-Host "[2/5] Preparing minimal pack project..." -ForegroundColor Yellow
New-PackProject -Root $root -PackProjectPath $packProject
Write-Host "  Minimal pack project prepared." -ForegroundColor Green
Write-Host ""

Write-Host "[3/5] Importing mod resources..." -ForegroundColor Yellow
Invoke-External -FilePath $godot -Arguments @("--headless", "--path", $packProject, "--import") -StepName "Godot import"
$importedDir = Join-Path $packProject ".godot\imported"
$ctexFiles = @()
for ($i = 0; $i -lt 20; $i++) {
    $ctexFiles = Get-ChildItem -LiteralPath $importedDir -Filter "mod_image.png-*.ctex" -ErrorAction SilentlyContinue
    if ($ctexFiles) {
        break
    }

    Start-Sleep -Milliseconds 200
}
if (-not $ctexFiles) {
    Write-Host "  [WARN] mod_image.png .ctex was not generated. Cover image may not display in-game." -ForegroundColor DarkYellow
} else {
    Write-Host "  mod_image.png .ctex generated successfully." -ForegroundColor Green
}
Write-Host ""

Write-Host "[4/5] Packing PCK resources..." -ForegroundColor Yellow
Invoke-External -FilePath $godot -Arguments @("--headless", "--path", $packProject, "--script", "res://tools/build_pck.gd") -StepName "Godot PCK build"
if (-not (Wait-ForPath -LiteralPath $tempPckPath)) {
    Fail "Packed PCK was not found at $tempPckPath"
}
Copy-Item -LiteralPath $tempPckPath -Destination $finalPckPath -Force
Write-Host "  PCK packed successfully." -ForegroundColor Green
Write-Host ""

Write-Host "[5/5] Assembling release and ZIP..." -ForegroundColor Yellow
if (Test-Path $releaseDir) {
    Remove-PathWithRetry -LiteralPath $releaseDir
}
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Copy-Item -LiteralPath $dllSource -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.dll") -Force
Copy-Item -LiteralPath $finalPckPath -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.pck") -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.json") -Force

$rootConfigPath = Join-Path $root "config.ini"
$releaseConfigPath = Join-Path $releaseDir "config.ini"
if (Test-Path $rootConfigPath) {
    Copy-Item -LiteralPath $rootConfigPath -Destination $releaseConfigPath -Force
} else {
    Write-ConfigTemplate -DestinationPath $releaseConfigPath
}

$metadata = Get-ModMetadata -ManifestPath $manifestPath
$version = $metadata.Version
$modFolderName = $metadata.FolderName
$zipName = "sts2-RMP-$version.zip"
$zipPath = Join-Path $buildRoot $zipName
$zipStageRoot = Join-Path $buildRoot "_zip_stage"
$zipModFolder = Join-Path $zipStageRoot $modFolderName

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path $zipStageRoot) {
    Remove-Item -LiteralPath $zipStageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $zipModFolder | Out-Null
Copy-Item -Path (Join-Path $releaseDir "*") -Destination $zipModFolder -Recurse -Force

$installBatPath = Join-Path $zipStageRoot "Install.bat"
$helperPs1Path = Join-Path $zipStageRoot "helper.ps1"

@'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0helper.ps1"
pause
'@ | Set-Content -LiteralPath $installBatPath -Encoding ASCII

$helperTemplate = @'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$host.UI.RawUI.WindowTitle = 'Remove Multiplayer Player Limit - Installer'

Write-Host '============================================'
Write-Host '  Remove Multiplayer Player Limit v{VERSION}'
Write-Host '  One-Click Installer'
Write-Host '============================================'
Write-Host ''

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
    foreach ($file in $missing) { Write-Host "  - $file" -ForegroundColor Red }
    exit 1
}

$steamPath = $null
try { $steamPath = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction Stop).InstallPath } catch {}
if (-not $steamPath) {
    try { $steamPath = (Get-ItemProperty 'HKCU:\SOFTWARE\Valve\Steam' -ErrorAction Stop).SteamPath } catch {}
}

$gamePath = $null
if ($steamPath) {
    $libraryVdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $libraryVdf) {
        foreach ($line in Get-Content $libraryVdf) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $libraryPath = $Matches[1].Replace('\\', '\')
                $candidate = Join-Path $libraryPath 'steamapps\common\Slay the Spire 2'
                if (Test-Path $candidate) {
                    $gamePath = $candidate
                    break
                }
            }
        }
    }

    if (-not $gamePath) {
        $candidate = Join-Path $steamPath 'steamapps\common\Slay the Spire 2'
        if (Test-Path $candidate) {
            $gamePath = $candidate
        }
    }
}

if (-not $gamePath) {
    Write-Host 'Could not locate Slay the Spire 2 automatically.' -ForegroundColor Red
    Write-Host 'Please copy RemoveMultiplayerPlayerLimit/ into <game>\mods\ manually.'
    exit 1
}

$destination = Join-Path $gamePath 'mods\RemoveMultiplayerPlayerLimit'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath (Join-Path $modFolder '*') -Destination $destination -Recurse -Force

Write-Host ''
Write-Host 'Installation successful.' -ForegroundColor Green
Write-Host "Installed to: $destination"
Write-Host ''
'@

$helperTemplate.Replace("{VERSION}", $version) | Set-Content -LiteralPath $helperPs1Path -Encoding UTF8

Compress-Archive -Path (Join-Path $zipStageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
Remove-PathWithRetry -LiteralPath $zipStageRoot
Remove-PathWithRetry -LiteralPath $packProject

$dllSize = [math]::Round((Get-Item -LiteralPath (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.dll")).Length / 1KB, 1)
$pckSize = [math]::Round((Get-Item -LiteralPath (Join-Path $releaseDir "RemoveMultiplayerPlayerLimit.pck")).Length / 1KB, 1)
$zipSize = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1KB, 1)

Write-Host "  Release directory assembled." -ForegroundColor Green
Write-Host ""
Write-Host "=====================================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Version  : $version"
Write-Host "  DLL      : $dllSize KB"
Write-Host "  PCK      : $pckSize KB"
Write-Host "  ZIP      : $zipPath ($zipSize KB)"
Write-Host "  Release  : $releaseDir"
Write-Host ""
