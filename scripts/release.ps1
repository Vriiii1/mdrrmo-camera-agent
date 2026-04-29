# release.ps1 — Build, (optionally) sign, and Velopack-pack a release.
# Usage:
#   .\scripts\release.ps1 -Version 0.3.0
#   .\scripts\release.ps1 -Version 0.3.0 -SkipSign   # CI / no EV cert
#
# Prerequisites:
#   dotnet SDK 8+
#   vpk CLI: dotnet tool install -g vpk
#
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$SkipSign
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Verify vpk is available ───────────────────────────────────────────────────
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error @"
vpk CLI not found. Install it with:
    dotnet tool install -g vpk
Then ensure the dotnet global tools path is on your PATH (e.g. %USERPROFILE%\.dotnet\tools).
"@
    exit 1
}

$dist     = "./dist"
$releases = "./releases"

# ── Publish ───────────────────────────────────────────────────────────────────
Write-Host "[release] Publishing v$Version ..."
dotnet publish src/MdrrmoCameraAgent `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $dist

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ── EV sign (skipped in CI) ───────────────────────────────────────────────────
# EV cert signing is deferred to Task 25. Use -SkipSign when no cert is available.
if (-not $SkipSign) {
    Write-Host "[release] Signing $dist/MdrrmoCameraAgent.exe ..."
    & signtool sign `
        /tr  http://timestamp.digicert.com `
        /td  sha256 `
        /fd  sha256 `
        /a   "$dist/MdrrmoCameraAgent.exe"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool failed (exit $LASTEXITCODE). Use -SkipSign to bypass."
        exit $LASTEXITCODE
    }
}
else {
    Write-Host "[release] Skipping code signing (-SkipSign)."
}

# ── Velopack pack ─────────────────────────────────────────────────────────────
Write-Host "[release] Packing with vpk ..."
vpk pack `
    --packId       MdrrmoCameraAgent `
    --packVersion  $Version `
    --packDir      $dist `
    --mainExe      MdrrmoCameraAgent.exe `
    --outputDir    $releases

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[release] Done. Artifacts in $releases"
