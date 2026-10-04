#Requires -RunAsAdministrator
param([switch]$VerboseOutput)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Get-SvrRepoRoot
Initialize-SvrUpdateSession -Quiet:(-not $VerboseOutput) -RepoRoot $root

$liveDir = Get-SvrPublishDirectory -Root $root
$serviceExe = Join-Path $liveDir "SelectiveVpnRouter.Service.exe"
$stagingDriver = Get-SvrDriverStagingSysPath -Root $root -Configuration Release
$publishDriver = Get-SvrDriverRuntimeSysPath -Root $root

Write-SvrUpdateLogLine "=== recover-publish-runtime ==="
$snapshot = Get-SvrPublishRuntimeSnapshot
Write-SvrPublishRuntimeSnapshot -Snapshot $snapshot

$targetDriver = $null
if (Test-Path -LiteralPath $stagingDriver) { $targetDriver = $stagingDriver }
elseif (Test-Path -LiteralPath $publishDriver) { $targetDriver = $publishDriver }
elseif ($snapshot.DriverImagePath) {
    $targetDriver = ConvertFrom-SvrKernelImagePath -ImagePath $snapshot.DriverImagePath
}

if (-not $targetDriver -or -not (Test-Path -LiteralPath $targetDriver)) {
    Write-SvrUpdateLogLine "FAIL recover: no SelectiveVpnCallout.sys found (staging or publish layout)"
    exit 1
}

Stop-SvrPublishRuntimeForTreeSwap -PublishDir $liveDir
Set-SvrKernelDriverBinPath -SysPath $publishDriver
if ($targetDriver -ne $publishDriver) {
    New-Item -ItemType Directory -Force -Path (Split-Path $publishDriver -Parent) | Out-Null
    Copy-Item -LiteralPath $targetDriver -Destination $publishDriver -Force
}
Repair-SvrPublishRuntimeImagePaths -PublishDir $liveDir -Snapshot $snapshot
if (Get-Service -Name "SelectiveVpnCallout" -ErrorAction SilentlyContinue) {
    Start-SvrWindowsServiceForPublish -ServiceName "SelectiveVpnCallout"
}
if (Test-Path -LiteralPath $serviceExe) {
    Start-SvrPublishedService -ServiceName "SelectiveVpnRouter" -ServiceExe $serviceExe
    Wait-SvrIpcReady -ServiceName "SelectiveVpnRouter" -TimeoutSeconds 20 -PollIntervalMs 250
}
$final = Get-SvrPublishRuntimeSnapshot
Write-SvrPublishRuntimeSnapshot -Snapshot $final
if ((Get-Service SelectiveVpnCallout -ErrorAction SilentlyContinue).Status -eq "Running") {
    Write-SvrUpdateLogLine "PASS recover-publish-runtime"
    exit 0
}
Write-SvrUpdateLogLine "FAIL recover: callout not Running"
exit 1