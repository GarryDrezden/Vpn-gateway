$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_update-helpers.ps1")

function Assert-True { param([bool]$C,[string]$M) if (-not $C) { throw $M } }
function Assert-Equal { param($E,$A,[string]$M) if ($E -ne $A) { throw "$M expected=$E actual=$A" } }

# F: no EndInvoke() without IAsyncResult in helpers
$helpersText = Get-Content -LiteralPath (Join-Path $PSScriptRoot "_update-helpers.ps1") -Raw
if ($helpersText -match '\.EndInvoke\(\s*\)') { throw 'Found EndInvoke() without IAsyncResult in _update-helpers.ps1' }

# A/B: immediate completion returns output via EndInvoke(asyncResult)
$ps = [powershell]::Create()
try {
    [void]$ps.AddScript({ return 42 })
    $async = $ps.BeginInvoke()
    $deadline = [datetime]::UtcNow.AddSeconds(5)
    $out = Wait-SvrPowerShellAsyncResult -PowerShell $ps -AsyncResult $async -DeadlineUtc $deadline
    $items = Get-SvrPowerShellPipelineOutputItems -Output $out
    Assert-Equal 1 $items.Count 'EndInvoke PSDataCollection item count'
    Assert-Equal 42 $items[0] 'immediate output'
}
finally { $ps.Dispose() }

# C: async throws -> original error surfaces
$ps2 = [powershell]::Create()
try {
    [void]$ps2.AddScript({ throw 'boom-async' })
    $async2 = $ps2.BeginInvoke()
    $deadline2 = [datetime]::UtcNow.AddSeconds(5)
    $failed = $false
    try {
        $null = Wait-SvrPowerShellAsyncResult -PowerShell $ps2 -AsyncResult $async2 -DeadlineUtc $deadline2
    }
    catch {
        $failed = $true
        Assert-True ($_.Exception.Message -match 'boom-async') 'async throw message'
    }
    Assert-True $failed 'async throw expected'
}
finally { $ps2.Dispose() }

# D: hang -> bounded timeout (test must finish quickly)
$ps3 = [powershell]::Create()
try {
    [void]$ps3.AddScript({ Start-Sleep -Seconds 30; return 1 })
    $async3 = $ps3.BeginInvoke()
    $deadline3 = [datetime]::UtcNow.AddMilliseconds(800)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    try {
        $null = Wait-SvrPowerShellAsyncResult -PowerShell $ps3 -AsyncResult $async3 -DeadlineUtc $deadline3 -PollIntervalMs 50
    }
    catch {
        $timedOut = $true
        Assert-True ($_.Exception.Message -match 'deadline exceeded') 'timeout message'
    }
    $sw.Stop()
    Assert-True $timedOut 'timeout expected'
    Assert-True ($sw.Elapsed.TotalSeconds -lt 8) 'timeout bounded'
}
finally { $ps3.Dispose() }

# E: poll abort (simulated PID change)
$ps4 = [powershell]::Create()
try {
    [void]$ps4.AddScript({ Start-Sleep -Seconds 10; return 99 })
    $async4 = $ps4.BeginInvoke()
    $deadline4 = [datetime]::UtcNow.AddSeconds(5)
    $poll = { return [pscustomobject]@{ Abort = $true; Message = 'service restarted during IPC (baselinePid=1 currentPid=2)' } }
    $aborted = $false
    try {
        $null = Wait-SvrPowerShellAsyncResult -PowerShell $ps4 -AsyncResult $async4 -DeadlineUtc $deadline4 -PollAbort $poll -PollIntervalMs 50
    }
    catch {
        $aborted = $true
        Assert-True ($_.Exception.Message -match 'service restarted') 'pid abort message'
    }
    Assert-True $aborted 'pid abort expected'
}
finally { $ps4.Dispose() }

Write-Host 'PASS test-svr-powershell-async-watch'
exit 0