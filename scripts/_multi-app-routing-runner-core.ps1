. (Join-Path $PSScriptRoot "_multi-app-routing-acceptance.ps1")

$lastError = ""
$acceptanceOk = $true
$diagName = Get-MultiAppRoutingDiagnosticName

try {
    Add-Line "run-multi-app-routing-diagnostic: waiting for IPC..."
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
    $driverLoaded = $false
    if ($null -ne $snap.PSObject.Properties['VpnRoutingReady']) {
        $vpnRoutingReady = [bool]$snap.VpnRoutingReady
    }
    elseif ($null -ne $snap.Vpn) {
        $vpnRoutingReady = [bool]$snap.Vpn.Connected
    }
    if ($null -ne $snap.PSObject.Properties['DriverLoaded']) {
        $driverLoaded = [bool]$snap.DriverLoaded
    }

    if (-not $vpnRoutingReady) {
        Add-Line ("run-multi-app-routing-diagnostic: waiting up to ${WaitConnectedSeconds}s for VpnRoutingReady...")
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
            if ($null -ne $snap.PSObject.Properties['DriverLoaded']) {
                $driverLoaded = [bool]$snap.DriverLoaded
            }
        }
    }

    Add-Line ("VpnRoutingReady=true DriverLoaded=$driverLoaded before multi-app diagnostic.")

    if (-not $driverLoaded) {
        Set-SvrRunnerExitCode -Value 2
        throw "Callout driver not loaded (DriverLoaded=false)."
    }

    Add-Line ""
    Add-Line "--- RunDiagnostic: $diagName ---"
    $payload = (@{ name = $diagName } | ConvertTo-Json -Compress)

    Set-SvrRunnerScriptField -Name 'activeDiagnostic' -Value $diagName
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

    $view = Get-SvrDiagnosticPayloadFromJson -PayloadJson $resp.PayloadJson -Context "RunDiagnostic $diagName"
    Add-Line ("Outcome: " + $view.Outcome)
    Add-Line $view.Message
    Set-SvrRunnerScriptField -Name 'activeDiagnosticState' -Value 'COMPLETED'
    Set-SvrRunnerScriptField -Name 'activeDiagnosticEndedAt' -Value (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")

    if (-not (Test-SvrMultiAppRoutingDiagnosticOutcomeAcceptable -Outcome $view.Outcome -Message $view.Message)) {
        $acceptanceOk = $false
        Set-SvrRunnerExitCode -Value 3
        throw "Multi-app routing regression not acceptable (Outcome=$($view.Outcome))."
    }

    if ($acceptanceOk) {
        Set-SvrRunnerExitCode -Value 0
    }

    Add-Line "run-multi-app-routing-diagnostic: completed."
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
