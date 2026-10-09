#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Slice 8 acceptance-only browser routing state seed or restore (not a production write API).

.DESCRIPTION
  Default: stop SelectiveVpnRouter, timestamped backup of browser-routing-state files,
  seed two acceptance rules via BrowserRoutingStateStore, start Service, verify manifest.

  -Restore: copy state files back from a prior backup directory and restart the Service.

  Requires elevation. Do not run while the Service holds the state file.
#>
param(
    [switch]$Restore,
    [Parameter(Mandatory = $false)]
    [string]$RestoreFrom = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "_update-helpers.ps1")

$ServiceName = "SelectiveVpnRouter"
$ServiceTimeout = New-TimeSpan -Seconds 45
$DataDir = Join-Path $env:ProgramData "SelectiveVpnRouter"
$StateFile = Join-Path $DataDir "browser-routing-state.json"
$BakFile = Join-Path $DataDir "browser-routing-state.bak.json"
$BackupRoot = Join-Path $DataDir "acceptance-backups"
$SeedProject = Join-Path (Get-SvrRepoRoot) "tools\BrowserRoutingAcceptanceSeed\BrowserRoutingAcceptanceSeed.csproj"
$ExtRoot = (Resolve-Path (Join-Path (Get-SvrRepoRoot) "..\ext-vpn-route")).Path
$CheckLive = Join-Path $ExtRoot "scripts\check-live-service.js"
$ExpectedRevisionForSeed = 0

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("Running", "Stopped")][string]$Status
    )
    $svc = Get-Service -Name $ServiceName -ErrorAction Stop
    $svc.WaitForStatus($Status, $ServiceTimeout)
}

function Copy-StateBackup {
    param([Parameter(Mandatory = $true)][string]$DestinationDir)
    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    $copied = @()
    foreach ($path in @($StateFile, $BakFile)) {
        if (Test-Path -LiteralPath $path) {
            $name = Split-Path $path -Leaf
            Copy-Item -LiteralPath $path -Destination (Join-Path $DestinationDir $name) -Force
            $copied += $name
        }
    }
    if ($copied.Count -eq 0) {
        throw "No browser routing state files to back up under $DataDir"
    }
    return $copied
}

function Invoke-SeedTool {
    param([Parameter(Mandatory = $true)][string[]]$ToolArgs)
    Push-Location (Get-SvrRepoRoot)
    try {
        & dotnet run --project $SeedProject -c Release --no-restore -- @ToolArgs
        if ($LASTEXITCODE -ne 0) {
            throw "BrowserRoutingAcceptanceSeed failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

function Restore-FromBackupDir {
    param([Parameter(Mandatory = $true)][string]$Dir)
    Invoke-SeedTool -ToolArgs @("restore-files", "--from", $Dir)
}

function Assert-LiveManifest {
    if (-not (Test-Path -LiteralPath $CheckLive)) {
        throw "check-live-service.js not found: $CheckLive"
    }
    $json = & node $CheckLive 2>&1 | Out-String
    $summary = $json | ConvertFrom-Json
    if (-not $summary.ok) {
        throw "Live check failed: $($json.Trim())"
    }
    if ($summary.identity.revision -ne 1) {
        throw "Expected manifest revision 1, got $($summary.identity.revision)"
    }
    if ($summary.ruleCount -ne 2) {
        throw "Expected ruleCount 2, got $($summary.ruleCount)"
    }
    if ($summary.defaultRoute -ne "Direct") {
        throw "Expected defaultRoute Direct, got $($summary.defaultRoute)"
    }
    if ($summary.browserProxy -ne "READY") {
        throw "Expected browserProxy READY, got $($summary.browserProxy)"
    }
    Write-Host "VERIFY ok revision=$($summary.identity.revision) ruleCount=$($summary.ruleCount) browserProxy=$($summary.browserProxy) generation=$($summary.identity.stateGeneration)"
}

if ($Restore) {
    if ([string]::IsNullOrWhiteSpace($RestoreFrom)) {
        throw "-Restore requires -RestoreFrom <acceptance-backups\timestamp directory>"
    }
    $from = (Resolve-Path -LiteralPath $RestoreFrom).Path
    Write-Host "== restore browser routing state from $from"
    $wasRunning = (Get-Service -Name $ServiceName).Status -eq "Running"
    if ($wasRunning) {
        Write-Host "Stopping $ServiceName..."
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Wait-ServiceStatus -Status Stopped
    }
    try {
        Restore-FromBackupDir -Dir $from
    }
    catch {
        throw
    }
    finally {
        Write-Host "Starting $ServiceName..."
        Start-Service -Name $ServiceName -ErrorAction Stop
        Wait-ServiceStatus -Status Running
        Wait-SvrIpcReady -ServiceName $ServiceName -TimeoutSeconds 30
    }
    Write-Host "DONE restore"
    exit 0
}

$backupDir = Join-Path $BackupRoot (Get-Date -Format "yyyyMMdd-HHmmss")
$serviceWasRunning = $false
$backedUp = $false

Write-Host "== Slice 8 acceptance seed (expected revision $ExpectedRevisionForSeed)"
try {
    & dotnet restore $SeedProject | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for acceptance seed tool" }

    $svc = Get-Service -Name $ServiceName -ErrorAction Stop
    $serviceWasRunning = $svc.Status -eq "Running"
    if ($serviceWasRunning) {
        Write-Host "Stopping $ServiceName..."
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Wait-ServiceStatus -Status Stopped
    }

    Write-Host "Backing up state to $backupDir"
    $files = Copy-StateBackup -DestinationDir $backupDir
    Write-Host ("  copied: " + ($files -join ", "))
    $backedUp = $true

    Write-Host "Seeding acceptance rules..."
    Invoke-SeedTool -ToolArgs @("seed", "--expected-revision", "$ExpectedRevisionForSeed")

    Write-Host "Starting $ServiceName..."
    Start-Service -Name $ServiceName -ErrorAction Stop
    Wait-ServiceStatus -Status Running
    Wait-SvrIpcReady -ServiceName $ServiceName -TimeoutSeconds 30

    Assert-LiveManifest
    Write-Host "DONE seed; backup=$backupDir"
    Write-Host "Restore later: powershell -ExecutionPolicy Bypass -File scripts\acceptance-seed-browser-routing-state.ps1 -Restore -RestoreFrom `"$backupDir`""
    exit 0
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)"
    if ($backedUp) {
        Write-Host "Attempting rollback from $backupDir ..."
        try {
            if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
                Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
                Wait-ServiceStatus -Status Stopped
            }
            Restore-FromBackupDir -Dir $backupDir
            Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
            Wait-ServiceStatus -Status Running
            Write-Host "Rollback: state files restored; Service Running."
        }
        catch {
            Write-Host "Rollback failed: $($_.Exception.Message). Manual restore from $backupDir may be required."
        }
    }
    exit 1
}
