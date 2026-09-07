#Requires -RunAsAdministrator
sc.exe stop SelectiveVpnCallout
sc.exe delete SelectiveVpnCallout
$sys = Join-Path $env:SystemRoot "System32\drivers\SelectiveVpnCallout.sys"
if (Test-Path $sys) {
    Remove-Item $sys -Force
}
Write-Host "Callout driver service removed."
