$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptsRoot = $PSScriptRoot
. (Join-Path $scriptsRoot "_common.ps1")
. (Join-Path $scriptsRoot "_update-helpers.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-report-state.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-report.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-mock-helpers.ps1")

function Assert-True { param([bool]$C,[string]$M) if (-not $C) { throw $M } }
function Assert-Equal { param($E,$A,[string]$M) if ($E -ne $A) { throw "$M expected=$E actual=$A" } }

function Initialize-SvrRuntimeDiagnosticTestHost {
    param([string[]]$DiagnosticNames = @('wfp-runtime-appid-blob', 'wfp-runtime-appid-case'))

    $repoRoot = Get-SvrRepoRoot
    $reportPath = Join-Path $env:TEMP ("svr-runtime-full-" + [Guid]::NewGuid().ToString("N") + ".txt")
    Initialize-SvrRuntimeDiagnosticReportState -StartedAt (Get-Date) -RepoRoot $repoRoot -ReportPath $reportPath
    Set-Variable -Scope Script -Name IpcReadinessSeconds -Value 5
    Set-Variable -Scope Script -Name WaitConnectedSeconds -Value 1
    Set-Variable -Scope Script -Name DiagnosticNames -Value $DiagnosticNames
    Set-Variable -Scope Script -Name PollIntervalMs -Value 50
    Set-Variable -Scope Script -Name StatusTimeoutMs -Value 5000
    Set-Variable -Scope Script -Name IpcAttemptTimeoutMs -Value 5000
    Set-Variable -Scope Script -Name DiagnosticTimeoutMs -Value 5000
    Set-Variable -Scope Script -Name DiagnosticServicePollMs -Value 50

    function script:Add-Line {
        param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line)
        Add-SvrRuntimeDiagnosticLine -Line $Line
    }
    function script:Flush-Report { param([int]$FinalExitCode = 3) Flush-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode }
    function script:Save-Report { param([Parameter(Mandatory = $true)][int]$FinalExitCode) Save-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode }

    return $reportPath
}

function Invoke-RunnerCore {
    . (Join-Path $scriptsRoot "_runtime-diagnostic-runner-core.ps1")
    return Get-SvrRunnerExitCode
}

function Test-NoRawResultCountInWatchHelper {
    $text = Get-Content -LiteralPath (Join-Path $scriptsRoot "_update-helpers.ps1") -Raw
    if ($text -match '\$result\.Count') { throw 'Found forbidden $result.Count in _update-helpers.ps1' }
}

function Test-PipelineOutputShapes {
    $ps = [powershell]::Create()
    try {
        [void]$ps.AddScript({ return [pscustomobject]@{ Ok = $true; Id = 1 } })
        $async = $ps.BeginInvoke()
        while (-not $async.IsCompleted) { Start-Sleep -Milliseconds 10 }
        $raw = $ps.EndInvoke($async)
        $single = Get-SvrPowerShellPipelineOutputSingle -Output $raw -EmptyMessage 'empty'
        Assert-True ($null -ne $single.Ok) 'scalar PSCustomObject'

        [void]$ps.Commands.Clear()
        [void]$ps.AddScript({ return ,@([pscustomobject]@{ Ok = $true; Id = 2 }) })
        $async2 = $ps.BeginInvoke()
        while (-not $async2.IsCompleted) { Start-Sleep -Milliseconds 10 }
        $raw2 = $ps.EndInvoke($async2)
        $single2 = Get-SvrPowerShellPipelineOutputSingle -Output $raw2 -EmptyMessage 'empty2'
        Assert-True ($null -ne $single2.Ok) 'single-element array'

        $itemsNull = Get-SvrPowerShellPipelineOutputItems -Output $null
        Assert-Equal 0 $itemsNull.Count 'null -> empty items'

        $itemsScalar = Get-SvrPowerShellPipelineOutputItems -Output 'x'
        Assert-Equal 1 $itemsScalar.Count 'string scalar -> one item'
    }
    finally { $ps.Dispose() }
}

function Invoke-ShapeRun {
    param([scriptblock]$WatchFactory, [int]$ExpectedExit = 3)

    Clear-SvrRuntimeDiagnosticMockOverrides
    Register-SvrRuntimeDiagnosticMockHappyPath -DiagnosticNames @('wfp-runtime-appid-blob')
    $script:SvrIpcWithServiceWatchOverride = {
        param($Method, $PayloadJson, $WallClockTimeoutMs, $ServicePidBaseline, $ServicePidPollMs, $HelpersRoot)
        return & $WatchFactory
    }

    $reportPath = Initialize-SvrRuntimeDiagnosticTestHost -DiagnosticNames @('wfp-runtime-appid-blob')
    $threw = $false
    try {
        $runExit = Invoke-RunnerCore
        Assert-Equal $ExpectedExit $runExit "shape exit code"
    }
    catch {
        $threw = $true
        if ($_.Exception.GetType().FullName -eq 'System.Management.Automation.PropertyNotFoundException') {
            throw ("Shape run hit Count/property bug: " + $_.Exception.Message)
        }
        if ($ExpectedExit -eq 0) { throw }
    }
    finally {
        Clear-SvrRuntimeDiagnosticMockOverrides
        Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    }
}

Test-NoRawResultCountInWatchHelper
Test-PipelineOutputShapes

$passPayload = New-SvrMockDiagnosticResultPayload
$sparsePassPayload = New-SvrMockDiagnosticResultPayload -Outcome 'Pass' -SparseMessage
$shapes = @(
    @{ Label = 'sparse-no-error'; Factory = { New-SvrSparseIpcEnvelope -Ok $true -PayloadJson $sparsePassPayload }; Exit = 0 }
    @{ Label = 'sparse-fail-no-payload'; Factory = { New-SvrSparseIpcEnvelope -Ok $false -IncludeError -Error 'boom' }; Exit = 3 }
    @{ Label = 'A-scalar'; Factory = { New-SvrIpcEnvelope -Ok $true -PayloadJson $passPayload }; Exit = 0 }
    @{ Label = 'B-array1'; Factory = { ,@(New-SvrIpcEnvelope -Ok $true -PayloadJson $passPayload) }; Exit = 0 }
    @{ Label = 'C-array2'; Factory = { ,@((New-SvrIpcEnvelope -Ok $true -PayloadJson $passPayload), (New-SvrIpcEnvelope -Ok $false -Error 'second')) }; Exit = 0 }
    @{ Label = 'D-null'; Factory = { $null }; Exit = 3 }
    @{ Label = 'E-empty'; Factory = { @() }; Exit = 3 }
    @{ Label = 'F-string'; Factory = { 'not-an-envelope' }; Exit = 3 }
    @{ Label = 'G-empty-payload'; Factory = { New-SvrIpcEnvelope -Ok $true -PayloadJson '' }; Exit = 3 }
    @{ Label = 'H-malformed-json'; Factory = { New-SvrIpcEnvelope -Ok $true -PayloadJson "{bad" }; Exit = 3 }
    @{ Label = 'J-ok-false'; Factory = { New-SvrIpcEnvelope -Ok $false -Error 'mock fail' }; Exit = 3 }
)
foreach ($s in $shapes) {
    Invoke-ShapeRun -WatchFactory $s.Factory -ExpectedExit $s.Exit
}

# Happy path: two diagnostics, exit 0
Clear-SvrRuntimeDiagnosticMockOverrides
Register-SvrRuntimeDiagnosticMockHappyPath
$reportPath = Initialize-SvrRuntimeDiagnosticTestHost
$runExit = Invoke-RunnerCore
Assert-Equal 0 $runExit 'happy path exit 0'
$content = Get-Content -LiteralPath $reportPath -Raw
Assert-True ([regex]::Matches($content, 'outcome=Pass').Count -ge 2) 'report contains two Pass outcomes'
Assert-True ($content -match 'wfp-runtime-appid-blob') 'blob in report'
Assert-True ($content -match 'wfp-runtime-appid-case') 'case in report'
Clear-SvrRuntimeDiagnosticMockOverrides
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

# Failure: diagnostic #1 throws from watch
Clear-SvrRuntimeDiagnosticMockOverrides
Register-SvrRuntimeDiagnosticMockHappyPath -DiagnosticNames @('wfp-runtime-appid-blob', 'wfp-runtime-appid-case')
$call = 0
$script:SvrIpcWithServiceWatchOverride = {
    param($Method, $PayloadJson, $WallClockTimeoutMs, $ServicePidBaseline, $ServicePidPollMs, $HelpersRoot)
    $script:call++
    if ($script:call -eq 1) { throw 'mock diagnostic exception' }
    $payload = New-SvrMockDiagnosticResultPayload
    return New-SvrIpcEnvelope -Ok $true -PayloadJson $payload
}
$reportPath = Initialize-SvrRuntimeDiagnosticTestHost
$runExit = Invoke-RunnerCore
Assert-Equal 3 $runExit 'diag1 exception exit 3'
$failReport = Get-Content -LiteralPath $reportPath -Raw
Assert-True ($failReport -match 'failureCategory=RunnerInfrastructure') 'infra category on exception'
Clear-SvrRuntimeDiagnosticMockOverrides
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

# Failure: timeout message from watch
Clear-SvrRuntimeDiagnosticMockOverrides
Register-SvrRuntimeDiagnosticMockHappyPath -DiagnosticNames @('wfp-runtime-appid-blob')
$script:SvrIpcWithServiceWatchOverride = {
    param($Method, $PayloadJson, $WallClockTimeoutMs, $ServicePidBaseline, $ServicePidPollMs, $HelpersRoot)
    throw 'IPC wall-clock timeout 5000ms method=RunDiagnostic'
}
$reportPath = Initialize-SvrRuntimeDiagnosticTestHost -DiagnosticNames @('wfp-runtime-appid-blob')
$runExit = Invoke-RunnerCore
Assert-Equal 3 $runExit 'timeout exit 3'
Clear-SvrRuntimeDiagnosticMockOverrides
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

# Failure: service restart during diagnostic
Clear-SvrRuntimeDiagnosticMockOverrides
Register-SvrRuntimeDiagnosticMockHappyPath -DiagnosticNames @('wfp-runtime-appid-blob')
$script:SvrIpcWithServiceWatchOverride = {
    param($Method, $PayloadJson, $WallClockTimeoutMs, $ServicePidBaseline, $ServicePidPollMs, $HelpersRoot)
    throw 'service restarted during IPC (baselinePid=1 currentPid=2)'
}
$reportPath = Initialize-SvrRuntimeDiagnosticTestHost -DiagnosticNames @('wfp-runtime-appid-blob')
$runExit = Invoke-RunnerCore
Assert-Equal 3 $runExit 'service restart exit 3'
$restartReport = Get-Content -LiteralPath $reportPath -Raw
Assert-True ($restartReport -match 'servicePidChangedDuringDiagnostic=True') 'pid changed flag'
Clear-SvrRuntimeDiagnosticMockOverrides
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

Write-Host 'PASS test-runtime-diagnostic-runner-full'
exit 0