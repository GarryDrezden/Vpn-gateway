#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)]
    [string]$SysPath
)

$ErrorActionPreference = "Stop"
$SysPath = [System.IO.Path]::GetFullPath($SysPath)
if (-not (Test-Path $SysPath)) {
    throw "SYS not found: $SysPath"
}

sc.exe stop SelectiveVpnCallout 2>$null | Out-Null
sc.exe delete SelectiveVpnCallout 2>$null | Out-Null
sc.exe create SelectiveVpnCallout type= kernel start= demand binPath= "$SysPath"
sc.exe start SelectiveVpnCallout
Write-Host "Callout driver started from $SysPath"
Write-Host "Development machines typically need testsigning (bcdedit /set testsigning on) — the app never enables it."
