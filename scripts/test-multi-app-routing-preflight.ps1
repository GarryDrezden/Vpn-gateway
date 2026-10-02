$ErrorActionPreference = "Stop"
$scriptsRoot = $PSScriptRoot
$tests = @(
    'test-runner-ipc-dependencies.ps1',
    'test-run-runtime-diagnostic-logging.ps1',
    'test-run-runtime-diagnostic-report-state.ps1',
    'test-svr-ipc-response-properties.ps1',
    'test-svr-service-pid-watch.ps1',
    'test-svr-powershell-async-watch.ps1',
    'test-multi-app-routing-report-parse.ps1',
    'test-multi-app-routing-sequence-mock.ps1'
)
foreach ($t in $tests) {
    Write-Host "==> $t"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptsRoot $t)
    if ($LASTEXITCODE -ne 0) { throw "FAILED: $t" }
}

foreach ($runner in @('run-multi-app-routing-diagnostic.ps1', 'run-multi-app-routing-regression.ps1')) {
    $path = Join-Path $scriptsRoot $runner
    $tokens = $null; $errs = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errs)
    if ($errs -and $errs.Count -gt 0) {
        throw ("Syntax errors in ${runner}: " + ($errs | ForEach-Object { $_.Message }) -join '; ')
    }
}

Write-Host 'MULTI-APP ROUTING PREFLIGHT: PASS'
exit 0
