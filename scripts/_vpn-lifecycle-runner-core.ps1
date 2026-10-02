# VPN lifecycle runner core (uses RunDiagnostic IPC).

. (Join-Path $PSScriptRoot "_vpn-lifecycle-acceptance.ps1")

$diagnosticResults = New-Object System.Collections.Generic.List[object]
$lastError = ""
$acceptanceOk = $true

try {
    Add-Line "run-vpn-lifecycle-diagnostic: waiting for IPC..."
    $pipeExistsTelemetry = Test-SvrNamedPipeExists
    Add-Line ("telemetry: pipeExistsTelemetry=$pipeExistsTelemetry")

    $serviceName = Get-SvrRunnerScriptField -Name 'serviceName' -Default 'SelectiveVpnRouter'
    $serviceStatus = ""
    $servicePid = ""
    try {
        $svc = Get-Service -Name $serviceName -ErrorAction Stop
        $serviceStatus = $svc.Status.ToString()
        $svcCim = Get-CimInstance Win32_Service -Filter ("Name='" + $serviceName + "'") -ErrorAction SilentlyContinue
        if ($null -ne $svcCim -and [int]$svcCim.ProcessId -gt 0) {
            $servicePid = [string][int]$svcCim.ProcessId
        }
    }
    catch {
        $serviceStatus = "unavailable:" + $_.Exception.Message
    }

    Add-Line ("telemetry: serviceName=$serviceName serviceStatus=$serviceStatus servicePid=$servicePid")
    Set-SvrRunnerScriptField -Name 'serviceStatus' -Value $serviceStatus
    if ($servicePid) { Set-SvrRunnerScriptField -Name 'servicePid' -Value $servicePid }

    $ipcReadyResult = Wait-SvrIpcGetStatusReady `
        -TimeoutSeconds $IpcReadinessSeconds `
        -PollIntervalMs $PollIntervalMs `
        -StatusTimeoutMs $IpcAttemptTimeoutMs
    if (-not $ipcReadyResult.Ready) {
        Set-SvrRunnerExitCode -Value 3
        throw $ipcReadyResult.LastError
    }

    Set-SvrRunnerScriptField -Name 'ipcReady' -Value $true
    Add-Line ("IPC ready after $($ipcReadyResult.Attempts) attempt(s).")

    $statusResp = $ipcReadyResult.StatusResponse
    $snap = $statusResp.PayloadJson | ConvertFrom-Json
    $vpnRoutingReady = $false
    if ($null -ne $snap.PSObject.Properties['VpnRoutingReady']) {
        $vpnRoutingReady = [bool]$snap.VpnRoutingReady
    }
    elseif ($null -ne $snap.Vpn) {
        $vpnRoutingReady = [bool]$snap.Vpn.Connected
    }

    if (-not $vpnRoutingReady) {
        Add-Line ("run-vpn-lifecycle-diagnostic: waiting up to ${WaitConnectedSeconds}s for VpnRoutingReady...")
        $connectedDeadline = (Get-Date).AddSeconds($WaitConnectedSeconds)
        while (-not $vpnRoutingReady) {
            if ((Get-Date) -ge $connectedDeadline) {
                Set-SvrRunnerExitCode -Value 1
                throw "VPN Route not VpnRoutingReady after ${WaitConnectedSeconds}s (connect manually in the app)."
            }

            Start-Sleep -Milliseconds $PollIntervalMs
            $statusResp = Invoke-SvrIpc -Method "GetStatus" -TimeoutMs $StatusTimeoutMs
            if (-not $statusResp.Ok) { continue }
            $snap = $statusResp.PayloadJson | ConvertFrom-Json
            if ($null -ne $snap.PSObject.Properties['VpnRoutingReady']) {
                $vpnRoutingReady = [bool]$snap.VpnRoutingReady
            }
            elseif ($null -ne $snap.Vpn) {
                $vpnRoutingReady = [bool]$snap.Vpn.Connected
            }
        }
    }

    Add-Line ("VpnRoutingReady=true before acceptance sequence.")

    $resourceHealthPass = 0
    foreach ($name in $DiagnosticNames) {
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $trimmed = $name.Trim()
        $label = $trimmed
        if ($trimmed -eq 'vpn-resource-health') {
            $resourceHealthPass++
            $phase = if ($resourceHealthPass -eq 1) { 'initial-connected' } else { 'final-disconnected' }
            $label = "$trimmed ($phase)"
        }

        Add-Line ""
        Add-Line "--- RunDiagnostic: $label ---"
        $payloadObj = @{ name = $trimmed }
        if ($trimmed -eq 'vpn-lifecycle-reconnect-stress') {
            $payloadObj.confirm = $true
        }
        $payload = ($payloadObj | ConvertTo-Json -Compress)

        Set-SvrRunnerScriptField -Name 'activeDiagnostic' -Value $label
        Set-SvrRunnerScriptField -Name 'activeDiagnosticState' -Value 'STARTED'
        Set-SvrRunnerScriptField -Name 'activeDiagnosticStartedAt' -Value (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
        Set-SvrRunnerScriptField -Name 'activeDiagnosticEndedAt' -Value ""

        $baselinePid = Resolve-SvrServicePidBaselineForWatch -ServiceName $serviceName -TimeoutMs 5000 -PollIntervalMs 200
        if ($null -eq $baselinePid -or [int]$baselinePid -le 0) {
            Set-SvrRunnerExitCode -Value 3
            throw "Unable to determine service PID before diagnostic"
        }

        $servicePidAtDiagnosticStart = [string][int]$baselinePid
        Set-SvrRunnerScriptField -Name 'servicePidAtDiagnosticStart' -Value $servicePidAtDiagnosticStart
        Set-SvrRunnerScriptField -Name 'servicePid' -Value $servicePidAtDiagnosticStart
        Flush-Report -FinalExitCode (Get-SvrRunnerExitCode)

        $resp = Invoke-SvrIpcWithServiceWatch `
            -Method "RunDiagnostic" `
            -PayloadJson $payload `
            -WallClockTimeoutMs $DiagnosticTimeoutMs `
            -ServicePidBaseline $baselinePid `
            -ServicePidPollMs $DiagnosticServicePollMs `
            -HelpersRoot $PSScriptRoot
        if (-not $resp.Ok) {
            Set-SvrRunnerExitCode -Value 3
            throw ("RunDiagnostic failed: " + $resp.Error)
        }
        $view = Get-SvrDiagnosticPayloadFromJson -PayloadJson $resp.PayloadJson -Context "RunDiagnostic $name"
        Add-Line ("Outcome: " + $view.Outcome)
        Add-Line $view.Message
        Set-SvrRunnerScriptField -Name 'activeDiagnosticState' -Value 'COMPLETED'
        Set-SvrRunnerScriptField -Name 'activeDiagnosticEndedAt' -Value (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
        if (-not (Test-SvrVpnLifecycleDiagnosticOutcomeAcceptable -Outcome $view.Outcome)) {
            $acceptanceOk = $false
            Set-SvrRunnerExitCode -Value 3
            if ($view.Outcome -eq 'Warn') {
                throw ("Diagnostic returned Warn (not acceptable for lifecycle regression): " + $label)
            }
        }
    }

    if ($acceptanceOk) {
        Set-SvrRunnerExitCode -Value 0
    }

    Add-Line "run-vpn-lifecycle-diagnostic: completed."
}
catch {
    $lastError = $_.Exception.Message
    Add-Line ("ERROR: " + $lastError)
    if ($lastError -match 'service restarted') {
        Set-SvrRunnerScriptField -Name 'servicePidChangedDuringDiagnostic' -Value $true
    }
    if ((Get-SvrRunnerExitCode) -eq 0) { Set-SvrRunnerExitCode -Value 3 }
}
finally {
    Save-Report -FinalExitCode (Get-SvrRunnerExitCode)
}
