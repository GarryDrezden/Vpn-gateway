# VPN Route update-desktop pipeline helpers (quiet console + full log)
# State is initialized in Initialize-SvrUpdateSession only (safe to dot-source repeatedly).

if (-not (Get-Variable -Name SvrUpdateQuiet -Scope Global -ErrorAction SilentlyContinue)) { $global:SvrUpdateQuiet = $false }
if (-not (Get-Variable -Name SvrUpdateLogPath -Scope Global -ErrorAction SilentlyContinue)) { $global:SvrUpdateLogPath = $null }
if (-not (Get-Variable -Name SvrUpdateRepoRoot -Scope Global -ErrorAction SilentlyContinue)) { $global:SvrUpdateRepoRoot = $null }
if (-not (Get-Variable -Name SvrStepResults -Scope Global -ErrorAction SilentlyContinue)) { $global:SvrStepResults = @() }
if (-not (Get-Variable -Name SvrStageOutputs -Scope Global -ErrorAction SilentlyContinue)) { $global:SvrStageOutputs = @{} }
if (-not (Get-Variable -Name SvrCurrentStage -Scope Global -ErrorAction SilentlyContinue)) { $global:SvrCurrentStage = $null }

if ($env:SVR_UPDATE_LOG_PATH -and -not $global:SvrUpdateLogPath) {
    $global:SvrUpdateLogPath = $env:SVR_UPDATE_LOG_PATH
}
if ($env:SVR_UPDATE_QUIET -eq "1") {
    $global:SvrUpdateQuiet = $true
}

function Initialize-SvrUpdateSession {
    param(
        [switch]$Quiet,
        [string]$RepoRoot = (Get-SvrRepoRoot)
    )

    $global:SvrUpdateQuiet = [bool]$Quiet
    $global:SvrUpdateRepoRoot = $RepoRoot
    $global:SvrStepResults = @()
    $global:SvrStageOutputs = @{}
    $global:SvrCurrentStage = $null

    $logDir = Join-Path $RepoRoot "artifacts\logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $global:SvrUpdateLogPath = Join-Path $logDir "update-desktop-$stamp.log"
    Write-SvrUpdateLogLine "VPN Route update-desktop log started $(Get-Date -Format o)"
}

function Write-SvrUpdateLogLine {
    param([Parameter(Mandatory = $true)][string]$Line)
    $path = $global:SvrUpdateLogPath
    if (-not $path -and $env:SVR_UPDATE_LOG_PATH) {
        $path = $env:SVR_UPDATE_LOG_PATH
    }
    if (-not $path) { return }
    Add-Content -LiteralPath $path -Value $Line -Encoding UTF8
}

function ConvertTo-SvrEscapedCommandLine {
    param([Parameter(Mandatory = $true)][string[]]$Tokens)

    $escaped = foreach ($token in $Tokens) {
        if ($null -eq $token) { continue }
        if ($token -match '[\s"]') {
            '"' + ($token -replace '"', '\"') + '"'
        }
        else {
            $token
        }
    }
    return ($escaped -join " ")
}

function Get-SvrRelativeRepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $rootPath = if ($global:SvrUpdateRepoRoot) { $global:SvrUpdateRepoRoot } else { Get-SvrRepoRoot }
    $fullRoot = [IO.Path]::GetFullPath($rootPath)
    if (-not $fullRoot.EndsWith("\")) { $fullRoot += "\" }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length)
    }
    return $Path
}

function Add-SvrStageOutput {
    param([string]$Text)
    if (-not $global:SvrCurrentStage -or [string]::IsNullOrWhiteSpace($Text)) { return }
    if (-not $global:SvrStageOutputs.ContainsKey($global:SvrCurrentStage)) {
        $global:SvrStageOutputs[$global:SvrCurrentStage] = ""
    }
    $global:SvrStageOutputs[$global:SvrCurrentStage] += $Text.TrimEnd() + "`n"
}

function Set-SvrStageOutput {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Text
    )
    $global:SvrStageOutputs[$Stage] = $Text
}

function Get-SvrStageOutput {
    param([Parameter(Mandatory = $true)][string]$Stage)
    if ($global:SvrStageOutputs.ContainsKey($Stage)) {
        return [string]$global:SvrStageOutputs[$Stage]
    }
    return ""
}

function Add-SvrStepResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "FAIL", "SKIP", "INFO")][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$Label,
        [string]$Detail = "",
        [TimeSpan]$Duration = [TimeSpan]::Zero
    )
    $global:SvrStepResults += [pscustomobject]@{
        Name     = $Name
        Outcome  = $Outcome
        Label    = $Label
        Detail   = $Detail
        Duration = $Duration
    }
}

function Write-SvrUpdateDetail {
    param([string]$Line)
    if ($Line) {
        Write-SvrUpdateLogLine $Line
        Add-SvrStageOutput $Line
    }
    if (-not $global:SvrUpdateQuiet) { Write-Host $Line }
}

function Get-SvrMeaningfulErrorLines {
    param(
        [string]$Output,
        [int]$MaxLines = 5
    )

    if ([string]::IsNullOrWhiteSpace($Output)) { return @() }

    $patterns = @(
        ':error\s',
        'error\sCS\d+',
        'error\sMSB\d+',
        'Unhandled exception',
        'Fatal error',
        '\bFAIL\b',
        'Exception',
        'UnauthorizedAccess',
        'Access denied',
        '\bcannot\b',
        '\bfailed\b',
        'network-catalog-smoke',
        'routing-fast'
    )

    $lines = @($Output -split "`r?`n" | ForEach-Object { $_.TrimEnd() })
    $picked = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        if (-not $line) { continue }

        $matched = $false
        foreach ($pattern in $patterns) {
            if ($line -match $pattern) {
                $matched = $true
                break
            }
        }
        if (-not $matched) { continue }

        if ($i -gt 0 -and $picked.Count -lt $MaxLines) {
            $prev = $lines[$i - 1].Trim()
            if ($prev -and ($prev -match '\.(cs|xaml|csproj)\(' -or $prev -match '\.(cs|xaml|csproj):\d+')) {
                if (-not $picked.Contains($prev)) { [void]$picked.Add($prev) }
            }
        }

        if (-not $picked.Contains($line)) { [void]$picked.Add($line) }
        if ($picked.Count -ge $MaxLines) { break }
    }

    if ($picked.Count -eq 0) {
        $nonEmpty = @($lines | Where-Object { $_.Trim() })
        $start = [Math]::Max(0, $nonEmpty.Count - 5)
        for ($i = $start; $i -lt $nonEmpty.Count; $i++) {
            [void]$picked.Add($nonEmpty[$i].Trim())
        }
    }

    return @($picked | Select-Object -First $MaxLines)
}

function Format-SvrStepDetail {
    param($StepResult)
    if ($StepResult.Detail) { return $StepResult.Detail }
    if ($StepResult.Duration -and $StepResult.Duration.TotalSeconds -gt 0) {
        return ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:N1}s", $StepResult.Duration.TotalSeconds))
    }
    return ""
}

function Write-SvrCompactConsole {
    param(
        [switch]$Success,
        [string]$FailedStep,
        [string]$FailedMessage
    )

    Write-Host ""
    Write-Host "VPN Route update"
    Write-Host ""

    $failedStage = $null
    foreach ($r in $global:SvrStepResults) {
        if ($r.Name -eq "deploy" -and $r.Outcome -eq "PASS") { continue }

        $detail = Format-SvrStepDetail $r
        $suffix = if ($detail) { " " * [Math]::Max(1, 12 - $r.Name.Length) + $detail } else { "" }

        if ($r.Outcome -eq "PASS") {
            Write-Host ("PASS  {0}{1}" -f $r.Name, $suffix) -ForegroundColor Green
            continue
        }

        if ($r.Outcome -eq "SKIP") {
            if (-not $global:SvrUpdateQuiet) {
                Write-Host ("SKIP  {0}{1}" -f $r.Name, $suffix) -ForegroundColor DarkGray
            }
            continue
        }

        if ($r.Outcome -eq "FAIL") {
            Write-Host ("FAIL  {0}" -f $r.Name) -ForegroundColor Red
            if (-not $failedStage) { $failedStage = $r.Name }
            if ($r.Name -ne "rollback") { continue }
        }
    }

    if (-not $FailedStep) { $FailedStep = $failedStage }
    if ($FailedStep) {
        $stageOutput = Get-SvrStageOutput -Stage $FailedStep
        if ($FailedMessage -and -not $stageOutput.Contains($FailedMessage.Trim())) {
            $stageOutput = ($FailedMessage.Trim() + "`n" + $stageOutput).Trim()
        }
        $errorLines = Get-SvrMeaningfulErrorLines -Output $stageOutput
        if ($errorLines.Count -gt 0) {
            Write-Host ""
            foreach ($line in $errorLines) { Write-Host $line }
        }
    }

    if ($Success) {
        Write-Host ""
        Write-Host "DONE  updated" -ForegroundColor Green
    }

    if ($global:SvrUpdateLogPath) {
        Write-Host ""
        Write-Host ("Log: " + (Get-SvrRelativeRepoPath $global:SvrUpdateLogPath))
    }
}

function Invoke-SvrDotNet {
    param(
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$ExpectedExitCode = 0
    )

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $cwd = if ($WorkingDirectory) { $WorkingDirectory } else { $global:SvrUpdateRepoRoot }
    $prevLang = $env:DOTNET_CLI_UI_LANGUAGE
    $prevLogo = $env:DOTNET_NOLOGO
    $env:DOTNET_CLI_UI_LANGUAGE = "en"
    $env:DOTNET_NOLOGO = "1"

    $argText = ($ArgumentList | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join " "
    Write-SvrUpdateLogLine ">>> dotnet $argText"

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $dotnet
    $psi.Arguments = $argText
    $psi.WorkingDirectory = $cwd
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8

    $proc = [Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    if ($prevLang) { $env:DOTNET_CLI_UI_LANGUAGE = $prevLang } else { Remove-Item Env:DOTNET_CLI_UI_LANGUAGE -ErrorAction SilentlyContinue }
    if ($prevLogo) { $env:DOTNET_NOLOGO = $prevLogo } else { Remove-Item Env:DOTNET_NOLOGO -ErrorAction SilentlyContinue }

    $combined = ($stdout + "`n" + $stderr).Trim()
    foreach ($line in ($combined -split "`r?`n")) {
        if ($line.Trim()) { Write-SvrUpdateLogLine $line }
    }
    Add-SvrStageOutput $combined

    if ($proc.ExitCode -ne $ExpectedExitCode) {
        throw "dotnet exited $($proc.ExitCode)"
    }

    return [pscustomobject]@{
        ExitCode = $proc.ExitCode
        Output   = $combined
    }
}

function Get-SvrDotNetTestPassedCount {
    param([string]$Output)
    $total = 0
    foreach ($match in [regex]::Matches($Output, "Passed![^\r\n]*Passed:\s+(\d+)")) {
        $total += [int]$match.Groups[1].Value
    }
    if ($total -gt 0) { return $total }
    if ($Output -match "Passed:\s+(\d+),\s+Failed:\s+\d+") {
        return [int]$Matches[1]
    }
    if ($Output -match "Total tests:\s+(\d+).*Passed:\s+(\d+)") {
        return [int]$Matches[2]
    }
    return $null
}

function Invoke-SvrExternal {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [int]$ExpectedExitCode = 0
    )

    $argText = if ($ArgumentList.Count -gt 0) { " " + ($ArgumentList -join " ") } else { "" }
    Write-SvrUpdateLogLine ">>> $FilePath$argText"

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ($ArgumentList -join " ")
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8

    $proc = [Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $combined = ($stdout + "`n" + $stderr).Trim()
    foreach ($line in ($combined -split "`r?`n")) {
        if ($line.Trim()) { Write-SvrUpdateLogLine $line }
    }
    Add-SvrStageOutput $combined

    if ($proc.ExitCode -ne $ExpectedExitCode) {
        throw "$FilePath exited $($proc.ExitCode)"
    }

    return $combined
}

function Invoke-SvrCaptureScript {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [int[]]$AllowedExitCodes = @(0)
    )

    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw "script not found: $FilePath"
    }

    $psExe = (Get-Command powershell.exe -ErrorAction Stop).Source
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $FilePath
    ) + $ArgumentList
    $argText = ConvertTo-SvrEscapedCommandLine -Tokens $args
    Write-SvrUpdateLogLine ">>> $psExe $argText"

    if ($global:SvrUpdateLogPath) {
        $env:SVR_UPDATE_LOG_PATH = $global:SvrUpdateLogPath
    }
    if ($global:SvrUpdateQuiet) {
        $env:SVR_UPDATE_QUIET = "1"
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $psExe
    $psi.Arguments = $argText
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8

    $proc = [Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $combined = ($stdout + "`n" + $stderr).Trim()
    if ($combined) {
        if (-not $global:SvrUpdateQuiet) {
            Write-Host $combined
        }
        foreach ($line in ($combined -split "`r?`n")) {
            if ($line.Trim()) { Write-SvrUpdateLogLine $line.TrimEnd() }
        }
        Add-SvrStageOutput $combined
    }

    if ($proc.ExitCode -notin $AllowedExitCodes) {
        throw "$(Split-Path -Leaf $FilePath) exited $($proc.ExitCode)"
    }

    return $combined
}

function Invoke-SvrStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [string]$PassLabel,
        [string]$PassDetail
    )

    $global:SvrCurrentStage = $Name
    $global:SvrStageOutputs[$Name] = ""
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-SvrUpdateLogLine "=== STEP $Name ==="

    try {
        $result = & $Action
        $sw.Stop()
        $label = if ($PassLabel) { $PassLabel } else { $Name }
        Add-SvrStepResult -Name $Name -Outcome PASS -Label $label -Detail $PassDetail -Duration $sw.Elapsed
        return $result
    }
    catch {
        $sw.Stop()
        Add-SvrStageOutput $_.Exception.Message
        Add-SvrStepResult -Name $Name -Outcome FAIL -Label $Name -Duration $sw.Elapsed
        Write-SvrCompactConsole -FailedStep $Name -FailedMessage $_.Exception.Message
        throw
    }
    finally {
        $global:SvrCurrentStage = $null
    }
}

function Write-SvrUpdateSummary {
    param([switch]$Success)
    Write-SvrCompactConsole -Success:$Success
}

function Test-SvrStagingPublish {
    param([Parameter(Mandatory = $true)][string]$StagingDir)

    $required = @(
        "SelectiveVpnRouter.App.exe",
        "SelectiveVpnRouter.Service.exe",
        "SelectiveVpnRouter.Probe.exe"
    )

    $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $StagingDir $_)) })
    if ($missing.Count -gt 0) {
        throw "staging publish missing required files: $($missing -join ', ')"
    }
}

function Test-SvrNamedPipeExists {
    param([string]$PipeName = "SelectiveVpnRouter")
    return Test-Path -LiteralPath ("\\.\pipe\" + $PipeName)
}

function Test-SvrIpcTransientError {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) { return $false }

    $patterns = @(
        "IPC response header EOF"
        "IPC response body EOF"
        "Unable to connect"
        "Pipe is broken"
        "broken pipe"
        "Pipe not found"
        "did not respond"
        "The pipe has been ended"
        "Cannot connect"
        "No process is on the other end of the pipe"
        "The system cannot find the file specified"
        "Timed out"
        "timeout"
    )
    foreach ($pattern in $patterns) {
        if ($Message.IndexOf($pattern, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Test-SvrServiceProcessAlive {
    param([string]$ServiceName = "SelectiveVpnRouter")

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -ne "Running") {
        return $false
    }

    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if (-not $wmi -or [int]$wmi.ProcessId -le 0) {
        return $false
    }

    return $null -ne (Get-Process -Id ([int]$wmi.ProcessId) -ErrorAction SilentlyContinue)
}

function Wait-SvrIpcReady {
    param(
        [string]$ServiceName = "SelectiveVpnRouter",
        [int]$TimeoutSeconds = 15,
        [int]$PollIntervalMs = 250,
        [int]$AttemptTimeoutMs = 5000
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempt = 0
    $lastError = "unknown"

    while ((Get-Date) -lt $deadline) {
        $attempt++

        if (-not (Test-SvrServiceProcessAlive -ServiceName $ServiceName)) {
            throw "service process is not running"
        }

        if (-not (Test-SvrNamedPipeExists)) {
            $lastError = "named pipe not available yet"
            Write-SvrUpdateLogLine "IPC readiness attempt ${attempt}: waiting for pipe"
            Start-Sleep -Milliseconds $PollIntervalMs
            continue
        }

        try {
            $resp = Invoke-SvrIpc -Method "GetStatus" -TimeoutMs $AttemptTimeoutMs
            if ($resp.Ok) {
                Write-SvrUpdateLogLine "IPC ready after $attempt attempt(s)"
                return
            }

            $lastError = if ([string]::IsNullOrWhiteSpace($resp.Error)) { "GetStatus returned Ok=false" } else { [string]$resp.Error }
            if (-not (Test-SvrIpcTransientError $lastError)) {
                throw "GetStatus failed: $lastError"
            }

            Write-SvrUpdateLogLine "IPC readiness attempt ${attempt}: transient - $lastError"
        }
        catch {
            $lastError = $_.Exception.Message
            if (-not (Test-SvrIpcTransientError $lastError)) {
                throw
            }

            Write-SvrUpdateLogLine "IPC readiness attempt ${attempt}: transient - $lastError"
        }

        Start-Sleep -Milliseconds $PollIntervalMs
    }

    throw "IPC not ready after ${TimeoutSeconds}s (last: $lastError)"
}

function Invoke-SvrIpcGetStatusWithRetry {
    param(
        [int]$MaxAttempts = 24,
        [int]$PollIntervalMs = 250,
        [int]$TimeoutMs = 5000
    )

    $lastError = "unknown"
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if (-not (Test-SvrNamedPipeExists)) {
            $lastError = "named pipe not available yet"
            if ($attempt -lt $MaxAttempts) {
                Write-SvrUpdateLogLine "GetStatus retry ${attempt}: waiting for pipe"
                Start-Sleep -Milliseconds $PollIntervalMs
                continue
            }
            throw "IPC not ready (last: $lastError)"
        }

        try {
            $resp = Invoke-SvrIpc -Method "GetStatus" -TimeoutMs $TimeoutMs
            if ($resp.Ok) {
                return $resp
            }

            $lastError = if ([string]::IsNullOrWhiteSpace($resp.Error)) { "GetStatus returned Ok=false" } else { [string]$resp.Error }
            if ($attempt -lt $MaxAttempts -and (Test-SvrIpcTransientError $lastError)) {
                Write-SvrUpdateLogLine "GetStatus retry ${attempt}: transient - $lastError"
                Start-Sleep -Milliseconds $PollIntervalMs
                continue
            }

            throw "GetStatus failed: $lastError"
        }
        catch {
            $lastError = $_.Exception.Message
            if ($attempt -lt $MaxAttempts -and (Test-SvrIpcTransientError $lastError)) {
                Write-SvrUpdateLogLine "GetStatus retry ${attempt}: transient - $lastError"
                Start-Sleep -Milliseconds $PollIntervalMs
                continue
            }

            throw
        }
    }

    throw "GetStatus failed: $lastError"
}

function Test-SvrIpcRunnerNonRetryableError {
    param(
        [Parameter(Mandatory = $true)]$ErrorRecord
    )

    $ex = $ErrorRecord.Exception
    if ($ex -is [System.Management.Automation.CommandNotFoundException]) {
        return $true
    }

    if ($ex -is [System.Management.Automation.ParameterBindingException]) {
        return $true
    }

    if ($ex -is [System.Management.Automation.ParseException]) {
        return $true
    }

    $msg = [string]$ErrorRecord.Exception.Message
    if ($msg -match 'is not recognized as the name of a cmdlet') {
        return $true
    }

    if ($msg -match 'Check runner dependency initialization') {
        return $true
    }

    return $false
}

function Invoke-SvrIpcGetStatusReadyAttempt {
    param([int]$StatusTimeoutMs = 5000)

    try {
        $resp = Invoke-SvrIpc -Method "GetStatus" -TimeoutMs $StatusTimeoutMs
        if ($resp.Ok) {
            return [pscustomobject]@{
                Ok           = $true
                Response     = $resp
                Error        = ""
                NonRetryable = $false
            }
        }

        $err = if ([string]::IsNullOrWhiteSpace($resp.Error)) { "GetStatus Ok=false" } else { [string]$resp.Error }
        return [pscustomobject]@{
            Ok           = $false
            Response     = $resp
            Error        = $err
            NonRetryable = -not (Test-SvrIpcTransientError $err)
        }
    }
    catch {
        $nonRetry = Test-SvrIpcRunnerNonRetryableError $_
        if (-not $nonRetry) {
            $nonRetry = -not (Test-SvrIpcTransientError $_.Exception.Message)
        }

        return [pscustomobject]@{
            Ok           = $false
            Response     = $null
            Error        = $_.Exception.Message
            NonRetryable = $nonRetry
            ErrorRecord  = $_
        }
    }
}

function Assert-SvrRunnerIpcDependencies {
    $required = @(
        'Invoke-SvrIpc',
        'Invoke-SvrIpcCore',
        'Invoke-SvrIpcGetStatusReadyAttempt',
        'Wait-SvrIpcGetStatusReady',
        'Invoke-SvrIpcWithServiceWatch',
        'Get-SvrServiceProcessId'
    )

    foreach ($name in $required) {
        if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
            throw "Required helper $name is not loaded. Check runner dependency initialization."
        }
    }
}

function Test-SvrIpcGetStatusReadyCore {
    param(
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][int]$PollIntervalMs,
        [Parameter(Mandatory = $true)][scriptblock]$InvokeAttempt,
        $InvokeAttemptArgument = $null
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempts = 0
    $lastError = ""
    $lastResponse = $null
    $lastNonRetryable = $false
    $lastErrorRecord = $null

    while ($true) {
        $attempts++
        if ($null -ne $InvokeAttemptArgument) {
            $attempt = & $InvokeAttempt $InvokeAttemptArgument
        }
        else {
            $attempt = & $InvokeAttempt
        }

        if ($attempt.Ok) {
            return [pscustomobject]@{
                Ready          = $true
                Attempts       = $attempts
                LastError      = ""
                StatusResponse = $attempt.Response
                NonRetryable   = $false
                ErrorRecord    = $null
            }
        }

        $lastError = if ([string]::IsNullOrWhiteSpace($attempt.Error)) { "GetStatus not ready" } else { [string]$attempt.Error }
        $lastResponse = $attempt.Response
        $lastNonRetryable = [bool]$attempt.NonRetryable
        if (@($attempt.PSObject.Properties.Match('ErrorRecord')).Count -gt 0) {
            $lastErrorRecord = $attempt.ErrorRecord
        }

        if ($lastNonRetryable) {
            break
        }

        if ((Get-Date) -ge $deadline) {
            break
        }

        Start-Sleep -Milliseconds $PollIntervalMs
    }

    return [pscustomobject]@{
        Ready          = $false
        Attempts       = $attempts
        LastError      = $lastError
        StatusResponse = $lastResponse
        NonRetryable   = $lastNonRetryable
        ErrorRecord    = $lastErrorRecord
    }
}

function Wait-SvrIpcGetStatusReady {
    param(
        [int]$TimeoutSeconds = 30,
        [int]$PollIntervalMs = 2000,
        [int]$StatusTimeoutMs = 5000,
        [scriptblock]$InvokeAttempt = $null,
        $InvokeAttemptArgument = $null
    )

    if ($null -eq $InvokeAttempt) {
        $InvokeAttempt = ${function:Invoke-SvrIpcGetStatusReadyAttempt}
        $InvokeAttemptArgument = $StatusTimeoutMs
    }

    return Test-SvrIpcGetStatusReadyCore `
        -TimeoutSeconds $TimeoutSeconds `
        -PollIntervalMs $PollIntervalMs `
        -InvokeAttempt $InvokeAttempt `
        -InvokeAttemptArgument $InvokeAttemptArgument
}

function Invoke-SvrReadExact {
    param(
        [Parameter(Mandatory = $true)][System.IO.Stream]$Stream,
        [Parameter(Mandatory = $true)][byte[]]$Buffer,
        [int]$Offset = 0,
        [Parameter(Mandatory = $true)][int]$Length,
        [string]$EofMessage = "IPC response EOF",
        $DeadlineUtc = $null
    )

    $read = 0
    while ($read -lt $Length) {
        if ($null -ne $DeadlineUtc -and ([datetime]::UtcNow -gt $DeadlineUtc)) {
            throw "IPC read wall-clock timeout (read $read of $Length bytes)"
        }

        if ($Stream.CanTimeout) {
            $Stream.ReadTimeout = 1000
        }

        try {
            $n = $Stream.Read($Buffer, $Offset + $read, $Length - $read)
        }
        catch [System.IO.IOException] {
            if ($_.Exception.InnerException -is [System.TimeoutException]) {
                continue
            }

            throw
        }

        if ($n -le 0) {
            throw $EofMessage
        }
        $read += $n
    }
}

$script:SvrServiceProcessIdOverride = $null

function Get-SvrServiceProcessId {
    param([string]$ServiceName = "SelectiveVpnRouter")

    if ($null -ne $script:SvrServiceProcessIdOverride) {
        return & $script:SvrServiceProcessIdOverride -ServiceName $ServiceName
    }

    $svc = Get-CimInstance Win32_Service -Filter ("Name='" + $ServiceName + "'") -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        return $null
    }

    $processId = [int]$svc.ProcessId
    if ($processId -le 0) {
        return $null
    }

    return $processId
}

function Resolve-SvrServicePidBaselineForWatch {
    param(
        [string]$ServiceName = "SelectiveVpnRouter",
        [int]$TimeoutMs = 5000,
        [int]$PollIntervalMs = 200
    )

    if ($TimeoutMs -le 0) {
        $TimeoutMs = 5000
    }
    if ($PollIntervalMs -le 0) {
        $PollIntervalMs = 200
    }

    $deadline = [datetime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([datetime]::UtcNow -lt $deadline) {
        $processId = Get-SvrServiceProcessId -ServiceName $ServiceName
        if ($null -ne $processId -and [int]$processId -gt 0) {
            return [int]$processId
        }
        Start-Sleep -Milliseconds $PollIntervalMs
    }

    return $null
}

function Invoke-SvrIpcCore {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [string]$PayloadJson,
        [Parameter(Mandatory = $true)][datetime]$DeadlineUtc
    )

    $reqObj = @{
        version     = 1
        id          = [Guid]::NewGuid().ToString("N")
        method      = $Method
        payloadJson = $PayloadJson
    }
    $reqJson = ($reqObj | ConvertTo-Json -Compress)
    $body = [Text.Encoding]::UTF8.GetBytes($reqJson)

    $remainingMs = [Math]::Max(1, [int](($DeadlineUtc - [datetime]::UtcNow).TotalMilliseconds))
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
        ".", "SelectiveVpnRouter", [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None)
    try {
        $pipe.Connect($remainingMs)
        $header = [BitConverter]::GetBytes([int32]$body.Length)
        $pipe.Write($header, 0, 4)
        $pipe.Write($body, 0, $body.Length)
        $pipe.Flush()

        $headerBuf = New-Object byte[] 4
        Invoke-SvrReadExact -Stream $pipe -Buffer $headerBuf -Length 4 -EofMessage "IPC response header EOF" -DeadlineUtc $DeadlineUtc
        $len = [BitConverter]::ToInt32($headerBuf, 0)
        if ($len -le 0 -or $len -gt 4000000) { throw "Invalid IPC response length: $len" }

        $respBuf = New-Object byte[] $len
        Invoke-SvrReadExact -Stream $pipe -Buffer $respBuf -Length $len -EofMessage "IPC response body EOF" -DeadlineUtc $DeadlineUtc

        $respJson = [Text.Encoding]::UTF8.GetString($respBuf)
        Write-SvrUpdateLogLine "IPC $Method <= $respJson"
        return ($respJson | ConvertFrom-Json)
    }
    finally {
        $pipe.Dispose()
    }
}

$script:SvrIpcInvokeOverride = $null
$script:SvrIpcWithServiceWatchOverride = $null

function Get-SvrObjectPropertyInfo {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $prop = $Object.PSObject.Properties[$Name]
    if ($null -ne $prop) {
        return $prop
    }

    foreach ($candidate in $Object.PSObject.Properties) {
        if ($candidate.Name.Equals($Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $candidate
        }
    }

    return $null
}

function Get-SvrObjectPropertyValue {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string]$Name,
        $Default = $null
    )

    if ($null -eq $Object) {
        return $Default
    }

    $prop = Get-SvrObjectPropertyInfo -Object $Object -Name $Name
    if ($null -eq $prop) {
        return $Default
    }

    return $prop.Value
}

function Test-SvrObjectHasProperty {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $false
    }

    return ($null -ne (Get-SvrObjectPropertyInfo -Object $Object -Name $Name))
}

function Write-SvrIpcResponseShapeTelemetry {
    param(
        $Response,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($null -eq $Response) {
        Write-Host ("${Label}: null")
        return
    }

    $names = @($Response.PSObject.Properties | ForEach-Object { $_.Name })
    Write-Host ("${Label} type=" + $Response.GetType().FullName + " properties=" + ($names -join ','))
}

function Assert-SvrIpcResponseProtocol {
    param(
        $Response,
        [string]$Context = "IPC"
    )

    if ($null -eq $Response) {
        throw "${Context} response is null"
    }

    if (-not (Test-SvrObjectHasProperty -Object $Response -Name "Ok")) {
        throw "${Context} response missing required property: Ok"
    }
}

function ConvertTo-SvrIpcResponseView {
    param(
        $Response,
        [string]$Context = "IPC"
    )

    Assert-SvrIpcResponseProtocol -Response $Response -Context $Context

    $errorVal = Get-SvrObjectPropertyValue -Object $Response -Name "Error" -Default ""
    if ($null -eq $errorVal) { $errorVal = "" } else { $errorVal = [string]$errorVal }

    $payloadVal = Get-SvrObjectPropertyValue -Object $Response -Name "PayloadJson" -Default ""
    if ($null -eq $payloadVal) { $payloadVal = "" } else { $payloadVal = [string]$payloadVal }

    return [pscustomobject]@{
        Ok          = [bool](Get-SvrObjectPropertyValue -Object $Response -Name "Ok" -Default $false)
        Error       = $errorVal
        PayloadJson = $payloadVal
    }
}

function ConvertTo-SvrDiagnosticPayloadView {
    param(
        $PayloadObject,
        [string]$Context = "RunDiagnostic payload"
    )

    if ($null -eq $PayloadObject) {
        throw "${Context} is null"
    }

    if (-not (Test-SvrObjectHasProperty -Object $PayloadObject -Name "Outcome")) {
        throw "${Context} missing required property: Outcome"
    }

    $message = Get-SvrObjectPropertyValue -Object $PayloadObject -Name "Message" -Default ""
    if ($null -eq $message) { $message = "" } else { $message = [string]$message }

    return [pscustomobject]@{
        Outcome = [string](Get-SvrObjectPropertyValue -Object $PayloadObject -Name "Outcome")
        Message = $message
    }
}

function Get-SvrDiagnosticPayloadFromJson {
    param(
        [string]$PayloadJson,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ([string]::IsNullOrWhiteSpace($PayloadJson)) {
        throw "${Context}: PayloadJson is empty"
    }

    $doc = $PayloadJson | ConvertFrom-Json
    return ConvertTo-SvrDiagnosticPayloadView -PayloadObject $doc -Context $Context
}

function Get-SvrPowerShellPipelineOutputItems {
    param($Output)

    if ($null -eq $Output) {
        return ,@()
    }

    $typeName = $Output.GetType().FullName
    if ($typeName -like 'System.Management.Automation.PSDataCollection*') {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Output) {
            [void]$items.Add($item)
        }
        return ,$items.ToArray()
    }

    if ($Output -is [System.Array] -or ($Output -is [System.Collections.IList] -and $Output -isnot [string])) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Output) {
            [void]$items.Add($item)
        }
        return ,$items.ToArray()
    }

    # Comma prefix: PowerShell unwraps single-element arrays returned from functions.
    return ,@($Output)
}

function Get-SvrPowerShellPipelineOutputSingle {
    param(
        $Output,
        [Parameter(Mandatory = $true)][string]$EmptyMessage
    )

    $items = Get-SvrPowerShellPipelineOutputItems -Output $Output
    if ($items.Count -eq 0) {
        throw $EmptyMessage
    }

    return $items[0]
}

function Invoke-SvrIpc {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [string]$PayloadJson,
        [int]$TimeoutMs = 15000,
        [int]$WallClockTimeoutMs = 0
    )

    if ($null -ne $script:SvrIpcInvokeOverride) {
        $overrideRaw = & $script:SvrIpcInvokeOverride -Method $Method -PayloadJson $PayloadJson -TimeoutMs $TimeoutMs -WallClockTimeoutMs $WallClockTimeoutMs
        return ConvertTo-SvrIpcResponseView -Response $overrideRaw -Context "IPC method=$Method (mock)"
    }

    $effectiveMs = if ($WallClockTimeoutMs -gt 0) { $WallClockTimeoutMs } else { $TimeoutMs }
    $deadline = [datetime]::UtcNow.AddMilliseconds($effectiveMs)
    $raw = Invoke-SvrIpcCore -Method $Method -PayloadJson $PayloadJson -DeadlineUtc $deadline
    return ConvertTo-SvrIpcResponseView -Response $raw -Context "IPC method=$Method"
}

function Test-SvrIpcServiceWatchPoll {
    param(
        [Parameter(Mandatory = $true)][int]$BaselinePid
    )

    if ($BaselinePid -le 0) {
        return [pscustomobject]@{
            Abort   = $true
            Message = "Unable to determine service PID during diagnostic watch (invalid baselinePid=$BaselinePid)"
        }
    }

    $currentPid = Get-SvrServiceProcessId
    if ($null -eq $currentPid -or [int]$currentPid -le 0) {
        return [pscustomobject]@{
            Abort   = $true
            Message = "service stopped during IPC (baselinePid=$BaselinePid currentPid=unknown)"
        }
    }

    if ([int]$currentPid -ne $BaselinePid) {
        return [pscustomobject]@{
            Abort   = $true
            Message = "service restarted during IPC (baselinePid=$BaselinePid currentPid=$currentPid)"
        }
    }

    return [pscustomobject]@{ Abort = $false; Message = "" }
}

function Stop-SvrPowerShellAsyncPipeline {
    param(
        [Parameter(Mandatory = $true)][System.Management.Automation.PowerShell]$PowerShell,
        [Parameter(Mandatory = $true)][System.IAsyncResult]$AsyncResult
    )

    if ($AsyncResult.IsCompleted) {
        return
    }

    try {
        $PowerShell.Stop() | Out-Null
    }
    catch {
    }

    try {
        $null = $PowerShell.EndInvoke($AsyncResult)
    }
    catch [System.Management.Automation.PipelineStoppedException] {
    }
    catch {
    }
}

function Wait-SvrPowerShellAsyncResult {
    param(
        [Parameter(Mandatory = $true)][System.Management.Automation.PowerShell]$PowerShell,
        [Parameter(Mandatory = $true)][System.IAsyncResult]$AsyncResult,
        [Parameter(Mandatory = $true)][datetime]$DeadlineUtc,
        [scriptblock]$PollAbort = $null,
        $PollAbortArgument = $null,
        [int]$PollIntervalMs = 100
    )

    while (-not $AsyncResult.IsCompleted) {
        if ([datetime]::UtcNow -gt $DeadlineUtc) {
            Stop-SvrPowerShellAsyncPipeline -PowerShell $PowerShell -AsyncResult $AsyncResult
            throw "IPC wall-clock timeout (deadline exceeded)"
        }

        if ($null -ne $PollAbort) {
            if ($null -ne $PollAbortArgument) {
                $abortSignal = & $PollAbort $PollAbortArgument
            }
            else {
                $abortSignal = & $PollAbort
            }
            if ($null -ne $abortSignal -and $abortSignal.Abort) {
                Stop-SvrPowerShellAsyncPipeline -PowerShell $PowerShell -AsyncResult $AsyncResult
                throw [string]$abortSignal.Message
            }
        }

        Start-Sleep -Milliseconds $PollIntervalMs
    }

    $output = $PowerShell.EndInvoke($AsyncResult)
    if ($PowerShell.HadErrors) {
        $err = ($PowerShell.Streams.Error | ForEach-Object { $_.Exception.Message }) -join "; "
        throw $err
    }

    return $output
}

function Invoke-SvrIpcWithServiceWatch {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [string]$PayloadJson,
        [Parameter(Mandatory = $true)][int]$WallClockTimeoutMs,
        [Parameter(Mandatory = $true)][int]$ServicePidBaseline,
        [int]$ServicePidPollMs = 1000,
        [string]$HelpersRoot = $PSScriptRoot
    )

    if ($ServicePidBaseline -le 0) {
        throw "Unable to determine service PID before diagnostic (baselinePid=$ServicePidBaseline)"
    }

    if ($null -ne $script:SvrIpcWithServiceWatchOverride) {
        $overrideRaw = & $script:SvrIpcWithServiceWatchOverride -Method $Method -PayloadJson $PayloadJson -WallClockTimeoutMs $WallClockTimeoutMs -ServicePidBaseline $ServicePidBaseline -ServicePidPollMs $ServicePidPollMs -HelpersRoot $HelpersRoot
        $single = Get-SvrPowerShellPipelineOutputSingle -Output $overrideRaw -EmptyMessage "IPC mock returned no result for method=$Method"
        return ConvertTo-SvrIpcResponseView -Response $single -Context "IPC method=$Method (mock watch)"
    }

    $deadline = [datetime]::UtcNow.AddMilliseconds($WallClockTimeoutMs)
    $deadlineIso = $deadline.ToString("o")

    $rs = [runspacefactory]::CreateRunspace()
    $rs.Open()
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    $handle = $null
    try {
        [void]$ps.AddScript({
            param($Root, $M, $P, $DeadlineIso)
            . (Join-Path $Root "_common.ps1")
            . (Join-Path $Root "_update-helpers.ps1")
            $dl = [datetime]::Parse($DeadlineIso, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)
            Invoke-SvrIpcCore -Method $M -PayloadJson $P -DeadlineUtc $dl
        }).AddArgument($HelpersRoot).AddArgument($Method).AddArgument($PayloadJson).AddArgument($deadlineIso)

        $handle = $ps.BeginInvoke()
        $pollAbort = {
            param([int]$BaselinePid)
            Test-SvrIpcServiceWatchPoll -BaselinePid $BaselinePid
        }

        $result = Wait-SvrPowerShellAsyncResult `
            -PowerShell $ps `
            -AsyncResult $handle `
            -DeadlineUtc $deadline `
            -PollAbort $pollAbort `
            -PollAbortArgument $ServicePidBaseline `
            -PollIntervalMs $ServicePidPollMs

        $single = Get-SvrPowerShellPipelineOutputSingle -Output $result -EmptyMessage "IPC returned no result for method=$Method"
        return ConvertTo-SvrIpcResponseView -Response $single -Context "IPC method=$Method"
    }
    catch {
        if ($_.Exception.Message -match 'deadline exceeded') {
            throw "IPC wall-clock timeout ${WallClockTimeoutMs}ms method=$Method"
        }
        throw
    }
    finally {
        if ($null -ne $handle) {
            Stop-SvrPowerShellAsyncPipeline -PowerShell $ps -AsyncResult $handle
        }
        $ps.Dispose()
        $rs.Close()
    }
}