#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"
sc.exe stop SelectiveVpnRouter
sc.exe delete SelectiveVpnRouter
Write-Host "Service removed. Driver and owned routes are not touched; run uninstall-driver.ps1 and/or Emergency Restore if needed."
