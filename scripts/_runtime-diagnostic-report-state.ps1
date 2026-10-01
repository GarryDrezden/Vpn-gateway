# StrictMode-safe report state helpers for run-runtime-diagnostic.ps1

function Get-SvrRunnerScriptField {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        $Default = ""
    )

    if (-not (Test-Path -LiteralPath "variable:script:$Name")) {
        return $Default
    }

    $variable = Get-Variable -Scope Script -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $variable) {
        return $Default
    }

    $value = $variable.Value
    if ($null -eq $value) {
        return $Default
    }

    return $value
}

function Set-SvrRunnerScriptField {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        $Value
    )
    Set-Variable -Scope Script -Name $Name -Value $Value
}

function Set-SvrRunnerExitCode {
    param([Parameter(Mandatory = $true)][int]$Value)
    Set-SvrRunnerScriptField -Name 'exitCode' -Value $Value
}

function Get-SvrRunnerExitCode {
    return [int](Get-SvrRunnerScriptField -Name 'exitCode' -Default 3)
}

function Initialize-SvrRuntimeDiagnosticReportState {
    param(
        [Parameter(Mandatory = $true)][datetime]$StartedAt,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ReportPath
    )

    Set-Variable -Scope Script -Name startedAt -Value $StartedAt
    Set-Variable -Scope Script -Name repoRoot -Value $RepoRoot
    Set-Variable -Scope Script -Name reportPath -Value $ReportPath

    Set-Variable -Scope Script -Name exitCode -Value 3
    Set-Variable -Scope Script -Name vpnConnected -Value $false
    Set-Variable -Scope Script -Name driverLoaded -Value $false
    Set-Variable -Scope Script -Name wfpPolicyHealthy -Value $false
    Set-Variable -Scope Script -Name ipcReady -Value $false
    Set-Variable -Scope Script -Name ipcAttempts -Value 0
    Set-Variable -Scope Script -Name lastIpcError -Value ""

    Set-Variable -Scope Script -Name serviceName -Value "SelectiveVpnRouter"
    Set-Variable -Scope Script -Name serviceStatus -Value ""
    Set-Variable -Scope Script -Name servicePid -Value ""
    Set-Variable -Scope Script -Name pipeExistsTelemetry -Value $false

    Set-Variable -Scope Script -Name activeDiagnostic -Value ""
    Set-Variable -Scope Script -Name activeDiagnosticState -Value ""
    Set-Variable -Scope Script -Name activeDiagnosticStartedAt -Value ""
    Set-Variable -Scope Script -Name activeDiagnosticEndedAt -Value ""
    Set-Variable -Scope Script -Name servicePidAtDiagnosticStart -Value ""
    Set-Variable -Scope Script -Name servicePidChangedDuringDiagnostic -Value $false

    Set-Variable -Scope Script -Name lastError -Value ""
    Set-Variable -Scope Script -Name failureCategory -Value ""
    Set-Variable -Scope Script -Name failureExceptionType -Value ""
    Set-Variable -Scope Script -Name failureExceptionMessage -Value ""
    Set-Variable -Scope Script -Name failurePositionMessage -Value ""
    Set-Variable -Scope Script -Name failureScriptStackTrace -Value ""

    Set-Variable -Scope Script -Name diagnosticResults -Value (New-Object System.Collections.Generic.List[object])
    Set-Variable -Scope Script -Name consoleLines -Value (New-Object System.Collections.Generic.List[string])
}