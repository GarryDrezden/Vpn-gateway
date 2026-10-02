function Get-VpnLifecycleAcceptanceDiagnosticNames {
    @(
        'vpn-resource-health',
        'vpn-connection-routing-smoke',
        'vpn-lifecycle-reconnect-stress',
        'vpn-lifecycle-cleanup-check',
        'vpn-resource-health'
    )
}

function Test-SvrVpnLifecycleDiagnosticOutcomeAcceptable {
    param([Parameter(Mandatory = $true)][string]$Outcome)
    return ($Outcome -eq 'Pass')
}
