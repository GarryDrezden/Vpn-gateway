#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Get-SvrRepoRoot
$publishDir = Get-SvrPublishDirectory -Root $root
$appExe = Join-Path $publishDir "SelectiveVpnRouter.App.exe"
$serviceName = "SelectiveVpnRouter"
$serviceTimeout = New-TimeSpan -Seconds 30

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("Running", "Stopped")][string]$Status
    )
    $svc = Get-Service -Name $Name -ErrorAction Stop
    $svc.WaitForStatus($Status, $serviceTimeout)
}

# 1. Stop all GUI instances before touching publish directory.
Stop-AllSvrGuiProcesses -TimeoutSeconds 10

# 2. Stop Windows Service and wait until Stopped (force service process if needed).
$serviceExisted = Stop-SvrServiceForPublish -ServiceName $serviceName -TimeoutSeconds 15

# 3. Ensure no executable from publish directory is still running.
Ensure-SvrPublishDirectoryUnlocked -PublishDir $publishDir -ProcessExitTimeoutSeconds 10
Write-SvrResult -Outcome INFO -Name "publish-lock-check" -Message "no processes using publish directory"

# 4. Remove old publish folder with retry (handles lingering DLL handles).
Remove-DirectoryWithRetry -Path $publishDir -PublishDir $publishDir

# 5. Publish managed binaries.
& (Join-Path $PSScriptRoot "publish-desktop.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-SvrResult -Outcome FAIL -Name "update-desktop" -Message "publish-desktop.ps1 failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-SvrResult -Outcome PASS -Name "publish" -Message $publishDir

# 6. Install or reconfigure service, then start.
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
