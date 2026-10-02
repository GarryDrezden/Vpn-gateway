#Requires -Version 5.1
<#
.SYNOPSIS
  Offline VPN Route connection lifecycle regression (health, routing smoke, reconnect stress, cleanup).

.DESCRIPTION
  User must start this runner manually while VPN Route service is running and VPN Route is Connected.
  Reconnect stress uses confirm=true and will Connect/Disconnect VPN Route via service diagnostics.
  Does NOT control external VPN software.
#>

param(
    [int]$IpcReadinessSeconds = 30,
    [int]$WaitConnectedSeconds = 300,
    [int]$DiagnosticTimeoutMs = 600000
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "_vpn-lifecycle-acceptance.ps1")

$runner = Join-Path $PSScriptRoot "run-vpn-lifecycle-diagnostic.ps1"
$names = Get-VpnLifecycleAcceptanceDiagnosticNames

& $runner `
    -DiagnosticNames $names `
    -IpcReadinessSeconds $IpcReadinessSeconds `
    -WaitConnectedSeconds $WaitConnectedSeconds `
    -DiagnosticTimeoutMs $DiagnosticTimeoutMs

$exitCode = $LASTEXITCODE
if ($exitCode -eq 0) {
    Write-Host "VPN LIFECYCLE REGRESSION: PASS"
}
else {
    Write-Host "VPN LIFECYCLE REGRESSION: FAIL"
}

exit $exitCode
