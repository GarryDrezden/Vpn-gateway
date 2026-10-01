#Requires -RunAsAdministrator
param(
    [switch]$UseShortPathFilter,
    [switch]$LaunchViaShortPath,
    [switch]$RunAbExperiment
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "_update-helpers.ps1")

$unicodeDir = Join-Path $env:USERPROFILE ([char]0x0422 + [char]0x0435 + [char]0x0441 + [char]0x0442 + " VPN Route")
$longExe = Join-Path $unicodeDir "SelectiveVpnRouter.Probe.exe"
$publish = Get-SvrPublishDirectory -Root (Get-SvrRepoRoot)
$probeSrc = Join-Path $publish "SelectiveVpnRouter.Probe.exe"
if (-not (Test-Path -LiteralPath $longExe)) {
    New-Item -ItemType Directory -Force -Path $unicodeDir | Out-Null
    Copy-Item -LiteralPath $probeSrc -Destination $longExe -Force
}

$fso = New-Object -ComObject Scripting.FileSystemObject
$shortExe = $fso.GetFile($longExe).ShortPath
Write-Host "LONG  = $longExe"
Write-Host "SHORT = $shortExe"

if ($RunAbExperiment) {
    $payload = (@{ name = "wfp-probe-short-appid-ab" } | ConvertTo-Json -Compress)
    $resp = Invoke-SvrIpc -Method "RunDiagnostic" -PayloadJson $payload -TimeoutMs 120000
    Write-Host ($resp | ConvertTo-Json -Depth 6)
    exit 0
}

$mode = if ($UseShortPathFilter) { 2 } else { 0 }
$applyPath = if ($UseShortPathFilter) { $shortExe } else { $longExe }
$launch = if ($LaunchViaShortPath) { $shortExe } else { $longExe }

Invoke-SvrIpc -Method "RemoveTempAppVpnRoute" -TimeoutMs 30000 | Out-Null
$applyPayload = (@{ exePath = $applyPath; identityPathMode = $mode } | ConvertTo-Json -Compress)
$st = Invoke-SvrIpc -Method "ApplyTempAppVpnRoute" -PayloadJson $applyPayload -TimeoutMs 30000
Write-Host "Apply: $($st.PayloadJson)"
Write-Host "Launch: & `"$launch`" --http https://api.ipify.org"