function Get-MultiAppRoutingDiagnosticName {
    'multi-app-routing-isolation'
}

function Get-SvrMultiAppRoutingRegressionPassMarker {
    'MULTI-APP ROUTING REGRESSION: PASS'
}

function Get-SvrMultiAppRoutingRegressionFailMarker {
    'MULTI-APP ROUTING REGRESSION: FAIL'
}

function New-SvrMockMultiAppRoutingAcceptanceBody {
    param(
        [string]$AppBActual = 'VPN',
        [string]$AppCActual = 'DIRECT',
        [bool]$DirectViaProxy = $false,
        [bool]$CrossAttribution = $false,
        [bool]$SimultaneousIsolation = $true,
        [bool]$VpnAppsViaProxy = $true,
        [bool]$DiagnosticCleanupOk = $true,
        [bool]$AbIsolation = $true,
        [bool]$AcIsolation = $true,
        [bool]$AbcIsolation = $true,
        [bool]$MultiFlowBurstOk = $true,
        [string]$RealAppSpotCheck = 'OBSERVED-NO-TRAFFIC'
    )

    $rows = @(
        "App C | expected DIRECT | actual DIRECT | PASS",
        "App A | expected VPN | actual VPN | PASS",
        ("App B | expected VPN | actual {0} | {1}" -f $AppBActual, ($(if ($AppBActual -eq 'VPN') { 'PASS' } else { 'FAIL' })))
    )
    if ($AppCActual -ne 'DIRECT') {
        $rows[0] = ("App C | expected DIRECT | actual {0} | FAIL" -f $AppCActual)
    }
    $flags = @(
        ("abIsolation={0}" -f ([string]$AbIsolation).ToLower()),
        ("acIsolation={0}" -f ([string]$AcIsolation).ToLower()),
        ("abcIsolation={0}" -f ([string]$AbcIsolation).ToLower()),
        ("multiFlowBurstOk={0}" -f ([string]$MultiFlowBurstOk).ToLower()),
        ("simultaneousIsolation={0}" -f ([string]$SimultaneousIsolation).ToLower()),
        ("crossAttribution={0}" -f ([string]$CrossAttribution).ToLower()),
        ("directViaProxy={0}" -f ([string]$DirectViaProxy).ToLower()),
        ("diagnosticCleanupOk={0}" -f ([string]$DiagnosticCleanupOk).ToLower()),
        ("vpnAppsViaProxy={0}" -f ([string]$VpnAppsViaProxy).ToLower())
    )
    $summary = @(
        '--- acceptance (synthetic A/B/C) ---',
        ($rows -join [Environment]::NewLine),
        'configuredPermanentVpnApps=1',
        'installedAppFilters=3',
        'postRunObservedProbeVpnPaths=2',
        'postRunObservedProbeDirectPaths=1',
        "realAppSpotCheck=$RealAppSpotCheck"
    ) + $flags
    return ($summary -join [Environment]::NewLine)
}

function New-SvrMockMultiAppRoutingPassMessage {
    param([string]$Prefix = "Multi-app isolation verified.`n")
    $body = New-SvrMockMultiAppRoutingAcceptanceBody
    return $Prefix + $body + [Environment]::NewLine + (Get-SvrMultiAppRoutingRegressionPassMarker)
}

function New-SvrMockMultiAppRoutingFailMessage {
    param(
        [ValidateSet('B-Direct', 'C-Proxy', 'Generic', 'CrossAttribution', 'RealAppOnly')]
        [string]$Scenario = 'Generic'
    )
    switch ($Scenario) {
        'B-Direct' {
            $body = New-SvrMockMultiAppRoutingAcceptanceBody -AppBActual 'DIRECT' -VpnAppsViaProxy $false
        }
        'C-Proxy' {
            $body = New-SvrMockMultiAppRoutingAcceptanceBody -AppCActual 'VPN' -DirectViaProxy $true
        }
        'CrossAttribution' {
            $body = New-SvrMockMultiAppRoutingAcceptanceBody -CrossAttribution $true
        }
        'RealAppOnly' {
            $body = New-SvrMockMultiAppRoutingAcceptanceBody -RealAppSpotCheck 'FAIL'
        }
        default {
            $body = New-SvrMockMultiAppRoutingAcceptanceBody -SimultaneousIsolation $false -MultiFlowBurstOk $false
        }
    }
    return ("Multi-app isolation failed.`n" + $body + [Environment]::NewLine + (Get-SvrMultiAppRoutingRegressionFailMarker))
}

function Test-SvrMultiAppRoutingMessageIndicatesPass {
    param([AllowNull()][string]$Message)
    if ([string]::IsNullOrWhiteSpace($Message)) { return $false }
    return ($Message -match [regex]::Escape((Get-SvrMultiAppRoutingRegressionPassMarker)))
}

function Test-SvrMultiAppRoutingDiagnosticOutcomeAcceptable {
    param(
        [Parameter(Mandatory = $true)][string]$Outcome,
        [AllowNull()][string]$Message
    )
    if ($Outcome -ne 'Pass') { return $false }
    return (Test-SvrMultiAppRoutingMessageIndicatesPass -Message $Message)
}

function Test-SvrMultiAppRoutingReportShape {
    param([Parameter(Mandatory = $true)][string]$Message)

    $required = @(
        '--- acceptance (synthetic A/B/C) ---',
        'App A | expected VPN | actual VPN | PASS',
        'App B | expected VPN',
        'App C | expected DIRECT',
        'abIsolation=',
        'acIsolation=',
        'abcIsolation=',
        'multiFlowBurstOk=',
        'simultaneousIsolation=',
        'crossAttribution=',
        'directViaProxy=',
        'vpnAppsViaProxy=',
        'configuredPermanentVpnApps=',
        'postRunObservedProbeVpnPaths=',
        'realAppSpotCheck='
    )
    foreach ($token in $required) {
        if ($Message -notmatch [regex]::Escape($token)) {
            throw "Report shape missing token: $token"
        }
    }
}
