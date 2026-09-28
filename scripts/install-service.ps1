#Requires -RunAsAdministrator
param(
    [string]$BinPath = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

if (-not $BinPath) {
    $publish = Join-Path (Get-SvrRepoRoot) "artifacts\publish\SelectiveVpnRouter\SelectiveVpnRouter.Service.exe"
    $build = Join-Path (Get-SvrRepoRoot) "src\SelectiveVpnRouter.Service\bin\Release\net10.0-windows\SelectiveVpnRouter.Service.exe"
    $BinPath = if (Test-Path $publish) { $publish } else { $build }
}
$BinPath = [System.IO.Path]::GetFullPath($BinPath)
if (-not (Test-Path $BinPath)) {
    throw "Service executable not found: $BinPath - build Release first (dotnet publish / dotnet build -c Release)."
}

$quotedBinPath = '"' + $BinPath + '"'

Initialize-SvrProgramData

sc.exe stop SelectiveVpnRouter 2>$null | Out-Null
sc.exe delete SelectiveVpnRouter 2>$null | Out-Null
sc.exe create SelectiveVpnRouter binPath= $quotedBinPath start= auto DisplayName= "Selective VPN Router"
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE" }
sc.exe description SelectiveVpnRouter "Elevated backend for Selective VPN Router (OpenVPN, owned routes, WFP, proxy)."
sc.exe start SelectiveVpnRouter
if ($LASTEXITCODE -ne 0) { throw "sc.exe start failed with exit code $LASTEXITCODE" }

$svc = Get-Service SelectiveVpnRouter -ErrorAction Stop
$svc.WaitForStatus("Running", (New-TimeSpan -Seconds 30))
if ($svc.Status -ne "Running") {
    throw "SelectiveVpnRouter service is not Running (status: $($svc.Status))."
}

$startMode = (Get-CimInstance Win32_Service -Filter "Name='SelectiveVpnRouter'").StartMode
if ($startMode -ne "Auto") {
    throw "SelectiveVpnRouter StartType is '$startMode', expected Auto."
}

Write-SvrResult -Outcome PASS -Name "install-service" -Message "Installed from $BinPath; Status=Running; StartType=Automatic"