<#
.SYNOPSIS
  Stop and delete the callout driver service. Does not change boot security.
#>
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "_common.ps1")

if (-not (Test-SvrElevated)) {
    Write-SvrResult "FAIL" "elevation" "Re-run from an elevated PowerShell."
    exit 1
}

sc.exe stop SelectiveVpnCallout | Out-Null
Start-Sleep -Milliseconds 400
sc.exe delete SelectiveVpnCallout | Out-Null
$sys = Join-Path $env:SystemRoot "System32\drivers\SelectiveVpnCallout.sys"
if (Test-Path $sys) {
    try {
        Remove-Item $sys -Force
        Write-SvrResult "PASS" "sys-file" "Removed $sys"
    } catch {
        Write-SvrResult "WARNING" "sys-file" "Could not delete $sys (reboot may be required): $_"
    }
}

$left = Get-Service SelectiveVpnCallout -ErrorAction SilentlyContinue
if ($left) {
    Write-SvrResult "FAIL" "uninstall-driver" "Service still present: $($left.Status)"
    exit 1
}

Write-SvrResult "PASS" "uninstall-driver" "Callout service removed. User-mode WFP dynamic filters are not this driver's objects."
exit 0
