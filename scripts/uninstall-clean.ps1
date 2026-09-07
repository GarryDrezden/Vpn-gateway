#Requires -RunAsAdministrator
# Stop service, remove owned leftover via starting service is not done here.
# Clean uninstall: service + driver. Routes/filters: start service once with no VPN or use Emergency Restore before this.

$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "uninstall-service.ps1")
& (Join-Path $PSScriptRoot "uninstall-driver.ps1")

$pd = Join-Path $env:ProgramData "SelectiveVpnRouter"
if (Test-Path $pd) {
    Remove-Item $pd -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed $pd"
}
Write-Host "Clean uninstall attempted. Foreign VPN routes were not deleted."
