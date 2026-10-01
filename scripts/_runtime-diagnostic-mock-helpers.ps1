function New-SvrIpcEnvelope {
    param(
        [bool]$Ok = $true,
        [string]$PayloadJson = "",
        [string]$Error = ""
    )

    return [pscustomobject]@{
        Id          = "mock"
        Ok          = $Ok
        PayloadJson = $PayloadJson
        Error       = $Error
    }
}

function New-SvrSparseIpcEnvelope {
    param(
        [bool]$Ok = $true,
        [string]$PayloadJson = "",
        [switch]$IncludeError,
        [string]$Error = "",
        [switch]$IncludePayloadJson
    )

    $props = [ordered]@{ Ok = $Ok }
    if ($IncludePayloadJson -or -not [string]::IsNullOrEmpty($PayloadJson)) {
        $props['PayloadJson'] = $PayloadJson
    }
    if ($IncludeError) {
        $props['Error'] = $Error
    }

    return [pscustomobject]$props
}

function New-SvrMockGetStatusPayload {
    param(
        [bool]$VpnConnected = $true,
        [bool]$DriverLoaded = $true,
        [bool]$WfpPolicyHealthy = $true
    )

    $snap = @{
        Vpn          = @{ Connected = $VpnConnected }
        DriverLoaded = $DriverLoaded
        WfpPolicy    = @{ PolicyHealthy = $WfpPolicyHealthy }
    }
    return ($snap | ConvertTo-Json -Compress)
}

function New-SvrMockDiagnosticResultPayload {
    param(
        [string]$Outcome = "Pass",
        [string]$Message = "mock diagnostic pass",
        [switch]$SparseMessage
    )

    $body = [ordered]@{ Outcome = $Outcome }
    if (-not $SparseMessage) {
        $body['Message'] = $Message
    }
    return ($body | ConvertTo-Json -Compress)
}

function Register-SvrRuntimeDiagnosticMockHappyPath {
    param(
        [string[]]$DiagnosticNames = @('wfp-runtime-appid-blob', 'wfp-runtime-appid-case')
    )

    $script:MockDiagnosticQueue = New-Object System.Collections.Queue
    foreach ($n in $DiagnosticNames) {
        $script:MockDiagnosticQueue.Enqueue($n)
    }

    $script:SvrIpcInvokeOverride = {
        param(
            [Parameter(Mandatory = $true)][string]$Method,
            [string]$PayloadJson,
            [int]$TimeoutMs = 15000,
            [int]$WallClockTimeoutMs = 0
        )

        if ($Method -ne 'GetStatus') {
            throw "Unexpected Invoke-SvrIpc method in mock: $Method"
        }

        return New-SvrSparseIpcEnvelope -Ok $true -PayloadJson (New-SvrMockGetStatusPayload)
    }

    $script:SvrIpcWithServiceWatchOverride = {
        param(
            [Parameter(Mandatory = $true)][string]$Method,
            [string]$PayloadJson,
            [Parameter(Mandatory = $true)][int]$WallClockTimeoutMs,
            [Parameter(Mandatory = $true)][int]$ServicePidBaseline,
            [int]$ServicePidPollMs = 1000,
            [string]$HelpersRoot = $PSScriptRoot
        )

        if ($Method -ne 'RunDiagnostic') {
            throw "Unexpected watch method in mock: $Method"
        }

        $name = 'mock-diagnostic'
        if (-not [string]::IsNullOrWhiteSpace($PayloadJson)) {
            $doc = $PayloadJson | ConvertFrom-Json
            if ($doc.name) { $name = [string]$doc.name }
        }

        $payload = New-SvrMockDiagnosticResultPayload -Outcome 'Pass' -Message ("mock pass for " + $name) -SparseMessage
        return New-SvrSparseIpcEnvelope -Ok $true -PayloadJson $payload
    }
}

function Clear-SvrRuntimeDiagnosticMockOverrides {
    $script:SvrIpcInvokeOverride = $null
    $script:SvrIpcWithServiceWatchOverride = $null
    $script:MockDiagnosticQueue = $null
}
