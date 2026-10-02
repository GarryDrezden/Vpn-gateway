$ErrorActionPreference = "Stop"
$scriptsRoot = $PSScriptRoot
$tests = @(
    'test-runner-ipc-dependencies.ps1',
    'test-run-runtime-diagnostic-logging.ps1',
    'test-run-runtime-diagnostic-report-state.ps1',
    'test-svr-ipc-response-properties.ps1',
    'test-svr-service-pid-watch.ps1',
    'test-svr-powershell-async-watch.ps1',
    'test-vpn-lifecycle-sequence-mock.ps1'
)
foreach ($t in $tests) {
    Write-Host "==> $t"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptsRoot $t)
    if ($LASTEXITCODE -ne 0) { throw "FAILED: $t" }
}

$runner = Join-Path $scriptsRoot 'run-vpn-lifecycle-diagnostic.ps1'
$tokens = $null; $errs = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile($runner, [ref]$tokens, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
    throw ("Syntax errors in run-vpn-lifecycle-diagnostic.ps1: " + ($errs | ForEach-Object { $_.Message }) -join '; ')
}

Write-Host 'VPN LIFECYCLE PREFLIGHT: PASS'
exit 0