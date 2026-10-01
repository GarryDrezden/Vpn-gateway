# StrictMode-safe report flush at each lifecycle checkpoint.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = $PSScriptRoot
. (Join-Path $root "_runtime-diagnostic-report-state.ps1")
. (Join-Path $root "_runtime-diagnostic-report.ps1")

$tempReport = Join-Path $env:TEMP ("svr-runtime-report-" + [Guid]::NewGuid().ToString("N") + ".txt")
$startedAt = Get-Date
Initialize-SvrRuntimeDiagnosticReportState -StartedAt $startedAt -RepoRoot $root -ReportPath $tempReport
Set-Variable -Scope Script -Name IpcReadinessSeconds -Value 30
Set-Variable -Scope Script -Name WaitConnectedSeconds -Value 300
Set-Variable -Scope Script -Name DiagnosticNames -Value @("wfp-runtime-appid-blob", "wfp-runtime-appid-case")

function Invoke-Checkpoint {
    param([string]$Name, [scriptblock]$Mutate)
    & $Mutate
    Flush-SvrRuntimeDiagnosticReport -FinalExitCode 3
    if (-not (Test-Path -LiteralPath $tempReport)) {
        throw "Report missing after checkpoint $Name"
    }
}

# A startup
Invoke-Checkpoint 'startup' { }

# B IPC ready
Invoke-Checkpoint 'ipc-ready' {
    Set-Variable -Scope Script -Name ipcReady -Value $true
    Set-Variable -Scope Script -Name ipcAttempts -Value 1
}

# C VPN connected
Invoke-Checkpoint 'vpn-connected' {
    Set-Variable -Scope Script -Name vpnConnected -Value $true
    Set-Variable -Scope Script -Name driverLoaded -Value $true
    Set-Variable -Scope Script -Name wfpPolicyHealthy -Value $true
}

# D diagnostic STARTED (failure fields still empty)
Invoke-Checkpoint 'diagnostic-started' {
    Set-Variable -Scope Script -Name activeDiagnostic -Value 'wfp-runtime-appid-blob'
    Set-Variable -Scope Script -Name activeDiagnosticState -Value 'STARTED'
    Set-Variable -Scope Script -Name activeDiagnosticStartedAt -Value (Get-Date).ToString('o')
}

# E diagnostic COMPLETED
Invoke-Checkpoint 'diagnostic-completed' {
    Set-Variable -Scope Script -Name activeDiagnosticState -Value 'COMPLETED'
    Set-Variable -Scope Script -Name activeDiagnosticEndedAt -Value (Get-Date).ToString('o')
}

# F diagnostic FAILED without setting failure* (StrictMode regression)
Invoke-Checkpoint 'diagnostic-failed' {
    Set-Variable -Scope Script -Name activeDiagnosticState -Value 'FAILED'
    Set-Variable -Scope Script -Name lastError -Value 'mock diagnostic failed'
}

# G infrastructure FAILED with failure fields populated
Invoke-Checkpoint 'infra-failed' {
    Set-Variable -Scope Script -Name failureCategory -Value 'RunnerDependency'
    Set-Variable -Scope Script -Name failureExceptionType -Value 'System.Exception'
    Set-Variable -Scope Script -Name failureExceptionMessage -Value 'mock'
    Set-Variable -Scope Script -Name failurePositionMessage -Value 'mock position'
    Set-Variable -Scope Script -Name failureScriptStackTrace -Value 'mock stack'
}

Write-Host "PASS test-run-runtime-diagnostic-report-state"
Remove-Item -LiteralPath $tempReport -Force -ErrorAction SilentlyContinue
exit 0