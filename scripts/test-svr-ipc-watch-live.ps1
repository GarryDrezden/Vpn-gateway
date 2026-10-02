$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_update-helpers.ps1")
$serviceProcId = Get-SvrServiceProcessId
if ($null -eq $serviceProcId -or [int]$serviceProcId -le 0) { Write-Host "SKIP live IPC watch (service down)"; exit 0 }
$livePoll = Test-SvrIpcServiceWatchPoll -BaselinePid $serviceProcId
if ($livePoll.Abort) { throw "False service watch at baseline: $($livePoll.Message)" }
$raw = $null
$ps = [powershell]::Create()
try {
    [void]$ps.AddScript({
        param($Root, $ServiceProcessId)
        . (Join-Path $Root "_common.ps1")
        . (Join-Path $Root "_update-helpers.ps1")
        $deadline = [datetime]::UtcNow.AddSeconds(15)
        Invoke-SvrIpcCore -Method GetStatus -DeadlineUtc $deadline
    }).AddArgument($PSScriptRoot).AddArgument($serviceProcId)
    $async = $ps.BeginInvoke()
    while (-not $async.IsCompleted) { Start-Sleep -Milliseconds 50 }
    $raw = $ps.EndInvoke($async)
}
finally { $ps.Dispose() }
$items = Get-SvrPowerShellPipelineOutputItems -Output $raw
Write-Host ("live EndInvoke type=" + ($raw.GetType().FullName) + " itemCount=" + $items.Count)
if ($items.Count -gt 0) {
    Write-SvrIpcResponseShapeTelemetry -Response $items[0] -Label "live GetStatus raw ipc"
}
$r = Invoke-SvrIpcWithServiceWatch -Method GetStatus -WallClockTimeoutMs 15000 -ServicePidBaseline $serviceProcId -ServicePidPollMs 500 -HelpersRoot $PSScriptRoot
Write-SvrIpcResponseShapeTelemetry -Response $r -Label "live GetStatus normalized view"
Write-Host ("watch return type=" + ($r.GetType().FullName) + " hasOk=" + ($null -ne $r.Ok))
if (-not $r.Ok) { throw "GetStatus failed: $($r.Error)" }
Write-Host "PASS live Invoke-SvrIpcWithServiceWatch GetStatus"
exit 0