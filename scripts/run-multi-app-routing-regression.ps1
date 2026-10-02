#Requires -Version 5.1
<#
.SYNOPSIS
  Offline multi-app routing / isolation regression (synthetic A/B/C matrix).

.DESCRIPTION
  User must start this runner manually while VPN Route service is running and VPN Route is Connected.
  Does NOT control external VPN software.
#>

param(
    [int]$IpcReadinessSeconds = 30,
    [int]$WaitConnectedSeconds = 300,
    [int]$DiagnosticTimeoutMs = 900000
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "_multi-app-routing-acceptance.ps1")

$runner = Join-Path $PSScriptRoot "run-multi-app-routing-diagnostic.ps1"

& $runner `
    -IpcReadinessSeconds $IpcReadinessSeconds `
    -WaitConnectedSeconds $WaitConnectedSeconds `
    -DiagnosticTimeoutMs $DiagnosticTimeoutMs

$exitCode = $LASTEXITCODE
if ($exitCode -eq 0) {
    Write-Host (Get-SvrMultiAppRoutingRegressionPassMarker)
    Write-Host "MULTI-APP ROUTING REGRESSION: PASS"
}
else {
    Write-Host (Get-SvrMultiAppRoutingRegressionFailMarker)
    Write-Host "MULTI-APP ROUTING REGRESSION: FAIL"
}

exit $exitCode
