try {
    Add-Line "run-runtime-diagnostic: waiting for IPC..."
    $pipeExistsTelemetry = Test-SvrNamedPipeExists
    Add-Line ("telemetry: pipeExistsTelemetry=$pipeExistsTelemetry")

    try {
        $svc = Get-Service -Name $serviceName -ErrorAction Stop
        $serviceStatus = $svc.Status.ToString()
        $svcCim = Get-CimInstance Win32_Service -Filter ("Name='" + $serviceName + "'") -ErrorAction SilentlyContinue
        if ($null -ne $svcCim -and $svcCim.ProcessId) {
            $servicePid = [string]$svcCim.ProcessId
        }
    }
    catch {
        $serviceStatus = "unavailable:" + $_.Exception.Message
    }

    Add-Line ("telemetry: serviceName=$serviceName serviceStatus=$serviceStatus servicePid=$servicePid")

    $ipcReadyResult = Wait-SvrIpcGetStatusReady `
        -TimeoutSeconds $IpcReadinessSeconds `
        -PollIntervalMs $PollIntervalMs `
        -StatusTimeoutMs $IpcAttemptTimeoutMs
    $ipcAttempts = $ipcReadyResult.Attempts
    $lastIpcError = [string]$ipcReadyResult.LastError
    if (-not $ipcReadyResult.Ready) {
        if ($ipcReadyResult.NonRetryable) {
            $failureCategory = "RunnerDependency"
            $lastError = $lastIpcError
            if ($ipcReadyResult.ErrorRecord) {
                $failureExceptionType = $ipcReadyResult.ErrorRecord.Exception.GetType().FullName
                $failureExceptionMessage = $ipcReadyResult.ErrorRecord.Exception.ToString()
                if ($ipcReadyResult.ErrorRecord.InvocationInfo) {
                    $failurePositionMessage = $ipcReadyResult.ErrorRecord.InvocationInfo.PositionMessage
                }
                if ($ipcReadyResult.ErrorRecord.ScriptStackTrace) {
                    $failureScriptStackTrace = $ipcReadyResult.ErrorRecord.ScriptStackTrace
                }
            }
            else {
                $failureExceptionType = "NonRetryableIpcReadinessError"
                $failureExceptionMessage = $lastIpcError
            }
        }
        else {
            $lastError = "IPC not available after ${IpcReadinessSeconds}s. Last error: $lastIpcError"
        }
        Set-SvrRunnerExitCode -Value 3
        throw $lastError
    }

    $ipcReady = $true
    Add-Line ("IPC ready after $ipcAttempts attempt(s).")

    $statusResp = $ipcReadyResult.StatusResponse
    $snap = $statusResp.PayloadJson | ConvertFrom-Json
    $vpnConnected = [bool]$snap.Vpn.Connected
    $driverLoaded = [bool]$snap.DriverLoaded
    if ($null -ne $snap.WfpPolicy) {
        $wfpPolicyHealthy = [bool]$snap.WfpPolicy.PolicyHealthy
    }

    if (-not $vpnConnected) {
        Add-Line ("run-runtime-diagnostic: waiting up to ${WaitConnectedSeconds}s for VPN Connected...")
        $connectedDeadline = (Get-Date).AddSeconds($WaitConnectedSeconds)
        while (-not $vpnConnected) {
            if ((Get-Date) -ge $connectedDeadline) {
                $lastError = "VPN Route not Connected after ${WaitConnectedSeconds}s (connect manually in the app)."
                Set-SvrRunnerExitCode -Value 1
                throw $lastError
            }

            Start-Sleep -Milliseconds $PollIntervalMs
            $statusResp = Invoke-SvrIpc -Method "GetStatus" -TimeoutMs $StatusTimeoutMs
            if (-not $statusResp.Ok) {
                $lastIpcError = if ([string]::IsNullOrWhiteSpace($statusResp.Error)) { "GetStatus Ok=false" } else { $statusResp.Error }
                continue
            }

            $snap = $statusResp.PayloadJson | ConvertFrom-Json
            $vpnConnected = [bool]$snap.Vpn.Connected
            $driverLoaded = [bool]$snap.DriverLoaded
            if ($null -ne $snap.WfpPolicy) {
                $wfpPolicyHealthy = [bool]$snap.WfpPolicy.PolicyHealthy
            }
        }
    }

    Add-Line ("VPN Connected=true DriverLoaded=$driverLoaded WfpPolicyHealthy=$wfpPolicyHealthy")

    if (-not $driverLoaded) {
        $lastError = "Callout driver not loaded (DriverLoaded=false)."
        Set-SvrRunnerExitCode -Value 2
        throw $lastError
    }

    foreach ($name in $DiagnosticNames) {
        if ([string]::IsNullOrWhiteSpace($name)) { continue }

        Add-Line ""
        Add-Line ("--- RunDiagnostic: $name ---")
        $payloadObj = @{ name = $name.Trim() }
        if ($name.Trim() -match 'telegram|normalization-matrix' -and -not [string]::IsNullOrWhiteSpace($Script:SvrRegressionTelegramExePath) -and (Test-Path -LiteralPath $Script:SvrRegressionTelegramExePath)) {
            $payloadObj.exePath = $Script:SvrRegressionTelegramExePath
        }
        $payload = ($payloadObj | ConvertTo-Json -Compress)
        $entry = [ordered]@{
            Name        = $name.Trim()
            IpcOk       = $false
            IpcError    = ""
            Outcome     = ""
            Message     = ""
            PayloadJson = ""
        }

        $activeDiagnostic = $name.Trim()
        $activeDiagnosticState = "STARTED"
        $activeDiagnosticStartedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
        $activeDiagnosticEndedAt = ""
        $servicePidAtDiagnosticStart = [string](Get-SvrServiceProcessId)
        if ($servicePidAtDiagnosticStart) { $servicePid = $servicePidAtDiagnosticStart }
        Flush-Report -FinalExitCode (Get-SvrRunnerExitCode)

        try {
            $baselinePid = 0
            if ($servicePidAtDiagnosticStart -match '^\d+$') { $baselinePid = [int]$servicePidAtDiagnosticStart }
            $resp = Invoke-SvrIpcWithServiceWatch `
                -Method "RunDiagnostic" `
                -PayloadJson $payload `
                -WallClockTimeoutMs $DiagnosticTimeoutMs `
                -ServicePidBaseline $baselinePid `
                -ServicePidPollMs $DiagnosticServicePollMs `
                -HelpersRoot $PSScriptRoot
            $entry.IpcOk = [bool]$resp.Ok
            $entry.IpcError = [string]$resp.Error
            $entry.PayloadJson = [string]$resp.PayloadJson

            if (-not $resp.Ok) {
                $lastError = "RunDiagnostic $name failed: $($entry.IpcError)"
                Set-SvrRunnerExitCode -Value 3
                $diagnosticResults.Add([pscustomobject]$entry)
                throw $lastError
            }

            $diagPayload = Get-SvrDiagnosticPayloadFromJson -PayloadJson $entry.PayloadJson -Context "RunDiagnostic $name payload"
            $entry.Outcome = $diagPayload.Outcome
            $entry.Message = $diagPayload.Message
            $diagnosticResults.Add([pscustomobject]$entry)
            $activeDiagnosticState = "COMPLETED"
            $activeDiagnosticEndedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
            Flush-Report -FinalExitCode (Get-SvrRunnerExitCode)

            Add-Line ("Outcome: $($entry.Outcome)")
            if ($null -ne $entry.Message) { Add-Line ([string]$entry.Message) }
        }
        catch {
            Set-SvrRunnerExitCode -Value 3
            $activeDiagnosticState = "FAILED"
            $activeDiagnosticEndedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
            if ($_.Exception.Message -match "service restarted") { Set-SvrRunnerScriptField -Name 'servicePidChangedDuringDiagnostic' -Value $true }
            if (-not $entry.IpcError) { $entry.IpcError = $_.Exception.Message }
            Flush-Report -FinalExitCode (Get-SvrRunnerExitCode)
            $prior = $diagnosticResults | Where-Object { $_.Name -eq $entry.Name }
            if (-not $prior) { $diagnosticResults.Add([pscustomobject]$entry) }
            throw
        }
    }

    Set-SvrRunnerExitCode -Value 0
    Add-Line ""
    Add-Line "run-runtime-diagnostic: all diagnostics completed."
}
catch {
    if (-not (Get-SvrRunnerScriptField -Name 'lastError')) {
        Set-SvrRunnerScriptField -Name 'lastError' -Value $_.Exception.Message
    }
    if (-not (Get-SvrRunnerScriptField -Name 'failureCategory')) {
        if ((Get-SvrRunnerScriptField -Name 'lastError') -match 'Required helper .+ is not loaded' -or $_.Exception.Message -match 'is not recognized as the name of a cmdlet') {
            Set-SvrRunnerScriptField -Name 'failureCategory' -Value 'RunnerDependency'
        }
        else {
            Set-SvrRunnerScriptField -Name 'failureCategory' -Value 'RunnerInfrastructure'
        }
    }
    Set-SvrRunnerScriptField -Name 'failureExceptionType' -Value $_.Exception.GetType().FullName
    Set-SvrRunnerScriptField -Name 'failureExceptionMessage' -Value $_.Exception.ToString()
    if ($_.InvocationInfo) {
        Set-SvrRunnerScriptField -Name 'failurePositionMessage' -Value $_.InvocationInfo.PositionMessage
    }
    if ($_.ScriptStackTrace) {
        Set-SvrRunnerScriptField -Name 'failureScriptStackTrace' -Value $_.ScriptStackTrace
    }
    Flush-Report -FinalExitCode (Get-SvrRunnerExitCode)
    Add-Line ("ERROR: " + $lastError)
}
finally {
    Save-Report -FinalExitCode (Get-SvrRunnerExitCode)
}