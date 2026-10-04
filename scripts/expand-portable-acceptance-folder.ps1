#Requires -Version 5.1
#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$TargetFolder
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$ZipPath = (Resolve-Path -LiteralPath $ZipPath).Path
$TargetFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TargetFolder)

$snapshot = Get-SvrPublishRuntimeSnapshot
Write-Host "Pre-cleanup snapshot:"
Write-Host ("  SelectiveVpnRouter: installed=$($snapshot.ProductInstalled) wasRunning=$($snapshot.ProductWasRunning)")
Write-Host ("  SelectiveVpnCallout: installed=$($snapshot.DriverInstalled) wasRunning=$($snapshot.DriverWasRunning)")

Stop-SvrPublishRuntimeForTreeSwap -PublishDir $TargetFolder
Start-Sleep -Seconds 1

if (Test-Path -LiteralPath $TargetFolder) {
    Remove-Item -LiteralPath $TargetFolder -Recurse -Force -ErrorAction Stop
}

if (Test-Path -LiteralPath $TargetFolder) {
    throw "Clean failed: target folder still exists: $TargetFolder"
}

New-Item -ItemType Directory -Path $TargetFolder -Force | Out-Null
Expand-Archive -LiteralPath $ZipPath -DestinationPath $TargetFolder -Force

Write-Host "Expanded $ZipPath -> $TargetFolder"

Repair-SvrPublishRuntimeImagePaths -PublishDir $TargetFolder
Restore-SvrPublishRuntimeFromSnapshot -Snapshot $snapshot -PublishDir $TargetFolder

$restoredProduct = $snapshot.ProductInstalled -and $snapshot.ProductWasRunning
if ($restoredProduct) {
    $ipcReady = Wait-SvrIpcGetStatusReady -TimeoutSeconds 15 -PollIntervalMs 250 -StatusTimeoutMs 3000
    if (-not $ipcReady.Ready) {
        $detail = if ($ipcReady.LastError) { $ipcReady.LastError } else { "GetStatus not ready" }
        throw (
            "IPC readiness failed after restarting SelectiveVpnRouter " +
            "(attempts=$($ipcReady.Attempts), nonRetryable=$($ipcReady.NonRetryable)): $detail"
        )
    }

    $serviceAlive = $null
    $payloadJson = $ipcReady.StatusResponse.PayloadJson
    if (-not [string]::IsNullOrWhiteSpace($payloadJson)) {
        try {
            $payload = $payloadJson | ConvertFrom-Json
            if ($null -ne $payload -and ($payload.PSObject.Properties.Name -contains "serviceAlive")) {
                $serviceAlive = [bool]$payload.serviceAlive
            }
        }
        catch {
        }
    }

    Write-Host (
        "IPC ready after service restart (attempts=$($ipcReady.Attempts), " +
        "serviceAlive=$serviceAlive)"
    )
}

$bootstrapExe = Join-Path $TargetFolder "SelectiveVpnRouter.Bootstrap.exe"
if (-not (Test-Path -LiteralPath $bootstrapExe)) {
    throw "Bootstrap executable missing after extraction: $bootstrapExe"
}

$statusJson = & $bootstrapExe status --root $TargetFolder
Write-Host $statusJson

$status = $statusJson | ConvertFrom-Json

if ($snapshot.ProductInstalled -and $snapshot.ProductWasRunning) {
    if (-not $status.serviceRunning) {
        throw "Bootstrap status: serviceRunning=false after restore (expected true)."
    }
}

$expectReady = $snapshot.ProductInstalled -and $snapshot.ProductWasRunning -and $snapshot.DriverInstalled
if ($expectReady) {
    if ($status.bootstrapState -ne "Ready") {
        throw (
            "Bootstrap status is not Ready after same-path extraction and runtime restore. " +
            "bootstrapState=$($status.bootstrapState) message=$($status.message)"
        )
    }

    Write-Host "ACCEPTANCE PREP: PASS (bootstrapState=Ready)"
}
else {
    Write-Host (
        "Extraction complete. Bootstrap state=$($status.bootstrapState) " +
        "(strict Ready check skipped: requires product installed+wasRunning and driver installed before cleanup)."
    )
}
