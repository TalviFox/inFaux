# inFaux Automated Release Script
# https://github.com/TalviFox/inFaux
# Usage: .\release.ps1 -Version "1.0.0"

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $false)]
    [string]$Version,

    [Parameter()]
    [switch]$DryRun,

    [Parameter()]
    [switch]$SkipGit
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$foxEmoji = [char]::ConvertFromUtf32(0x1F98A)
$tadaEmoji = [char]::ConvertFromUtf32(0x1F389)
$clipboardEmoji = [char]::ConvertFromUtf32(0x1F4CB)
$memoEmoji = [char]::ConvertFromUtf32(0x1F4DD)
$lockEmoji = [char]::ConvertFromUtf32(0x1F512)
$rocketEmoji = [char]::ConvertFromUtf32(0x1F680)
$packageEmoji = [char]::ConvertFromUtf32(0x1F4E6)

Write-Host @"
  =============================================================
     $foxEmoji inFaux Release Builder & Hash Signer
     FoxDen Software
  =============================================================
"@ -ForegroundColor DarkCyan

$root = $PSScriptRoot
$csprojPath = Join-Path $root "InFox.csproj"
$publishDir = Join-Path $root "publish"

# 1. Determine Target Version
[xml]$projXml = Get-Content $csprojPath
$currentVersionNode = $projXml.Project.PropertyGroup.Version
$currentVersion = if ($currentVersionNode) { $currentVersionNode.Trim() } else { "1.0.0" }

if (-not $Version) {
    Write-Host "[?] Current version in InFox.csproj is: v$currentVersion" -ForegroundColor Yellow
    $suggested = [Version]::Parse($currentVersion)
    $nextPatch = "$($suggested.Major).$($suggested.Minor).$($suggested.Build + 1)"
    $inputVer = Read-Host "    Enter release version to build (default: $nextPatch)"
    $Version = if ([string]::IsNullOrWhiteSpace($inputVer)) { $nextPatch } else { $inputVer.Trim() }
}

$cleanVersion = $Version.Trim().TrimStart('v', 'V')
Write-Host "[*] Target Release Version: v$cleanVersion" -ForegroundColor Cyan

# 2. Update InFox.csproj Version
if (-not $DryRun) {
    Write-Host "[*] Updating <Version> in InFox.csproj..." -ForegroundColor Cyan
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$cleanVersion</Version>")
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8
}

# 3. Locate .NET SDK
$dotnetPath = "dotnet"
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (Test-Path $localDotnet) {
    $dotnetPath = $localDotnet
}

Write-Host "[*] Using .NET toolchain: $dotnetPath" -ForegroundColor Cyan

# 4. Clean and Publish Single-File Release
Write-Host "[*] Publishing single-file release executable (win-x64)..." -ForegroundColor Cyan
$existingNotes = $null
$releaseNotesPath = Join-Path $publishDir "release_notes.md"
if (Test-Path $releaseNotesPath) {
    $existingNotes = [System.IO.File]::ReadAllText($releaseNotesPath, [System.Text.Encoding]::UTF8)
}

# Terminate running inFaux / inFox processes if updating publish folder
Get-Process -Name "inFaux", "inFox" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

if (Test-Path $publishDir) {
    Get-ChildItem -Path $publishDir -Exclude "release_notes.md" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

$publishArgs = @(
    "publish",
    $csprojPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $publishDir
)

& $dotnetPath $publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[X] Build failed with exit code $LASTEXITCODE!" -ForegroundColor Red
    return
}

$targetExe = Join-Path $publishDir "inFaux.exe"
if (-not (Test-Path $targetExe)) {
    Write-Host "`n[X] Published executable was not found at $targetExe!" -ForegroundColor Red
    return
}

# 5. Calculate SHA-256 Hash and generate SHA256SUMS.txt
Write-Host "[*] Calculating cryptographic SHA-256 hashes..." -ForegroundColor Cyan
$hash = (Get-FileHash -Path $targetExe -Algorithm SHA256).Hash.ToLowerInvariant()
$fileSizeMb = [math]::Round(((Get-Item $targetExe).Length / 1MB), 2)

# Copy companion scripts to publish directory
$installSrc = Join-Path $root "install.ps1"
$uninstallSrc = Join-Path $root "uninstall.ps1"
$verifySrc = Join-Path $root "verify.ps1"
$checksumEntries = [System.Collections.Generic.List[string]]::new()
$checksumEntries.Add("$hash  inFaux.exe")

$installHash = $null
if (Test-Path $installSrc) {
    $dest = Join-Path $publishDir "install.ps1"
    Copy-Item $installSrc -Destination $dest -Force
    $installHash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumEntries.Add("$installHash  install.ps1")
}

$uninstallHash = $null
if (Test-Path $uninstallSrc) {
    $dest = Join-Path $publishDir "uninstall.ps1"
    Copy-Item $uninstallSrc -Destination $dest -Force
    $uninstallHash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumEntries.Add("$uninstallHash  uninstall.ps1")
}

$verifyHash = $null
if (Test-Path $verifySrc) {
    $dest = Join-Path $publishDir "verify.ps1"
    Copy-Item $verifySrc -Destination $dest -Force
    $verifyHash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumEntries.Add("$verifyHash  verify.ps1")
}

$checksumsFile = Join-Path $publishDir "SHA256SUMS.txt"
Set-Content -Path $checksumsFile -Value ($checksumEntries -join "`r`n") -Encoding ASCII

# Also write individual hash file for convenience
$hashFile = Join-Path $publishDir "inFaux.exe.sha256"
Set-Content -Path $hashFile -Value $hash -Encoding ASCII

Write-Host "[+] Binary SHA-256:      $hash" -ForegroundColor Green
if ($installHash) { Write-Host "[+] Installer SHA-256:   $installHash" -ForegroundColor Green }
if ($uninstallHash) { Write-Host "[+] Uninstaller SHA-256: $uninstallHash" -ForegroundColor Green }
if ($verifyHash) { Write-Host "[+] Verifier SHA-256:    $verifyHash" -ForegroundColor Green }
Write-Host "[+] Checksums file:      $checksumsFile" -ForegroundColor Green

# 6. Git Commit & Tagging (if not DryRun or SkipGit)
if (-not $DryRun -and -not $SkipGit -and (Test-Path (Join-Path $root ".git"))) {
    try {
        Write-Host "[*] Creating Git release commit and tag v$cleanVersion..." -ForegroundColor Cyan
        git add InFox.csproj
        git commit -m "Release v$cleanVersion" -ErrorAction SilentlyContinue
        git tag -a "v$cleanVersion" -m "inFaux Release v$cleanVersion"
        Write-Host "[+] Git tag 'v$cleanVersion' created." -ForegroundColor Green
    }
    catch {
        Write-Host "[!] Git tagging failed: $_" -ForegroundColor Yellow
    }
}

# 7. Output Release Guidance
$bt = [char]96
$tripleBt = "$bt$bt$bt"

Write-Host @"

  =============================================================
     $tadaEmoji RELEASE v$cleanVersion BUILT SUCCESSFULLY!
  =============================================================
  Output Binary:    $targetExe ($fileSizeMb MB)
  SHA-256 Hash:     $hash
  Checksums File:   $checksumsFile

  -------------------------------------------------------------
  $clipboardEmoji COPY & PASTE FOR GITHUB RELEASE NOTES:
  -------------------------------------------------------------
  ## $lockEmoji Checksums & Binary Verification
  | File | SHA-256 Checksum |
  | :--- | :--- |
  | **inFaux.exe** | ${bt}$hash${bt} |
$(if ($installHash) { "  | **install.ps1** | ${bt}$installHash${bt} |`n" })$(if ($uninstallHash) { "  | **uninstall.ps1** | ${bt}$uninstallHash${bt} |`n" })$(if ($verifyHash) { "  | **verify.ps1** | ${bt}$verifyHash${bt} |`n" })
  Verify integrity before running (PowerShell):
  ${tripleBt}powershell
  Get-FileHash .\inFaux.exe -Algorithm SHA256
  ${tripleBt}
  -------------------------------------------------------------

  $packageEmoji GITHUB RELEASE ASSETS TO UPLOAD:
  1. publish\inFaux.exe
  2. publish\install.ps1
  3. publish\uninstall.ps1
  4. publish\verify.ps1
  5. publish\SHA256SUMS.txt
  6. publish\inFaux.exe.sha256
  7. publish\release_notes.md (Use this for the GitHub release body!)

  $rocketEmoji NEXT STEP (GIT PUSH):
  git push origin HEAD --tags
  =============================================================
"@ -ForegroundColor Green

# 8. Generate release_notes.md
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$checksumRows = [System.Collections.Generic.List[string]]::new()
$checksumRows.Add("| **inFaux.exe** | ${bt}$hash${bt} |")
if ($installHash) { $checksumRows.Add("| **install.ps1** | ${bt}$installHash${bt} |") }
if ($uninstallHash) { $checksumRows.Add("| **uninstall.ps1** | ${bt}$uninstallHash${bt} |") }
if ($verifyHash) { $checksumRows.Add("| **verify.ps1** | ${bt}$verifyHash${bt} |") }

$checksumSection = @"
## $lockEmoji Checksums & Binary Verification

| File | SHA-256 Checksum |
| :--- | :--- |
$($checksumRows -join "`r`n")

Verify integrity before running (PowerShell):

${tripleBt}powershell
Get-FileHash .\inFaux.exe -Algorithm SHA256
${tripleBt}
"@

if ($existingNotes -and $existingNotes -match "(?m)^## .*What's New in v$cleanVersion") {
    # Preserve existing custom changelog for this version, only update title and checksums table
    $updatedNotes = [regex]::Replace($existingNotes, "(?m)^# .*inFaux v.*$", "# $foxEmoji inFaux v$cleanVersion")
    $updatedNotes = [regex]::Replace($updatedNotes, "(?ms)^## [^\r\n]*Checksums & Binary Verification.*$", $checksumSection)
    [System.IO.File]::WriteAllText($releaseNotesPath, $updatedNotes, $utf8NoBom)
    Write-Host "[+] Updated checksums in existing release notes at $releaseNotesPath" -ForegroundColor Green
}
else {
    $releaseNotesTemplate = @"
# $foxEmoji inFaux v$cleanVersion
*FoxDen Software*

The modern, native hardware monitor and open telemetry server for Windows. Real telemetry. Faux drivers.

## $memoEmoji What's New in v$cleanVersion

- Initial Release of inFaux
- Curated human-first telemetry for CPU, GPU, RAM, NVMe, Battery, and Network
- Embedded local REST and WebSocket API on port 8765
- Dynamic numeric temperature badge rendering in the Windows System Tray
- Zero bloat, no drivers, sub-0.1% CPU overhead

$checksumSection
"@

    [System.IO.File]::WriteAllText($releaseNotesPath, $releaseNotesTemplate, $utf8NoBom)
    Write-Host "[+] Draft release notes saved to $releaseNotesPath" -ForegroundColor Green
}

