$ErrorActionPreference = "Stop"
$root    = (Resolve-Path "$PSScriptRoot/..").Path
$verFile = Join-Path $root "third-party/FFMPEG_VERSION.txt"
$lines   = Get-Content $verFile
$ver     = ($lines | Select-Object -First 1).Trim()
$url     = (($lines | Where-Object { $_ -match '^url:' })    -replace 'url:\s*','').Trim()
$expectedSha = (($lines | Where-Object { $_ -match '^sha256:' }) -replace 'sha256:\s*','').Trim()

# Plan-check [M2] - refuse to run against a placeholder.
if ($expectedSha -notmatch '^[a-f0-9]{64}$') {
    throw "FFMPEG_VERSION.txt sha256 is not a valid 64-hex string (got: '$expectedSha'). Compute via 'Get-FileHash <release-zip> -Algorithm SHA256' and paste the digest before running this script."
}

$dest = Join-Path $root "third-party/ffmpeg.exe"
if (Test-Path $dest) {
    # Skip re-download but always re-verify the WHIP muxer is in this binary.
    $muxers = & $dest -hide_banner -muxers 2>&1 | Out-String
    if ($muxers -match 'whip') { Write-Host "ffmpeg.exe v$ver already present; WHIP muxer verified"; exit 0 }
}

$tmp = Join-Path $env:TEMP "ffmpeg_v$ver.zip"
Invoke-WebRequest -Uri $url -OutFile $tmp
$actual = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
if ($actual -ne $expectedSha) { throw "ffmpeg SHA mismatch: expected $expectedSha got $actual" }
$extractDir = Join-Path $env:TEMP "ffmpeg_v${ver}_extract"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
Expand-Archive -Path $tmp -DestinationPath $extractDir -Force
$ffexe = Get-ChildItem -Path $extractDir -Filter ffmpeg.exe -Recurse | Select-Object -First 1
if (-not $ffexe) { throw "ffmpeg.exe not found in archive" }
Copy-Item $ffexe.FullName $dest -Force
Remove-Item $tmp
Remove-Item $extractDir -Recurse -Force

# Hard gate: refuse to bundle an ffmpeg without WHIP.
$muxers = & $dest -hide_banner -muxers 2>&1 | Out-String
if (-not ($muxers -match 'whip')) {
    Remove-Item $dest -Force
    throw "ffmpeg v$ver does NOT have the WHIP muxer. Pick a different build."
}
Write-Host "ffmpeg.exe v$ver fetched + WHIP muxer verified"
