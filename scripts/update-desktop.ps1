#Requires -RunAsAdministrator
param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Get-SvrRepoRoot
$quiet = -not $VerboseOutput
Initialize-SvrUpdateSession -Quiet:$quiet -RepoRoot $root

$solution = Join-Path $root "SelectiveVpnRouter.sln"
$liveDir = Get-SvrPublishDirectory -Root $root
$stagingDir = Get-SvrStagingDirectory -Root $root
$prevDir = Get-SvrPublishPrevDirectory -Root $root
$stagingParent = Split-Path $stagingDir -Parent
$prevParent = Split-Path $prevDir -Parent
$appExe = Join-Path $liveDir "SelectiveVpnRouter.App.exe"
$serviceExe = Join-Path $liveDir "SelectiveVpnRouter.Service.exe"
$probeExe = Join-Path $liveDir "SelectiveVpnRouter.Probe.exe"
$serviceName = "SelectiveVpnRouter"
$serviceTimeout = New-TimeSpan -Seconds 30
$publishScript = Join-Path $PSScriptRoot "publish-desktop.ps1"
$script:deployBackedUp = $false
$script:deployLiveUpdated = $false
$script:deployRetiredPath = $null
$script:deployRuntimeSnapshot = $null

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("Running", "Stopped")][string]$Status
    )
    $svc = Get-Service -Name $Name -ErrorAction Stop
    $svc.WaitForStatus($Status, $serviceTimeout)
}

function Invoke-SvrDeploySwap {
    Write-SvrUpdateLogLine "=== STEP deploy ==="
    $global:SvrCurrentStage = "deploy"
    if (-not $global:SvrStageOutputs.ContainsKey("deploy")) {
        $global:SvrStageOutputs["deploy"] = ""
    }

    try {
        $script:deployRuntimeSnapshot = Get-SvrPublishRuntimeSnapshot
        Write-SvrPublishRuntimeSnapshot -Snapshot $script:deployRuntimeSnapshot

        Stop-SvrPublishRuntimeForTreeSwap -PublishDir $liveDir
        Wait-SvrPublishDirectoryUnlocked -PublishDir $liveDir -TimeoutSeconds 20
        Remove-SvrRetiredPublishDirectories -ParentDir (Split-Path $liveDir -Parent)
        Write-SvrUpdateLogLine "publish-lock-check: live publish directory unlocked"

        if (-not (Test-Path -LiteralPath $liveDir)) {
            if (Test-Path -LiteralPath (Join-Path $prevDir "SelectiveVpnRouter.Service.exe")) {
                Write-SvrUpdateLogLine "live publish missing; restoring from previous backup"
                Invoke-SvrRestorePublishFromBackup -BackupDir $prevDir -LiveDir $liveDir
            }
            else {
                throw "live publish directory not found: $liveDir"
            }
        }
        elseif (-not (Test-Path -LiteralPath $serviceExe)) {
            if (Test-Path -LiteralPath (Join-Path $prevDir "SelectiveVpnRouter.Service.exe")) {
                Write-SvrUpdateLogLine "live publish incomplete; restoring from previous backup"
                Invoke-SvrRestorePublishFromBackup -BackupDir $prevDir -LiveDir $liveDir
            }
            else {
                throw "live publish incomplete (missing SelectiveVpnRouter.Service.exe) and no backup available"
            }
        }

        Write-SvrUpdateLogLine "Removing previous backup publish directory"
        Remove-DirectoryWithRetry -Path $prevDir -PublishDir $liveDir

        Write-SvrUpdateLogLine "Backing up live publish directory to previous"
        Invoke-SvrRobocopyMirror -Source $liveDir -Destination $prevDir
        $script:deployBackedUp = $true

        Write-SvrUpdateLogLine "Promoting staging publish directory to live"
        $script:deployRetiredPath = Invoke-SvrPromotePublishDirectory -Source $stagingDir -Destination $liveDir -RecreateSource
        $script:deployLiveUpdated = $true

        Repair-SvrPublishRuntimeImagePaths -PublishDir $liveDir -Snapshot $script:deployRuntimeSnapshot
        Restore-SvrPublishRuntimeFromSnapshot -Snapshot $script:deployRuntimeSnapshot -PublishDir $liveDir
        Remove-SvrRetiredPublishDirectories -ParentDir (Split-Path $liveDir -Parent)
    }
    catch {
        Add-SvrStageOutput $_.Exception.Message
        throw
    }
    finally {
        $global:SvrCurrentStage = $null
    }
}

function Invoke-SvrDeployRollback {
    Write-SvrUpdateLogLine "Attempting rollback to previous publish"
    try {
        $snapshot = if ($script:deployRuntimeSnapshot) { $script:deployRuntimeSnapshot } else { Get-SvrPublishRuntimeSnapshot }

        Stop-SvrPublishRuntimeForTreeSwap -PublishDir $liveDir

        if ((Test-Path -LiteralPath $prevDir) -and ($script:deployBackedUp -or $script:deployLiveUpdated)) {
            Write-SvrUpdateLogLine "Restoring previous publish from backup"
            Invoke-SvrRestorePublishFromBackup -BackupDir $prevDir -LiveDir $liveDir
        }

        if ((Test-Path -LiteralPath $liveDir) -and (Test-Path -LiteralPath (Join-Path $liveDir "SelectiveVpnRouter.Service.exe"))) {
            Restore-SvrPublishDriverImagePathFromSnapshot -Snapshot $snapshot
            Repair-SvrPublishRuntimeImagePaths -PublishDir $liveDir -Snapshot $snapshot
            Restore-SvrPublishRuntimeFromSnapshot -Snapshot $snapshot -PublishDir $liveDir
            Add-SvrStepResult -Name "rollback" -Outcome PASS -Label "rollback"
            Write-SvrUpdateLogLine "Rollback completed"
            return $true
        }
    }
    catch {
        Add-SvrStageOutput $_.Exception.Message
        Add-SvrStepResult -Name "rollback" -Outcome FAIL -Label "rollback"
        Write-SvrUpdateLogLine "Rollback failed: $($_.Exception.Message)"
    }
    return $false
}

try {
    Invoke-SvrStep -Name "build" -PassLabel "build" -Action {
        Invoke-SvrDotNet -ArgumentList @("restore", $solution) | Out-Null
        Invoke-SvrDotNet -ArgumentList @("build", $solution, "-c", "Release", "--no-restore") | Out-Null
    }

    $testResult = Invoke-SvrStep -Name "tests" -PassLabel "tests" -Action {
        Invoke-SvrDotNet -ArgumentList @("test", $solution, "-c", "Release", "--no-build")
    }
    $testPassedCount = Get-SvrDotNetTestPassedCount -Output $testResult.Output
    if ($testPassedCount) {
        $global:SvrStepResults[-1].Detail = "$testPassedCount"
    }

    if (Test-Path -LiteralPath $stagingDir) {
        Write-SvrUpdateLogLine "Clearing existing staging directory"
        Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction Stop
    }
    New-Item -ItemType Directory -Force -Path $stagingParent | Out-Null

    Invoke-SvrStep -Name "publish" -PassLabel "publish" -Action {
        & $publishScript -Quiet -OutputDirectory $stagingDir
        if ($LASTEXITCODE -ne 0) { throw "publish-desktop.ps1 failed with exit code $LASTEXITCODE" }
        Test-SvrStagingPublish -StagingDir $stagingDir
    }

    $deploySw = [Diagnostics.Stopwatch]::StartNew()
    try {
        Invoke-SvrDeploySwap
        $deploySw.Stop()
        Write-SvrUpdateLogLine ("deploy completed in {0:N1}s" -f $deploySw.Elapsed.TotalSeconds)
    }
    catch {
        $deploySw.Stop()
        Add-SvrStageOutput $_.Exception.Message
        Add-SvrStepResult -Name "deploy" -Outcome FAIL -Label "deploy" -Duration $deploySw.Elapsed
        if ($script:deployLiveUpdated -or $script:deployBackedUp) {
            Invoke-SvrDeployRollback | Out-Null
        }
        Write-SvrCompactConsole -FailedStep "deploy" -FailedMessage $_.Exception.Message
        exit 1
    }

    if ($script:deployRuntimeSnapshot -and $script:deployRuntimeSnapshot.ProductWasRunning) {
        Write-SvrUpdateLogLine "=== STEP service-ready ==="
        $global:SvrCurrentStage = "service-ready"
        if (-not $global:SvrStageOutputs.ContainsKey("service-ready")) {
            $global:SvrStageOutputs["service-ready"] = ""
        }
        try {
            Wait-SvrIpcReady -ServiceName $serviceName -TimeoutSeconds 15 -PollIntervalMs 250
        }
        catch {
            Add-SvrStageOutput $_.Exception.Message
            Add-SvrStepResult -Name "service-ready" -Outcome FAIL -Label "service-ready"
            Write-SvrCompactConsole -FailedStep "service-ready" -FailedMessage $_.Exception.Message
            exit 1
        }
        finally {
            $global:SvrCurrentStage = $null
        }

        $svc = Get-Service -Name $serviceName -ErrorAction Stop
        $serviceDetail = if ($svc.Status -eq "Running") { "Running" } else { $svc.Status.ToString() }
        Add-SvrStepResult -Name "service" -Outcome PASS -Label "service" -Detail $serviceDetail
    }
    else {
        Write-SvrUpdateLogLine "service-ready: skipped (product service was not running before deploy)"
        Add-SvrStepResult -Name "service-ready" -Outcome SKIP -Label "service-ready" -Detail "preserved-stopped"
        Add-SvrStepResult -Name "service" -Outcome SKIP -Label "service" -Detail "Stopped"
    }

    $iconIco = Join-Path $root "assets\branding\vpn-route-icon.ico"
    if (-not (Test-Path -LiteralPath $iconIco)) {
        $iconIco = Join-Path $root "src\SelectiveVpnRouter.App\vpn-route-icon.ico"
    }
    if (Test-Path -LiteralPath $iconIco) {
        Copy-Item -LiteralPath $iconIco -Destination (Join-Path $liveDir "vpn-route-icon.ico") -Force
    }

    Update-VpnRouteDesktopShortcuts -AppExe $appExe -ProductName "VPN Route" -IconPath $iconIco

    Invoke-SvrStep -Name "smoke" -PassLabel "smoke" -Action {
        Test-SvrPublishRuntimeLayout -PublishDir $liveDir

        Invoke-SvrCaptureScript -FilePath (Join-Path $PSScriptRoot "smoke-network-catalog.ps1") `
            -ArgumentList @("-ProbePath", $probeExe, "-Quiet") | Out-Null

        Invoke-SvrCaptureScript -FilePath (Join-Path $PSScriptRoot "smoke-app-publish.ps1") `
            -ArgumentList @("-PublishDir", $liveDir, "-Quiet") | Out-Null

        Write-SvrUpdateLogLine "=== STEP routing-fast ==="
        Invoke-SvrCaptureScript -FilePath (Join-Path $PSScriptRoot "test-routing-fast.ps1") `
            -ArgumentList @("-Quiet") -AllowedExitCodes @(0, 2) | Out-Null
    }

    Write-SvrUpdateSummary -Success
    exit 0
}
catch {
    if ($global:SvrStepResults.Count -eq 0 -or $global:SvrStepResults[-1].Outcome -ne "FAIL") {
        Write-SvrCompactConsole -FailedStep "update" -FailedMessage $_.Exception.Message
    }
    exit 1
}