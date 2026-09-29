#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\_common.ps1"

$repoRoot = Get-SvrRepoRoot
$probe = Join-Path $repoRoot 'src\SelectiveVpnRouter.Probe\bin\Release\net10.0-windows\SelectiveVpnRouter.Probe.exe'
if (-not (Test-Path -LiteralPath $probe)) {
    Write-Host 'Building Release Probe...'
    dotnet build (Join-Path $repoRoot 'SelectiveVpnRouter.sln') -c Release
}

if (-not (Test-Path -LiteralPath $probe)) {
    throw "Probe not found: $probe"
}

Write-Host "Running network catalog smoke: $probe"
& $probe --smoke-network-catalog
if ($LASTEXITCODE -ne 0) {
    throw "network-catalog-smoke failed with exit code $LASTEXITCODE"
}

Write-Host 'network-catalog-smoke: OK'
