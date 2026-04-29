$ErrorActionPreference = "Stop"
$root    = (Resolve-Path "$PSScriptRoot/..").Path
$verFile = Join-Path $root "third-party/WINSW_VERSION.txt"
$ver     = (Get-Content $verFile | Select-Object -First 1).Trim()
$expectedSha = ((Get-Content $verFile)[1] -replace 'sha256:\s*','').Trim()

# Plan-check [W2] - refuse to run against a placeholder. SHAs are 64 lowercase hex.
if ($expectedSha -notmatch '^[a-f0-9]{64}$') {
    throw "WINSW_VERSION.txt sha256 is not a valid 64-hex string (got: '$expectedSha'). Fill in the upstream digest before running this script."
}

$dest = Join-Path $root "third-party/winsw.exe"
if (Test-Path $dest) {
    $actual = (Get-FileHash $dest -Algorithm SHA256).Hash.ToLower()
    if ($actual -eq $expectedSha) { Write-Host "winsw.exe v$ver already present; checksum OK"; exit 0 }
}

$tmp = Join-Path $env:TEMP "WinSW-x64_v$ver.exe"
Invoke-WebRequest -Uri "https://github.com/winsw/winsw/releases/download/v$ver/WinSW-x64.exe" -OutFile $tmp
$actual = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
if ($actual -ne $expectedSha) { throw "SHA mismatch: expected $expectedSha got $actual" }
Copy-Item $tmp $dest -Force
Remove-Item $tmp
Write-Host "winsw.exe v$ver fetched and verified"
