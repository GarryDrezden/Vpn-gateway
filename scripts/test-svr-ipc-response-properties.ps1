$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "_update-helpers.ps1")

function Assert-Throws {
    param([scriptblock]$Script, [string]$Pattern, [string]$Label)
    $threw = $false
    try { & $Script } catch { $threw = $true; if ($_.Exception.Message -notmatch $Pattern) { throw ("${Label}: wrong message: " + $_.Exception.Message) } }
    if (-not $threw) { throw "${Label}: expected throw" }
}

function Assert-Equal {
    param($E, $A, [string]$Label)
    if ($E -ne $A) { throw "${Label} expected=[$E] actual=[$A]" }
}

# A: Ok + PayloadJson, Error absent
$a = [pscustomobject]@{ Ok = $true; PayloadJson = '{"x":1}' }
$viewA = ConvertTo-SvrIpcResponseView -Response $a -Context 'A'
Assert-Equal $true $viewA.Ok 'A Ok'
Assert-Equal '' $viewA.Error 'A Error default'
Assert-Equal '{"x":1}' $viewA.PayloadJson 'A PayloadJson'

# B: Ok only
$b = [pscustomobject]@{ Ok = $true }
$viewB = ConvertTo-SvrIpcResponseView -Response $b -Context 'B'
Assert-Equal '' $viewB.PayloadJson 'B PayloadJson default'

# C: Ok false + Error, no PayloadJson
$c = [pscustomobject]@{ Ok = $false; Error = 'boom' }
$viewC = ConvertTo-SvrIpcResponseView -Response $c -Context 'C'
Assert-Equal $false $viewC.Ok 'C Ok'
Assert-Equal 'boom' $viewC.Error 'C Error'

# D/E/F protocol failures
Assert-Throws { ConvertTo-SvrIpcResponseView -Response ([pscustomobject]@{ Error = 'boom' }) -Context 'D' } 'missing required property: Ok' 'D'
Assert-Throws { ConvertTo-SvrIpcResponseView -Response ([pscustomobject]@{}) -Context 'E' } 'missing required property: Ok' 'E'
Assert-Throws { ConvertTo-SvrIpcResponseView -Response $null -Context 'F' } 'response is null' 'F'

# G/H/I/J optional null/empty values
$g = [pscustomobject]@{ Ok = $true; Error = $null; PayloadJson = $null }
$viewG = ConvertTo-SvrIpcResponseView -Response $g -Context 'G'
Assert-Equal '' $viewG.Error 'G Error null'
Assert-Equal '' $viewG.PayloadJson 'G PayloadJson null'

$h = [pscustomobject]@{ Ok = $true; Error = '' }
$viewH = ConvertTo-SvrIpcResponseView -Response $h -Context 'H'
Assert-Equal '' $viewH.Error 'H Error empty'

# StrictMode: reading missing optional via helper must not throw
$missing = [pscustomobject]@{ Ok = $true; PayloadJson = (@{ Outcome = 'Pass' } | ConvertTo-Json -Compress) }
$errProbe = Get-SvrObjectPropertyValue -Object $missing -Name 'Error' -Default 'unset'
Assert-Equal 'unset' $errProbe 'missing Error property'

# Diagnostic payload contract
$diagOk = $viewA.PayloadJson | ConvertFrom-Json
Assert-Throws { ConvertTo-SvrDiagnosticPayloadView -PayloadObject $diagOk -Context 'diag missing outcome' } 'missing required property: Outcome' 'diag outcome required'

$diagSparse = Get-SvrDiagnosticPayloadFromJson -PayloadJson (@{ Outcome = 'Pass' } | ConvertTo-Json -Compress) -Context 'sparse diag'
Assert-Equal 'Pass' $diagSparse.Outcome 'sparse diag outcome'
Assert-Equal '' $diagSparse.Message 'sparse diag message default'

# Invoke-SvrIpc normalization with sparse mock object
$script:SvrIpcInvokeOverride = {
    param($Method, $PayloadJson, $TimeoutMs, $WallClockTimeoutMs)
    return [pscustomobject]@{ Ok = $true; PayloadJson = '{"Outcome":"Pass"}' }
}
$viaIpc = Invoke-SvrIpc -Method 'GetStatus' -TimeoutMs 1000
Assert-Equal '' $viaIpc.Error 'Invoke-SvrIpc sparse Error'
Assert-Equal $true $viaIpc.Ok 'Invoke-SvrIpc sparse Ok'
$script:SvrIpcInvokeOverride = $null

Write-Host 'PASS test-svr-ipc-response-properties'
exit 0