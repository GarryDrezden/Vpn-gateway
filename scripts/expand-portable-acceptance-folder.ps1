#Requires -Version 5.1
#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$TargetFolder
)

$ErrorActionPreference = "Stop"

$ProductServiceName = "SelectiveVpnRouter"
$DriverServiceName = "SelectiveVpnCallout"

function Get-AcceptanceServiceSnapshot {
    param([Parameter(Mandatory = $true)][string]$ServiceName)

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        return [pscustomobject]@{
            Name       = $ServiceName
            Installed  = $false
            WasRunning = $false
        }
    }

    return [pscustomobject]@{
        Name       = $ServiceName
        Installed  = $true
        WasRunning = ($svc.Status -eq "Running")
    }
}

function Stop-AcceptanceWindowsService {
    param([Parameter(Mandatory = $true)][string]$ServiceName)

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $existing) { return }
    if ($existing.Status -eq "Stopped") { return }

    try {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    }
    catch {
        & sc.exe stop $ServiceName | Out-Null
    }

    try {
        $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(45))
    }
    catch {
        throw "Could not stop service '$ServiceName' within 45s."
    }
}

function Start-AcceptanceWindowsService {
    param([Parameter(Mandatory = $true)][string]$ServiceName)

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        throw "Cannot start '$ServiceName': service is not installed."
    }

    if ($svc.Status -eq "Running") {
        return
    }

    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
    }
    catch {
        & sc.exe start $ServiceName | Out-Null
    }

    try {
        $svc.WaitForStatus("Running", [TimeSpan]::FromSeconds(45))
    }
    catch {
        throw "Service '$ServiceName' did not reach Running within 45s."
    }
}

$ZipPath = (Resolve-Path -LiteralPath $ZipPath).Path
$TargetFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TargetFolder)

$snapProduct = Get-AcceptanceServiceSnapshot -ServiceName $ProductServiceName
$snapDriver = Get-AcceptanceServiceSnapshot -ServiceName $DriverServiceName

Write-Host "Pre-cleanup snapshot:"
Write-Host ("  {0}: installed={1} wasRunning={2}" -f $snapProduct.Name, $snapProduct.Installed, $snapProduct.WasRunning)
Write-Host ("  {0}: installed={1} wasRunning={2}" -f $snapDriver.Name, $snapDriver.Installed, $snapDriver.WasRunning)

$procs = @("SelectiveVpnRouter.App", "SelectiveVpnRouter.Bootstrap", "SelectiveVpnRouter.Service")
foreach ($name in $procs) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Stop-AcceptanceWindowsService -ServiceName $ProductServiceName
Stop-AcceptanceWindowsService -ServiceName $DriverServiceName

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

$restoredProduct = $false
if ($snapDriver.Installed -and $snapDriver.WasRunning) {
    Write-Host "Restoring previous running state: $DriverServiceName"
    Start-AcceptanceWindowsService -ServiceName $DriverServiceName
}

if ($snapProduct.Installed -and $snapProduct.WasRunning) {
    Write-Host "Restoring previous running state: $ProductServiceName"
    Start-AcceptanceWindowsService -ServiceName $ProductServiceName
    $restoredProduct = $true

    $helpersPath = Join-Path $PSScriptRoot "_update-helpers.ps1"
    if (-not (Test-Path -LiteralPath $helpersPath)) {
        throw "Missing IPC helpers: $helpersPath"
    }

    . $helpersPath

    $ipcReady = Wait-SvrIpcGetStatusReady -TimeoutSeconds 15 -PollIntervalMs 250 -StatusTimeoutMs 3000
    if (-not $ipcReady.Ready) {
        $detail = if ($ipcReady.LastError) { $ipcReady.LastError } else { "GetStatus not ready" }
        throw (
            "IPC readiness failed after restarting $ProductServiceName " +
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

if ($snapProduct.Installed -and $snapProduct.WasRunning) {
    if (-not $status.serviceRunning) {
        throw "Bootstrap status: serviceRunning=false after restore (expected true)."
    }
}

$expectReady = $snapProduct.Installed -and $snapProduct.WasRunning -and $snapDriver.Installed
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
