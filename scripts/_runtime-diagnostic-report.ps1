# Report rendering for run-runtime-diagnostic.ps1 (requires _runtime-diagnostic-report-state.ps1).

function Add-SvrRuntimeDiagnosticLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Line
    )
    if ($null -eq $Line) {
        $Line = ""
    }
    $lines = Get-SvrRunnerScriptField -Name 'consoleLines' -Default $null
    if ($null -eq $lines -or -not ($lines -is [System.Collections.IList])) {
        $lines = New-Object System.Collections.Generic.List[string]
        Set-Variable -Scope Script -Name consoleLines -Value $lines
    }
    [void]$lines.Add([string]$Line)
    Write-Host $Line
}

function Write-SvrRuntimeDiagnosticReportFile {
    param(
        [Parameter(Mandatory = $true)][int]$FinalExitCode,
        [switch]$Silent
    )

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("=== VPN Route runtime diagnostic runner ===")
    [void]$sb.AppendLine("timestamp=" + (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK"))

    $startedAtValue = Get-SvrRunnerScriptField -Name 'startedAt' -Default (Get-Date)
    [void]$sb.AppendLine("startedAt=" + ([datetime]$startedAtValue).ToString("yyyy-MM-ddTHH:mm:ssK"))
    [void]$sb.AppendLine("repoRoot=" + (Get-SvrRunnerScriptField -Name 'repoRoot'))
    [void]$sb.AppendLine("exitCode=" + $FinalExitCode)
    [void]$sb.AppendLine("vpnConnected=" + (Get-SvrRunnerScriptField -Name 'vpnConnected' -Default $false))
    [void]$sb.AppendLine("driverLoaded=" + (Get-SvrRunnerScriptField -Name 'driverLoaded' -Default $false))
    [void]$sb.AppendLine("wfpPolicyHealthy=" + (Get-SvrRunnerScriptField -Name 'wfpPolicyHealthy' -Default $false))
    [void]$sb.AppendLine("serviceName=" + (Get-SvrRunnerScriptField -Name 'serviceName'))
    [void]$sb.AppendLine("serviceStatus=" + (Get-SvrRunnerScriptField -Name 'serviceStatus'))
    [void]$sb.AppendLine("servicePid=" + (Get-SvrRunnerScriptField -Name 'servicePid'))
    [void]$sb.AppendLine("pipeExistsTelemetry=" + (Get-SvrRunnerScriptField -Name 'pipeExistsTelemetry' -Default $false))
    [void]$sb.AppendLine("ipcReady=" + (Get-SvrRunnerScriptField -Name 'ipcReady' -Default $false))
    [void]$sb.AppendLine("ipcAttempts=" + (Get-SvrRunnerScriptField -Name 'ipcAttempts' -Default 0))
    [void]$sb.AppendLine("lastIpcError=" + (Get-SvrRunnerScriptField -Name 'lastIpcError'))
    [void]$sb.AppendLine("ipcReadinessSeconds=" + (Get-SvrRunnerScriptField -Name 'IpcReadinessSeconds' -Default 30))
    [void]$sb.AppendLine("waitConnectedSeconds=" + (Get-SvrRunnerScriptField -Name 'WaitConnectedSeconds' -Default 300))

    $diagNames = Get-SvrRunnerScriptField -Name 'DiagnosticNames' -Default @()
    if ($diagNames -is [string]) { $diagNames = @($diagNames) }
    [void]$sb.AppendLine("diagnostics=" + ($diagNames -join ", "))
    [void]$sb.AppendLine("activeDiagnostic=" + (Get-SvrRunnerScriptField -Name 'activeDiagnostic'))
    [void]$sb.AppendLine("activeDiagnosticState=" + (Get-SvrRunnerScriptField -Name 'activeDiagnosticState'))
    [void]$sb.AppendLine("activeDiagnosticStartedAt=" + (Get-SvrRunnerScriptField -Name 'activeDiagnosticStartedAt'))
    [void]$sb.AppendLine("activeDiagnosticEndedAt=" + (Get-SvrRunnerScriptField -Name 'activeDiagnosticEndedAt'))
    [void]$sb.AppendLine("servicePidAtDiagnosticStart=" + (Get-SvrRunnerScriptField -Name 'servicePidAtDiagnosticStart'))
    [void]$sb.AppendLine("servicePidChangedDuringDiagnostic=" + (Get-SvrRunnerScriptField -Name 'servicePidChangedDuringDiagnostic' -Default $false))

    $reportLastError = [string](Get-SvrRunnerScriptField -Name 'lastError')
    if (-not [string]::IsNullOrWhiteSpace($reportLastError)) {
        [void]$sb.AppendLine("lastError=" + $reportLastError)
    }

    $reportDiagnostics = Get-SvrRunnerScriptField -Name 'diagnosticResults' -Default $null
    $completedCount = 0
    if ($null -ne $reportDiagnostics) { $completedCount = @($reportDiagnostics).Count }
    [void]$sb.AppendLine("diagnosticsCompletedBeforeFailure=" + $completedCount)

    [void]$sb.AppendLine("failureCategory=" + (Get-SvrRunnerScriptField -Name 'failureCategory'))
    [void]$sb.AppendLine("failureExceptionType=" + (Get-SvrRunnerScriptField -Name 'failureExceptionType'))
    [void]$sb.AppendLine("failureExceptionMessage=" + (Get-SvrRunnerScriptField -Name 'failureExceptionMessage'))
    [void]$sb.AppendLine("failurePositionMessage=" + (Get-SvrRunnerScriptField -Name 'failurePositionMessage'))
    [void]$sb.AppendLine("failureScriptStackTrace=" + (Get-SvrRunnerScriptField -Name 'failureScriptStackTrace'))
    [void]$sb.AppendLine()

    if ($null -ne $reportDiagnostics) {
        foreach ($entry in $reportDiagnostics) {
            [void]$sb.AppendLine("=== Diagnostic: $($entry.Name) ===")
            [void]$sb.AppendLine("ipcOk=" + $entry.IpcOk)
            [void]$sb.AppendLine("ipcError=" + $entry.IpcError)
            [void]$sb.AppendLine("outcome=" + $entry.Outcome)
            [void]$sb.AppendLine("message=" + $entry.Message)
            [void]$sb.AppendLine("payloadJson=" + $entry.PayloadJson)
            [void]$sb.AppendLine()
        }
    }

    [void]$sb.AppendLine("=== Console log ===")
    $consoleLines = Get-SvrRunnerScriptField -Name 'consoleLines' -Default $null
    if ($null -ne $consoleLines) {
        foreach ($line in $consoleLines) {
            [void]$sb.AppendLine($line)
        }
    }

    $targetReport = [string](Get-SvrRunnerScriptField -Name 'reportPath')
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($targetReport, $sb.ToString(), $utf8NoBom)
    if (-not $Silent) {
        Add-SvrRuntimeDiagnosticLine ("Report saved: " + $targetReport)
    }
}

function Flush-SvrRuntimeDiagnosticReport {
    param([int]$FinalExitCode = 3)
    Write-SvrRuntimeDiagnosticReportFile -FinalExitCode $FinalExitCode -Silent
}

function Save-SvrRuntimeDiagnosticReport {
    param([Parameter(Mandatory = $true)][int]$FinalExitCode)
    Write-SvrRuntimeDiagnosticReportFile -FinalExitCode $FinalExitCode
}