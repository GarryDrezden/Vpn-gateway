#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Get-SvrRepoRoot
$publishDir = Join-Path $root "artifacts\publish\SelectiveVpnRouter"
$appExe = Join-Path $publishDir "SelectiveVpnRouter.App.exe"
$serviceName = "SelectiveVpnRouter"
$serviceTimeout = New-TimeSpan -Seconds 30

function Stop-AppIfRunning {
    $proc = Get-Process -Name "SelectiveVpnRouter.App" -ErrorAction SilentlyContinue
    if (-not $proc) { return }
    Write-SvrResult -Outcome INFO -Name "update-desktop" -Message "Stopping SelectiveVpnRouter.App (PID $($proc.Id))."
    $proc | Stop-Process -Force
    $proc.WaitForExit(5000)
}

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("Running", "Stopped")][string]$Status
    )
    $svc = Get-Service -Name $Name -ErrorAction Stop
    $svc.WaitForStatus($Status, $serviceTimeout)
}

Stop-AppIfRunning

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$serviceExisted = [bool]$service
if ($serviceExisted) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        Wait-ServiceStatus -Name $serviceName -Status Stopped
    }
}

& (Join-Path $PSScriptRoot "publish-desktop.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-SvrResult -Outcome FAIL -Name "update-desktop" -Message "publish-desktop.ps1 failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-SvrResult -Outcome PASS -Name "publish" -Message $publishDir

if (-not $serviceExisted) {
    & (Join-Path $PSScriptRoot "install-service.ps1") -BinPath (Join-Path $publishDir "SelectiveVpnRouter.Service.exe")
    if ($LASTEXITCODE -ne 0) {
        Write-SvrResult -Outcome FAIL -Name "update-desktop" -Message "install-service.ps1 failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
}
else {
    Set-Service -Name $serviceName -StartupType Automatic
}

Start-Service -Name $serviceName
Wait-ServiceStatus -Name $serviceName -Status Running

$svc = Get-Service -Name $serviceName
$startMode = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").StartMode
$startTypeLabel = if ($startMode -eq "Auto") { "Auto" } else { $startMode }
Write-SvrResult -Outcome PASS -Name "service" -Message "Status=$($svc.Status); StartType=$startTypeLabel"
Write-SvrResult -Outcome INFO -Name "GUI" -Message $appExe
Write-SvrResult -Outcome INFO -Name "update-desktop" -Message "completed. GUI was not started automatically."
