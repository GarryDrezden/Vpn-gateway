$ErrorActionPreference = "Stop"
$scriptsRoot = $PSScriptRoot
$tests = @(
    'test-runner-ipc-dependencies.ps1',
    'test-run-runtime-diagnostic-logging.ps1',
    'test-wait-svr-ipc-ready.ps1',
    'test-run-runtime-diagnostic-report-state.ps1',
    'test-svr-powershell-async-watch.ps1',
    'test-svr-ipc-response-properties.ps1',
    'test-runtime-diagnostic-runner-full.ps1',
    'test-svr-ipc-watch-live.ps1'
)

foreach ($t in $tests) {
    $path = Join-Path $scriptsRoot $t
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing test: $path" }
    Write-Host "==> $t"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $path
    if ($LASTEXITCODE -ne 0) { throw "FAILED: $t (exit=$LASTEXITCODE)" }
}

$runner = Join-Path $scriptsRoot 'run-runtime-diagnostic.ps1'
$tokens = $null
$errs = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile($runner, [ref]$tokens, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
    throw ("Syntax errors in run-runtime-diagnostic.ps1: " + ($errs | ForEach-Object { $_.Message }) -join '; ')
}

Write-Host 'RUNTIME DIAGNOSTIC PREFLIGHT: PASS'
exit 0