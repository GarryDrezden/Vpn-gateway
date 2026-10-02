$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_update-helpers.ps1")

function Assert-True { param([bool]$C, [string]$M) if (-not $C) { throw $M } }
function Assert-Equal { param($E, $A, [string]$M) if ($E -ne $A) { throw "$M expected=$E actual=$A" } }
function Assert-Match { param([string]$H, [string]$P, [string]$M) if ($H -notmatch $P) { throw "$M haystack=$H pattern=$P" } }

function Invoke-WithMockServicePid {
    param(
        [scriptblock]$Mock,
        [scriptblock]$Test
    )
    $prev = $script:SvrServiceProcessIdOverride
    $script:SvrServiceProcessIdOverride = $Mock
    try {
        & $Test
    }
    finally {
        $script:SvrServiceProcessIdOverride = $prev
    }
}

# A: baseline=100, current=100 => no restart
Invoke-WithMockServicePid -Mock { return 100 } -Test {
    $r = Test-SvrIpcServiceWatchPoll -BaselinePid 100
    Assert-Equal $false $r.Abort 'A abort'
}

# B: baseline=100, current=200 => restart
Invoke-WithMockServicePid -Mock { return 200 } -Test {
    $r = Test-SvrIpcServiceWatchPoll -BaselinePid 100
    Assert-True $r.Abort 'B abort'
    Assert-Match $r.Message 'service restarted during IPC \(baselinePid=100 currentPid=200\)' 'B message'
}

# C: baseline=100, current=0/stopped => stopped
Invoke-WithMockServicePid -Mock { return $null } -Test {
    $r = Test-SvrIpcServiceWatchPoll -BaselinePid 100
    Assert-True $r.Abort 'C abort'
    Assert-Match $r.Message 'service stopped during IPC \(baselinePid=100' 'C message'
    Assert-True ($r.Message -notmatch 'service restarted') 'C not restart'
}

# D: baseline=0 poll => NOT restart semantics (invalid baseline)
Invoke-WithMockServicePid -Mock { return 200 } -Test {
    $r = Test-SvrIpcServiceWatchPoll -BaselinePid 0
    Assert-True $r.Abort 'D abort'
    Assert-Match $r.Message 'Unable to determine service PID during diagnostic watch' 'D message'
    Assert-True ($r.Message -notmatch 'service restarted') 'D not restart'
}

# E: transient 0 then 200 => acquisition gets 200 before watch
$script:MockPidSequence = @(0, 0, 200)
$script:MockPidIndex = 0
Invoke-WithMockServicePid -Mock {
    $i = $script:MockPidIndex
    $script:MockPidIndex++
    if ($i -lt $script:MockPidSequence.Count) {
        $v = $script:MockPidSequence[$i]
        if ($null -eq $v -or [int]$v -le 0) { return $null }
        return [int]$v
    }
    return 200
} -Test {
    $script:MockPidIndex = 0
    $baseline = Resolve-SvrServicePidBaselineForWatch -TimeoutMs 2000 -PollIntervalMs 50
    Assert-Equal 200 $baseline 'E baseline after retry'
}

# F: always unknown => null baseline (infrastructure)
Invoke-WithMockServicePid -Mock { return $null } -Test {
    $baseline = Resolve-SvrServicePidBaselineForWatch -TimeoutMs 400 -PollIntervalMs 50
    Assert-Equal $null $baseline 'F no baseline'
}

# Invoke-SvrIpcWithServiceWatch rejects baseline=0 before IPC
$blocked = $false
try {
    $null = Invoke-SvrIpcWithServiceWatch -Method GetStatus -WallClockTimeoutMs 1000 -ServicePidBaseline 0 -HelpersRoot $PSScriptRoot
}
catch {
    $blocked = $true
    Assert-Match $_.Exception.Message 'Unable to determine service PID before diagnostic' 'F invoke guard'
}
Assert-True $blocked 'F invoke blocked'

Write-Host 'PASS test-svr-service-pid-watch'
exit 0
