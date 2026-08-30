# Packages Theme Forge into a .pext installer.
#   ./build/build.ps1                          # auto-detect Playnite
#   ./build/build.ps1 -PlayniteDir "E:\Software\Playnite" -OutDir ./dist
[CmdletBinding()]
param(
    [string]$PlayniteDir,
    [string]$OutDir,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo "dist" }

# --- locate Playnite (Toolbox.exe does the actual packing) ---------------
if (-not $PlayniteDir) {
    $candidates = @(
        $env:PLAYNITE_DIR,
        (Join-Path $env:LOCALAPPDATA "Playnite"),
        "C:\Program Files\Playnite",
        "C:\Program Files (x86)\Playnite"
    ) | Where-Object { $_ }
    $PlayniteDir = $candidates | Where-Object { Test-Path (Join-Path $_ "Toolbox.exe") } | Select-Object -First 1
}
if (-not $PlayniteDir -or -not (Test-Path (Join-Path $PlayniteDir "Toolbox.exe"))) {
    throw "Toolbox.exe not found. Pass -PlayniteDir with your Playnite installation folder."
}
$toolbox = Join-Path $PlayniteDir "Toolbox.exe"
Write-Host "Playnite : $PlayniteDir"

# --- build ---------------------------------------------------------------
$proj = Join-Path $repo "source\ThemeForge.csproj"
Write-Host "Building : $Configuration"
& dotnet build $proj -c $Configuration -v m
if ($LASTEXITCODE -ne 0) { throw "build failed" }

# --- stage ---------------------------------------------------------------
# Toolbox packs a folder as-is, so stage only what should ship.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("ThemeForge_stage_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    $src = Join-Path $repo "source"
    Copy-Item (Join-Path $src "extension.yaml") $stage
    Copy-Item (Join-Path $src "icon.png")       $stage
    Copy-Item (Join-Path $src "Localization")   $stage -Recurse
    Get-ChildItem (Join-Path $src "bin\$Configuration") -Filter *.dll |
        Where-Object { $_.Name -ne "Playnite.SDK.dll" } |
        ForEach-Object { Copy-Item $_.FullName $stage }

    if (-not (Test-Path (Join-Path $stage "ThemeForge.dll"))) { throw "ThemeForge.dll missing from staged output" }

    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    & $toolbox pack $stage $OutDir
    if ($LASTEXITCODE -ne 0) { throw "toolbox pack failed" }

    Get-ChildItem $OutDir -Filter *.pext | Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 Name, Length, LastWriteTime | Format-List
    Write-Host "Done -> $OutDir"
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
