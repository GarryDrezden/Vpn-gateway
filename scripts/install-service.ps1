#Requires -RunAsAdministrator
param(
    [string]$BinPath = ""
)

$ErrorActionPreference = "Stop"
if (-not $BinPath) {
    $BinPath = Join-Path $PSScriptRoot "..\src\SelectiveVpnRouter.Service\bin\Release\net10.0-windows\SelectiveVpnRouter.Service.exe"
}
$BinPath = [System.IO.Path]::GetFullPath($BinPath)
if (-not (Test-Path $BinPath)) {
    throw "Service executable not found: $BinPath — build Release first (dotnet publish / dotnet build -c Release)."
}

sc.exe stop SelectiveVpnRouter 2>$null | Out-Null
sc.exe delete SelectiveVpnRouter 2>$null | Out-Null
sc.exe create SelectiveVpnRouter binPath= "`"$BinPath`"" start= demand DisplayName= "Selective VPN Router"
sc.exe description SelectiveVpnRouter "Elevated backend for Selective VPN Router (OpenVPN, owned routes, WFP, proxy)."
sc.exe start SelectiveVpnRouter
Write-Host "Installed and started SelectiveVpnRouter from $BinPath"
