# Tests for IPC readiness retry + runner dependency / fail-fast classification.

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_update-helpers.ps1")

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw ("{0} (expected={1} actual={2})" -f $Message, $Expected, $Actual)
    }
}

function New-MockStatusResponse {
    return [pscustomobject]@{
        Ok          = $true
        PayloadJson = '{"Vpn":{"Connected":false},"DriverLoaded":true}'
        Error       = ""
    }
}

Assert-SvrRunnerIpcDependencies

# Case 1: first fail, second success
$script:attempt1 = 0
$invoke1 = {
    $script:attempt1++
    if ($script:attempt1 -lt 2) {
        return [pscustomobject]@{ Ok = $false; Response = $null; Error = "Pipe is broken"; NonRetryable = $false }
    }
    return [pscustomobject]@{ Ok = $true; Response = (New-MockStatusResponse); Error = ""; NonRetryable = $false }
}
$r1 = Test-SvrIpcGetStatusReadyCore -TimeoutSeconds 2 -PollIntervalMs 10 -InvokeAttempt $invoke1
Assert-True $r1.Ready "case1 Ready"
Assert-Equal 2 $r1.Attempts "case1 Attempts"

# Case 2: several transport fails then success
$script:attempt2 = 0
$invoke2 = {
    $script:attempt2++
    if ($script:attempt2 -lt 4) {
        return [pscustomobject]@{ Ok = $false; Response = $null; Error = "Unable to connect"; NonRetryable = $false }
    }
    return [pscustomobject]@{ Ok = $true; Response = (New-MockStatusResponse); Error = ""; NonRetryable = $false }
}
$r2 = Test-SvrIpcGetStatusReadyCore -TimeoutSeconds 5 -PollIntervalMs 10 -InvokeAttempt $invoke2
Assert-True $r2.Ready "case2 Ready"
Assert-Equal 4 $r2.Attempts "case2 Attempts"

# Case 3: all transport fails until deadline
$invoke3 = {
    return [pscustomobject]@{ Ok = $false; Response = $null; Error = "Pipe is broken"; NonRetryable = $false }
}
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$r3 = Test-SvrIpcGetStatusReadyCore -TimeoutSeconds 1 -PollIntervalMs 50 -InvokeAttempt $invoke3
$sw.Stop()
Assert-True (-not $r3.Ready) "case3 not Ready"
Assert-True ($sw.Elapsed.TotalSeconds -lt 5) "case3 should not wait 30s"
Assert-True (-not $r3.NonRetryable) "case3 retryable timeout"

# Case 4: non-retryable CommandNotFound-style fail fast
$invoke4 = {
    return [pscustomobject]@{
        Ok           = $false
        Response     = $null
        Error        = "Invoke-SvrIpc is not recognized as the name of a cmdlet"
        NonRetryable = $true
    }
}
$sw2 = [System.Diagnostics.Stopwatch]::StartNew()
$r4 = Test-SvrIpcGetStatusReadyCore -TimeoutSeconds 30 -PollIntervalMs 2000 -InvokeAttempt $invoke4
$sw2.Stop()
Assert-True (-not $r4.Ready) "case4 not Ready"
Assert-True $r4.NonRetryable "case4 NonRetryable"
Assert-Equal 1 $r4.Attempts "case4 single attempt"
Assert-True ($sw2.Elapsed.TotalSeconds -lt 3) "case4 fail fast"

# Case 5: default attempt uses real Invoke-SvrIpcGetStatusReadyAttempt (closure regression)
$attempt = Invoke-SvrIpcGetStatusReadyAttempt -StatusTimeoutMs 1000
Assert-True ($null -ne $attempt) "case5 attempt object"
Assert-True ($attempt.PSObject.Properties.Name -contains 'NonRetryable') "case5 NonRetryable property"

# Case 6: pipe telemetry does not block (mock path only)
$pipeProbe = Test-SvrNamedPipeExists
$r6 = Test-SvrIpcGetStatusReadyCore -TimeoutSeconds 2 -PollIntervalMs 10 -InvokeAttempt {
    return [pscustomobject]@{ Ok = $true; Response = (New-MockStatusResponse); Error = ""; NonRetryable = $false }
}
Assert-True $r6.Ready "case6 Ready despite pipeExistsTelemetry=$pipeProbe"

Write-Host "PASS test-wait-svr-ipc-ready (6 cases)"
exit 0