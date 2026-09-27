# =============================================================
#  🦊 inFaux Integrity Auditor & Verification Script
#  https://github.com/TalviFox/inFaux
#  Cryptographically verifies local inFaux.exe against official GitHub release hashes.
# =============================================================

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "Stop"

$repo = "TalviFox/inFaux"
$targetExe = $null

if ($Path -and (Test-Path $Path)) {
    $targetExe = (Resolve-Path $Path).Path
}
elseif (Test-Path "$env:LOCALAPPDATA\Programs\inFaux\inFaux.exe") {
    $targetExe = "$env:LOCALAPPDATA\Programs\inFaux\inFaux.exe"
}
elseif (Test-Path "$env:ProgramFiles\inFaux\inFaux.exe") {
    $targetExe = "$env:ProgramFiles\inFaux\inFaux.exe"
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "publish\inFaux.exe"))) {
    $targetExe = Join-Path $PSScriptRoot "publish\inFaux.exe"
}
elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot "inFaux.exe"))) {
    $targetExe = Join-Path $PSScriptRoot "inFaux.exe"
}

if (-not $targetExe) {
    Write-Host "[X] Could not locate inFaux.exe to verify." -ForegroundColor Red
    return
}

Write-Host "[*] Auditing binary: $targetExe" -ForegroundColor Cyan
$actualHash = (Get-FileHash -Path $targetExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "[+] Local SHA-256:   $actualHash" -ForegroundColor White

Write-Host "[*] Querying latest release from $repo..." -ForegroundColor Cyan
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "inFaux-Verifier" }
    $expectedHash = $null

    $sumsAsset = $release.assets | Where-Object { $_.name -match "^(SHA256SUMS|inFaux.*)\.(txt|sha256)$" -or $_.name -eq "inFaux.exe.sha256" } | Select-Object -First 1
    if ($sumsAsset) {
        $checksumText = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -Headers @{ "User-Agent" = "inFaux-Verifier" }
        if ($checksumText -match "([a-fA-F0-9]{64})\s+.*inFaux\.exe") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
        elseif ($checksumText -match "\b([a-fA-F0-9]{64})\b") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
    }

    if (-not $expectedHash -and $release.body -match "\b([a-fA-F0-9]{64})\b") {
        $expectedHash = $matches[1].ToLowerInvariant()
    }

    if ($expectedHash) {
        Write-Host "[+] Official SHA-256: $expectedHash ($($release.tag_name))" -ForegroundColor Cyan
        if ($actualHash -eq $expectedHash) {
            Write-Host "`n[+] VERIFICATION PASSED: Binary is 100% authentic and unaltered!" -ForegroundColor Green
        }
        else {
            Write-Host "`n[X] VERIFICATION FAILED: Local hash does not match official release!" -ForegroundColor Red
        }
    }
    else {
        Write-Host "[!] Could not extract official checksum from latest release." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "[!] Could not reach GitHub API: $_" -ForegroundColor Yellow
}
