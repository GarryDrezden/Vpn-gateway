#Requires -RunAsAdministrator
param(
    [switch]$SkipLoopbackDiagnostic,
    [switch]$SkipBuild,
    [switch]$Rebuild,
    [switch]$QuietLog
)

$ErrorActionPreference = "Stop"

function Write-DeployConsole {
    param([Parameter(Mandatory = $true)][string]$Line)
    Write-Host $Line
    if (Get-Command Write-SvrUpdateLogLine -ErrorAction SilentlyContinue) {
        Write-SvrUpdateLogLine $Line
    }
}

function Write-DeployFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Message
    )
    Write-DeployConsole "FAIL deploy-callout-driver stage=$Stage"
    Write-DeployConsole $Message
    if ($global:SvrUpdateLogPath) { Write-DeployConsole ("Log: " + $global:SvrUpdateLogPath) }
    exit 1
}

function Get-SvrSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-SvrRegisteredDriverPath {
    $raw = (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\SelectiveVpnCallout" -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
    if (-not $raw) { return $null }
    $trimmed = $raw.Trim().Trim('"')
    if ($trimmed.StartsWith('\??\')) { $trimmed = $trimmed.Substring(4) }
    try { return [IO.Path]::GetFullPath($trimmed) } catch { return $trimmed }
}

function Invoke-DeployCalloutRollback {
    param($Snapshot, [string]$LiveSys, [string]$RollbackSys, [string]$PublishDir, [string]$ServiceExe)
    Write-DeployConsole "ROLLBACK: restoring previous callout runtime..."
    try {
        Stop-SvrPublishRuntimeForTreeSwap -PublishDir $PublishDir
    } catch {}
    if ((Test-Path -LiteralPath $RollbackSys) -and (Test-Path -LiteralPath $LiveSys)) {
        Copy-Item -LiteralPath $RollbackSys -Destination $LiveSys -Force
        Write-DeployConsole "ROLLBACK: restored live sys from rollback artifact"
    }
    if ($Snapshot) {
        Restore-SvrPublishDriverImagePathFromSnapshot -Snapshot $Snapshot
        Restore-SvrPublishRuntimeFromSnapshot -Snapshot $Snapshot -PublishDir $PublishDir
    }
    if ($Snapshot.ProductInstalled -and $Snapshot.ProductWasRunning -and (Test-Path -LiteralPath $ServiceExe)) {
        try {
            Start-SvrPublishedService -ServiceName "SelectiveVpnRouter" -ServiceExe $ServiceExe
            Wait-SvrIpcReady -ServiceName "SelectiveVpnRouter" -TimeoutSeconds 20 -PollIntervalMs 250
            Write-DeployConsole "ROLLBACK: SelectiveVpnRouter Running IPC Ready"
        } catch {
            Write-DeployConsole "ROLLBACK WARN: product service: $($_.Exception.Message)"
        }
    }
    Write-DeployConsole "FAIL deploy-callout-driver (PASS rollback)"
    exit 1
}

try {
    . (Join-Path $PSScriptRoot "_common.ps1")
    $repoRoot = Get-SvrRepoRoot
    Initialize-SvrUpdateSession -Quiet:(-not $QuietLog) -RepoRoot $repoRoot
    $logDir = Join-Path $repoRoot "artifacts\logs"
    $global:SvrUpdateLogPath = Join-Path $logDir ("deploy-callout-driver-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

    $liveDir = Get-SvrPublishDirectory -Root $repoRoot
    $serviceExe = Join-Path $liveDir "SelectiveVpnRouter.Service.exe"
    $stagingSys = Get-SvrDriverStagingSysPath -Root $repoRoot -Configuration Release
    $liveSys = Get-SvrDriverRuntimeSysPath -Root $repoRoot
    $rollbackDir = Join-Path $repoRoot "artifacts\driver\rollback"
    $rollbackSys = Join-Path $rollbackDir "SelectiveVpnCallout.sys"
    $buildScript = Join-Path $PSScriptRoot "build-driver.ps1"
    $knownPreLoopbackHash = "48594AD445158F73E68E24E470803A3D418156A47A94F2D8BFBA57AC65E0EBDE"

    $mode = if ($SkipBuild) { "SkipBuild" } elseif ($Rebuild) { "Rebuild" } else { "Build" }
    Write-DeployConsole "=== VPN Route callout deploy ==="
    Write-DeployConsole "Mode: $mode"
    Write-DeployConsole "Staging build path: $stagingSys"
    Write-DeployConsole "Canonical runtime path: $liveSys"

    $snapshot = Get-SvrPublishRuntimeSnapshot
    Write-SvrPublishRuntimeSnapshot -Snapshot $snapshot

    $registeredPath = Get-SvrRegisteredDriverPath
    $liveHashBefore = if (Test-Path -LiteralPath $liveSys) { Get-SvrSha256 $liveSys } else { $null }
    Write-DeployConsole "Registered ImagePath: $(if ($registeredPath) { $registeredPath } else { '(none)' })"
    Write-DeployConsole "Live runtime hash (before): $(if ($liveHashBefore) { $liveHashBefore } else { '(missing)' })"

    if (-not $SkipBuild) {
        Write-DeployConsole "=== build-driver (staging, services may stay running) ==="
        if ($Rebuild) {
            $proj = Join-Path $repoRoot "driver\SelectiveVpnCallout\SelectiveVpnCallout.vcxproj"
            $msbuild = Get-SvrMsbuild
            if (-not $msbuild) { Write-DeployFailure -Stage "build" -Message "MSBuild.exe not found." }
            $outDir = Split-Path -Parent $stagingSys
            $intDir = Join-Path $repoRoot "artifacts\driver\obj\staging\Release"
            New-Item -ItemType Directory -Force -Path $outDir, $intDir | Out-Null
            & $msbuild $proj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:OutDir="$outDir\" /p:IntDir="$intDir\" /m
            if ($LASTEXITCODE -ne 0) { Write-DeployFailure -Stage "build" -Message "MSBuild Rebuild exited $LASTEXITCODE (runtime untouched)." }
        }
        else {
            & $buildScript -Configuration Release
            if ($LASTEXITCODE -ne 0) { Write-DeployFailure -Stage "build" -Message "build-driver.ps1 exited $LASTEXITCODE (runtime untouched)." }
        }
    }
    else {
        Write-DeployConsole "=== build-driver === SKIP"
    }

    if (-not (Test-Path -LiteralPath $stagingSys)) {
        Write-DeployFailure -Stage "staging" -Message "Staged driver missing: $stagingSys"
    }

    $stagedHash = Get-SvrSha256 $stagingSys
    Write-DeployConsole "Staged hash: $stagedHash"
    if ($stagedHash -eq $knownPreLoopbackHash) {
        Write-DeployFailure -Stage "hash" -Message "Staged build is pre-loopback hash."
    }

    $callout = Get-Service -Name "SelectiveVpnCallout" -ErrorAction SilentlyContinue
    $product = Get-Service -Name "SelectiveVpnRouter" -ErrorAction SilentlyContinue
    $calloutRunning = ($callout -and $callout.Status -eq "Running")
    $productRunning = ($product -and $product.Status -eq "Running")

    $registeredOnLive = $false
    if ($registeredPath -and (Test-Path -LiteralPath $liveSys)) {
        $registeredOnLive = ([IO.Path]::GetFullPath($registeredPath)).Equals([IO.Path]::GetFullPath($liveSys), [StringComparison]::OrdinalIgnoreCase)
    }

    if ($SkipBuild -and $calloutRunning -and $productRunning -and $registeredOnLive -and $liveHashBefore -and ($liveHashBefore -eq $stagedHash)) {
        Write-DeployConsole "SelectiveVpnCallout: Running"
        Write-DeployConsole "SelectiveVpnRouter: Running"
        try {
            Wait-SvrIpcReady -ServiceName "SelectiveVpnRouter" -TimeoutSeconds 10 -PollIntervalMs 250
            Write-DeployConsole "IPC: Ready"
        } catch { Write-DeployConsole "IPC: Not ready ($($_.Exception.Message))" }
        Write-DeployConsole "Diagnostic: SKIP (already deployed)"
        Write-DeployConsole "PASS already deployed"
        exit 0
    }

    Write-DeployConsole "Stopping product/callout for deploy swap..."
    Stop-SvrPublishRuntimeForTreeSwap -PublishDir $liveDir

    New-Item -ItemType Directory -Force -Path $rollbackDir | Out-Null
    if (Test-Path -LiteralPath $liveSys) {
        Copy-Item -LiteralPath $liveSys -Destination $rollbackSys -Force
        Write-DeployConsole "Rollback artifact: $rollbackSys"
    }

    $driverDir = Split-Path $liveSys -Parent
    New-Item -ItemType Directory -Force -Path $driverDir | Out-Null
    Copy-Item -LiteralPath $stagingSys -Destination $liveSys -Force
    $infSrc = Join-Path $repoRoot "driver\SelectiveVpnCallout\SelectiveVpnCallout.inf"
    if (Test-Path -LiteralPath $infSrc) {
        Copy-Item -LiteralPath $infSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.inf") -Force
    }
    Write-DeployConsole "Deployed staged driver -> runtime path"

    $liveHash = Get-SvrSha256 $liveSys
    Write-DeployConsole "Live runtime hash (after copy): $liveHash"

    Write-DeployConsole "Registering callout at canonical runtime path..."
    try {
        Set-SvrKernelDriverBinPath -SysPath $liveSys
        Write-DeployConsole "Starting SelectiveVpnCallout..."
        Start-SvrWindowsServiceForPublish -ServiceName "SelectiveVpnCallout"
        $callout = Get-Service -Name "SelectiveVpnCallout" -ErrorAction Stop
        if ($callout.Status -ne "Running") {
            throw "SelectiveVpnCallout state=$($callout.Status)"
        }
        Write-DeployConsole "SelectiveVpnCallout: Running"
        if ($product -or (Test-Path -LiteralPath $serviceExe)) {
            Write-DeployConsole "Starting SelectiveVpnRouter..."
            Start-SvrPublishedService -ServiceName "SelectiveVpnRouter" -ServiceExe $serviceExe
            Wait-SvrIpcReady -ServiceName "SelectiveVpnRouter" -TimeoutSeconds 20 -PollIntervalMs 250
            Write-DeployConsole "SelectiveVpnRouter: Running"
            Write-DeployConsole "IPC: Ready"
        }
    }
    catch {
        Invoke-DeployCalloutRollback -Snapshot $snapshot -LiveSys $liveSys -RollbackSys $rollbackSys -PublishDir $liveDir -ServiceExe $serviceExe
    }

    $registeredPath = Get-SvrRegisteredDriverPath
    $registeredHash = if ($registeredPath -and (Test-Path -LiteralPath $registeredPath)) { Get-SvrSha256 $registeredPath } else { $null }
    Write-DeployConsole "Registered ImagePath: $registeredPath"
    Write-DeployConsole "Registered hash: $registeredHash"

    if (-not $SkipLoopbackDiagnostic) {
        $status = Invoke-SvrIpcGetStatusWithRetry -MaxAttempts 10 -PollIntervalMs 500 -TimeoutMs 15000
        $snap = $status.PayloadJson | ConvertFrom-Json
        if (-not $snap.VpnRoutingReady) {
            Write-DeployConsole "Diagnostic: SKIP (VPN not connected)"
        }
        else {
            $payload = (@{ name = "loopback-local-callback" } | ConvertTo-Json -Compress)
            $resp = Invoke-SvrIpc -Method "RunDiagnostic" -PayloadJson $payload -TimeoutMs 180000
            if (-not $resp.Ok) { Write-DeployFailure -Stage "diagnostic-ipc" -Message $resp.Error }
            $result = $resp.PayloadJson | ConvertFrom-Json
            Write-DeployConsole "Diagnostic: $($result.Outcome) $($result.Message)"
            if ($result.Outcome -eq "FAIL") { Write-DeployFailure -Stage "diagnostic" -Message $result.Message }
        }
    }

    Write-DeployConsole "PASS deploy-callout-driver"
    exit 0
}
catch {
    Write-DeployFailure -Stage "unexpected" -Message $_.Exception.Message
}