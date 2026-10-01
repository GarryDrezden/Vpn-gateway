#Requires -Version 5.1
param(
    [string]$ProbePath,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_common.ps1")
if ($Quiet) { $global:SvrUpdateQuiet = $true }

$repoRoot = Get-SvrRepoRoot
$probe = if ($ProbePath) { $ProbePath } else {
    Join-Path $repoRoot "artifacts\publish\SelectiveVpnRouter\SelectiveVpnRouter.Probe.exe"
}

if (-not (Test-Path -LiteralPath $probe)) {
    $probe = Join-Path $repoRoot "src\SelectiveVpnRouter.Probe\bin\Release\net10.0-windows\SelectiveVpnRouter.Probe.exe"
}

if (-not (Test-Path -LiteralPath $probe)) {
    Write-SvrUpdateDetail "Building Release Probe..."
    Invoke-SvrDotNet -ArgumentList @("build", (Join-Path $repoRoot "SelectiveVpnRouter.sln"), "-c", "Release") -WorkingDirectory $repoRoot | Out-Null
    $probe = Join-Path $repoRoot "src\SelectiveVpnRouter.Probe\bin\Release\net10.0-windows\SelectiveVpnRouter.Probe.exe"
}

if (-not (Test-Path -LiteralPath $probe)) {
    throw "Probe not found: $probe"
}

$detail = "Running network catalog smoke: $probe"
if ($Quiet) {
    Write-Output $detail
    & $probe --smoke-network-catalog
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    Write-Output "network-catalog-smoke: OK"
    exit 0
}

Write-Host $detail
& $probe --smoke-network-catalog
if ($LASTEXITCODE -ne 0) {
    throw "network-catalog-smoke failed with exit code $LASTEXITCODE"
}

Write-Host "network-catalog-smoke: OK"