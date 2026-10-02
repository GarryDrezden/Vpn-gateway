$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "_multi-app-routing-acceptance.ps1")

function Assert-True { param([bool]$C, [string]$M) if (-not $C) { throw $M } }

$passMsg = New-SvrMockMultiAppRoutingPassMessage
Test-SvrMultiAppRoutingReportShape -Message $passMsg
Assert-True (Test-SvrMultiAppRoutingMessageIndicatesPass -Message $passMsg) 'pass marker'
Assert-True (Test-SvrMultiAppRoutingDiagnosticOutcomeAcceptable -Outcome 'Pass' -Message $passMsg) 'acceptable pass'

$failB = New-SvrMockMultiAppRoutingFailMessage -Scenario 'B-Direct'
Assert-True (-not (Test-SvrMultiAppRoutingMessageIndicatesPass -Message $failB)) 'B direct fail marker'
Assert-True ($failB -match 'actual DIRECT') 'B direct row'

$failC = New-SvrMockMultiAppRoutingFailMessage -Scenario 'C-Proxy'
Assert-True ($failC -match 'directViaProxy=true') 'C proxy flag'
Assert-True (-not (Test-SvrMultiAppRoutingDiagnosticOutcomeAcceptable -Outcome 'Pass' -Message $failC)) 'C proxy not acceptable'

$realOnlyBody = New-SvrMockMultiAppRoutingAcceptanceBody -RealAppSpotCheck 'FAIL'
$realOnly = 'Multi-app isolation verified.' + [Environment]::NewLine + $realOnlyBody + [Environment]::NewLine + (Get-SvrMultiAppRoutingRegressionPassMarker)
Assert-True ($realOnly -match 'realAppSpotCheck=FAIL') 'real app fail line present'
Assert-True (Test-SvrMultiAppRoutingMessageIndicatesPass -Message $realOnly) 'real-only does not fail synthetic marker'

$passNoTraffic = New-SvrMockMultiAppRoutingPassMessage
Assert-True ($passNoTraffic -match 'realAppSpotCheck=OBSERVED-NO-TRAFFIC') 'observed no traffic ok'

$cross = New-SvrMockMultiAppRoutingFailMessage -Scenario 'CrossAttribution'
Assert-True ($cross -match 'crossAttribution=true') 'cross attribution scenario'

Assert-True (-not (Test-SvrMultiAppRoutingDiagnosticOutcomeAcceptable -Outcome 'Fail' -Message $passMsg)) 'Fail outcome'

Write-Host 'PASS test-multi-app-routing-report-parse'
exit 0
