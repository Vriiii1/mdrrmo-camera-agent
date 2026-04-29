# scripts/build-installer.ps1
# Builds the Inno Setup installer for the MDRRMO Camera Agent.
#
# Prerequisites:
#   1. Inno Setup 6 installed (https://jrsoftware.org/isdl.php)
#   2. Third-party binaries fetched:  pwsh ./scripts/fetch-winsw.ps1
#                                     pwsh ./scripts/fetch-mediamtx.ps1
#   3. Agent published:
#        dotnet publish -c Release -r win-x64 --self-contained `
#               -p:PublishSingleFile=true -o ./dist
#
# Usage:
#   pwsh ./scripts/build-installer.ps1 -Version 0.1.0
#
# Output:
#   output/MdrrmoCameraAgent-Setup-<Version>.exe

param(
    [string]$Version = "0.4.0"
)

$ErrorActionPreference = "Stop"

# Locate ISCC.exe (Inno Setup 6 compiler) — try multiple locations
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not (Test-Path $iscc)) {
    Write-Error "Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php or add ISCC.exe to PATH."
    exit 1
}

$root = (Resolve-Path "$PSScriptRoot/..").Path
$issFile = Join-Path $root "packaging/installer.iss"

if (-not (Test-Path $issFile)) {
    Write-Error "ISS script not found: $issFile"
    exit 1
}

Write-Host "Building installer v$Version ..."
Write-Host "  ISCC   : $iscc"
Write-Host "  Script : $issFile"

& $iscc "/DAppVersion=$Version" $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC.exe failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# OutputBaseFilename in installer.iss is intentionally version-less so the
# GitHub `releases/latest/download/` alias keeps working without env-var
# rotation. The version is still embedded inside the installer (AppVersion).
$outExe = Join-Path $root "output\MdrrmoCameraAgent-Setup.exe"
Write-Host "Done. Installer written to: $outExe (AppVersion=$Version)"
