# Shared helpers for VPN Route driver/service scripts.
# Scripts never change Secure Boot, BitLocker, HVCI, or TESTSIGNING.

function Test-SvrElevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-SvrResult {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "FAIL", "WARNING", "INFO")][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (Get-Command Write-SvrUpdateLogLine -ErrorAction SilentlyContinue) {
        Write-SvrUpdateLogLine ("{0,-8} {1}: {2}" -f $Outcome, $Name, $Message)
    }
    if ($global:SvrUpdateQuiet -and $Outcome -ne "FAIL") {
        return
    }
    $color = switch ($Outcome) {
        "PASS" { "Green" }
        "FAIL" { "Red" }
        "WARNING" { "Yellow" }
        default { "Gray" }
    }
    Write-Host ("{0,-8} {1}: {2}" -f $Outcome, $Name, $Message) -ForegroundColor $color
}

function Write-SvrPipelineInfo {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (Get-Command Write-SvrUpdateLogLine -ErrorAction SilentlyContinue) {
        Write-SvrUpdateLogLine ("INFO     {0}: {1}" -f $Name, $Message)
    }
    elseif (-not $global:SvrUpdateQuiet) {
        Write-SvrResult -Outcome INFO -Name $Name -Message $Message
    }
}

function Invoke-SvrPipelineFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (Get-Command Write-SvrUpdateLogLine -ErrorAction SilentlyContinue) {
        Write-SvrUpdateLogLine ("FAIL     {0}: {1}" -f $Name, $Message)
    }
    if ($global:SvrUpdateQuiet) {
        throw "${Name}: ${Message}"
    }
    Write-SvrResult -Outcome FAIL -Name $Name -Message $Message
    exit 1
}

function Get-SvrRepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Initialize-SvrProgramData {
    param(
        [string]$DataDir = (Join-Path $env:ProgramData "SelectiveVpnRouter")
    )

    foreach ($sub in @("", "logs", "runtime")) {
        $path = if ($sub) { Join-Path $DataDir $sub } else { $DataDir }
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)

    $inheritance =
        [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit

    $fullControl =
        [System.Security.AccessControl.FileSystemRights]::FullControl

    $readExecute =
        [System.Security.AccessControl.FileSystemRights]::ReadAndExecute

    $propagation =
        [System.Security.AccessControl.PropagationFlags]::None

    $allow =
        [System.Security.AccessControl.AccessControlType]::Allow

    $systemSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-18")
    $administratorsSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-544")
    $usersSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-545")

    $systemRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $systemSid,
        $fullControl,
        $inheritance,
        $propagation,
        $allow
    )
    $acl.AddAccessRule($systemRule)

    $administratorsRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $administratorsSid,
        $fullControl,
        $inheritance,
        $propagation,
        $allow
    )
    $acl.AddAccessRule($administratorsRule)

    $usersRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $usersSid,
        $readExecute,
        $inheritance,
        $propagation,
        $allow
    )
    $acl.AddAccessRule($usersRule)

    Set-Acl -Path $DataDir -AclObject $acl
    Write-SvrResult -Outcome INFO -Name "program-data" -Message "Initialized ACL on $DataDir (SYSTEM/Administrators=FullControl, Users=ReadAndExecute)"
}

function Get-SvrVsInstall {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        return $null
    }
    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($path)) {
        return $null
    }
    return $path.Trim()
}

function Get-SvrMsbuild {
    $vs = Get-SvrVsInstall
    if ($vs) {
        $p = Join-Path $vs "MSBuild\Current\Bin\amd64\MSBuild.exe"
        if (Test-Path $p) { return $p }
        $p = Join-Path $vs "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Test-SvrWdk {
    $vs = Get-SvrVsInstall
    $toolset = $false
    if ($vs) {
        $toolset = Test-Path (Join-Path $vs "MSBuild\Microsoft\VC\v170\Platforms\x64\PlatformToolsets\WindowsKernelModeDriver10.0")
    }
    $km = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Include\*\km\fwpsk.h" -ErrorAction SilentlyContinue | Select-Object -First 1
    $wdf = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Include\wdf\kmdf\*\wdf.h" -ErrorAction SilentlyContinue | Select-Object -First 1
    return [pscustomobject]@{
        VsInstall     = $vs
        Toolset       = $toolset
        FwpskHeader   = [bool]$km
        WdfHeader     = [bool]$wdf
        Ready         = ($toolset -and $km -and $wdf)
        FwpskPath     = if ($km) { $km.FullName } else { $null }
        Missing       = @(
            $(if (-not $vs) { 'Visual Studio 2022 with Desktop development with C++' })
            $(if (-not $toolset) { 'WDK Visual Studio integration (platform toolset WindowsKernelModeDriver10.0)' })
            $(if (-not $km) { 'WDK kernel headers (Include km fwpsk.h)' })
            $(if (-not $wdf) { 'KMDF headers (Include wdf kmdf wdf.h)' })
        ) | Where-Object { $_ }
    }
}

function Get-SvrDefaultSysPath {
    $root = Get-SvrRepoRoot
    foreach ($cfg in @("Release", "Debug")) {
        $p = Join-Path $root "artifacts\driver\$cfg\SelectiveVpnCallout.sys"
        if (Test-Path $p) { return $p }
    }
    $sys32 = Join-Path $env:SystemRoot "System32\drivers\SelectiveVpnCallout.sys"
    if (Test-Path $sys32) { return $sys32 }
    return $null
}

function Get-SvrPublishDirectory {
    param([string]$Root = (Get-SvrRepoRoot))
    return (Join-Path $Root "artifacts\publish\SelectiveVpnRouter")
}

function Get-SvrStagingDirectory {
    param([string]$Root = (Get-SvrRepoRoot))
    return (Join-Path $Root "artifacts\staging\SelectiveVpnRouter")
}

function Get-SvrPublishPrevDirectory {
    param([string]$Root = (Get-SvrRepoRoot))
    return (Join-Path $Root "artifacts\publish-prev\SelectiveVpnRouter")
}

function Get-SvrNormalizedDirectoryPrefix {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.EndsWith('\')) {
        $full += '\'
    }
    return $full
}

function Test-SvrPathUnderPublishDirectory {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)][string]$PublishDir
    )

    if (-not $Path) {
        return $false
    }

    try {
        $full = [System.IO.Path]::GetFullPath($Path)
        $prefix = Get-SvrNormalizedDirectoryPrefix -Path $PublishDir
        return $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Wait-SvrProcessExit {
    param(
        [Parameter(Mandatory = $true)][int[]]$ProcessIds,
        [int]$TimeoutSeconds = 10
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    foreach ($procId in $ProcessIds) {
        while ($null -ne (Get-Process -Id $procId -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        if ($null -ne (Get-Process -Id $procId -ErrorAction SilentlyContinue)) {
            return $false
        }
    }
    return $true
}

function Stop-AllSvrGuiProcesses {
    param([int]$TimeoutSeconds = 10)

    $procs = @(Get-Process -Name "SelectiveVpnRouter.App" -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        Write-SvrPipelineInfo -Name "gui-stop" -Message "no SelectiveVpnRouter.App processes running"
        return
    }

    foreach ($proc in $procs) {
        $path = try { $proc.Path } catch { "<unknown>" }
        Write-SvrPipelineInfo -Name "gui-stop" -Message "stopping PID $($proc.Id) path=$path"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    $ids = @($procs | ForEach-Object { $_.Id })
    if (-not (Wait-SvrProcessExit -ProcessIds $ids -TimeoutSeconds $TimeoutSeconds)) {
        $alive = @(Get-Process -Name "SelectiveVpnRouter.App" -ErrorAction SilentlyContinue)
        $details = ($alive | ForEach-Object {
            $path = try { $_.Path } catch { "<unknown>" }
            "PID=$($_.Id) path=$path"
        }) -join "; "
        Invoke-SvrPipelineFailure -Name "gui-stop" -Message "SelectiveVpnRouter.App still running after ${TimeoutSeconds}s: $details"
    }

    Write-SvrPipelineInfo -Name "gui-stop" -Message "stopped $($ids.Count) process(es)"
}

function Start-SvrPublishedService {
    param(
        [string]$ServiceName = "SelectiveVpnRouter",
        [Parameter(Mandatory = $true)][string]$ServiceExe,
        [int]$TimeoutSeconds = 30
    )

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-SvrUpdateLogLine "service-start: installing service from publish"
        & (Join-Path $PSScriptRoot "install-service.ps1") -BinPath $ServiceExe | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "install-service.ps1 failed with exit code $LASTEXITCODE" }
        return
    }

    if ($svc.Status -eq "Running") {
        Write-SvrUpdateLogLine "service-start: stopping running service before fresh start"
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Service -Name $ServiceName).Status -ne "Stopped" -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }

    if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
        throw "service did not reach Stopped before publish start"
    }

    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ($wmi.ProcessId -gt 0) {
        Wait-SvrProcessExit -ProcessIds @([int]$wmi.ProcessId) -TimeoutSeconds 10 | Out-Null
    }

    Set-Service -Name $ServiceName -StartupType Automatic
    Write-SvrUpdateLogLine "service-start: starting $ServiceName from publish"
    Start-Service -Name $ServiceName -ErrorAction Stop
    (Get-Service -Name $ServiceName).WaitForStatus("Running", (New-TimeSpan -Seconds $TimeoutSeconds))
    if ((Get-Service -Name $ServiceName).Status -ne "Running") {
        throw "service did not reach Running after publish start"
    }

    $startedPid = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").ProcessId
    Write-SvrUpdateLogLine "service-start: Running PID=$startedPid"
}

function Stop-SvrServiceForPublish {
    param(
        [string]$ServiceName = "SelectiveVpnRouter",
        [int]$TimeoutSeconds = 15
    )

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-SvrPipelineInfo -Name "service-stop" -Message "service not installed"
        return $false
    }

    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ($svc.Status -eq "Stopped") {
        if ($wmi.ProcessId -gt 0) {
            Write-SvrPipelineInfo -Name "service-stop" -Message "orphan service PID=$($wmi.ProcessId); forcing stop"
            Stop-Process -Id ([int]$wmi.ProcessId) -Force -ErrorAction SilentlyContinue
            Wait-SvrProcessExit -ProcessIds @([int]$wmi.ProcessId) -TimeoutSeconds 5 | Out-Null
        }
        Write-SvrPipelineInfo -Name "service-stop" -Message "already Stopped"
        return $true
    }

    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Service -Name $ServiceName).Status -ne "Stopped" -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
    }

    if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
        $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
        $servicePid = [int]$wmi.ProcessId
        Write-SvrPipelineInfo -Name "service-stop" -Message "forcing service process PID=$servicePid Path=$($wmi.PathName)"
        if ($servicePid -gt 0) {
            Stop-Process -Id $servicePid -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
        }
    }

    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
        Invoke-SvrPipelineFailure -Name "service-stop" -Message "service did not reach Stopped; PID=$($wmi.ProcessId) Path=$($wmi.PathName)"
    }

    if ($wmi.ProcessId -gt 0) {
        Stop-Process -Id ([int]$wmi.ProcessId) -Force -ErrorAction SilentlyContinue
        Wait-SvrProcessExit -ProcessIds @([int]$wmi.ProcessId) -TimeoutSeconds 5 | Out-Null
    }

    Start-Sleep -Seconds 2
    foreach ($proc in @(Get-CimInstance Win32_Process -Filter "Name='SelectiveVpnRouter.Service.exe'" -ErrorAction SilentlyContinue)) {
        Write-SvrPipelineInfo -Name "service-stop" -Message "cleanup service PID=$($proc.ProcessId) path=$($proc.ExecutablePath)"
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-SvrProcessExit -ProcessIds @([int]$proc.ProcessId) -TimeoutSeconds 5 | Out-Null
    }

    Write-SvrPipelineInfo -Name "service-stop" -Message "Stopped"
    return $true
}

function Get-SvrPublishLockingProcesses {
    param([Parameter(Mandatory = $true)][string]$PublishDir)

    if (-not (Test-Path -LiteralPath $PublishDir)) {
        return @()
    }

    $prefix = Get-SvrNormalizedDirectoryPrefix -Path $PublishDir
    $byPid = @{}

    foreach ($svc in @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue)) {
        if ($svc.State -ne "Running" -or -not $svc.PathName) {
            continue
        }

        $exe = $svc.PathName.Trim()
        if ($exe.StartsWith('"')) {
            $endQuote = $exe.IndexOf('"', 1)
            if ($endQuote -gt 0) {
                $exe = $exe.Substring(1, $endQuote - 1)
            }
        }
        else {
            $exe = ($exe -split '\s+', 2)[0]
        }

        if (Test-SvrPathUnderPublishDirectory -Path $exe -PublishDir $PublishDir) {
            $byPid[[int]$svc.ProcessId] = [PSCustomObject]@{
                ProcessId      = $svc.ProcessId
                Name           = $svc.Name
                ExecutablePath = $exe
                LockReason     = "service:$($svc.Name)"
            }
        }
    }

    foreach ($proc in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
        if (Test-SvrPathUnderPublishDirectory -Path $proc.ExecutablePath -PublishDir $PublishDir) {
            $byPid[[int]$proc.ProcessId] = $proc
            continue
        }

        if ($proc.CommandLine -and $proc.CommandLine.IndexOf($prefix, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $byPid[[int]$proc.ProcessId] = $proc
        }
    }

    foreach ($proc in @(Get-Process -ErrorAction SilentlyContinue)) {
        if ($byPid.ContainsKey($proc.Id)) {
            continue
        }

        try {
            foreach ($mod in $proc.Modules) {
                if (Test-SvrPathUnderPublishDirectory -Path $mod.FileName -PublishDir $PublishDir) {
                    $byPid[$proc.Id] = [PSCustomObject]@{
                        ProcessId      = $proc.Id
                        Name           = $proc.ProcessName
                        ExecutablePath = $proc.Path
                        LockReason     = "module:$($mod.FileName)"
                    }
                    break
                }
            }
        }
        catch {
        }
    }

    return @($byPid.Values)
}

function Stop-SvrPublishOrphanProcesses {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [int]$TimeoutSeconds = 10
    )

    $imageNames = @(
        "SelectiveVpnRouter.Service.exe"
        "SelectiveVpnRouter.App.exe"
        "SelectiveVpnRouter.Probe.exe"
    )

    $stoppedIds = @()
    foreach ($image in $imageNames) {
        foreach ($proc in @(Get-CimInstance Win32_Process -Filter "Name='$image'" -ErrorAction SilentlyContinue)) {
            if (-not (Test-SvrPathUnderPublishDirectory -Path $proc.ExecutablePath -PublishDir $PublishDir)) {
                continue
            }

            Write-SvrPipelineInfo -Name "publish-orphan-stop" -Message "stopping PID $($proc.ProcessId) $image path=$($proc.ExecutablePath)"
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
            $stoppedIds += [int]$proc.ProcessId
        }
    }

    if ($stoppedIds.Count -eq 0) {
        return
    }

    if (-not (Wait-SvrProcessExit -ProcessIds $stoppedIds -TimeoutSeconds $TimeoutSeconds)) {
        Invoke-SvrPipelineFailure -Name "publish-orphan-stop" -Message "publish orphan processes still running after ${TimeoutSeconds}s"
    }
}

function Wait-SvrPublishDirectoryUnlocked {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [int]$TimeoutSeconds = 20,
        [int]$PollMilliseconds = 400
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $locking = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
        if ($locking.Count -eq 0) {
            Start-Sleep -Seconds 2
            return
        }

        foreach ($proc in $locking) {
            Write-SvrLockingProcess -Process $proc
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }

    $remaining = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
    foreach ($proc in $remaining) {
        Write-SvrLockingProcess -Process $proc
    }
    Invoke-SvrPipelineFailure -Name "publish-lock-check" -Message "publish directory still locked after ${TimeoutSeconds}s"
}

function Move-SvrPublishDirectoryAside {
    param([Parameter(Mandatory = $true)][string]$PublishDir)

    if (-not (Test-Path -LiteralPath $PublishDir)) {
        return $null
    }

    $parent = Split-Path $PublishDir -Parent
    $leaf = Split-Path $PublishDir -Leaf
    $retiredName = "{0}.retired-{1}" -f $leaf, (Get-Date -Format "yyyyMMddHHmmssfff")
    $retiredPath = Join-Path $parent $retiredName

    Write-SvrUpdateLogLine "publish-aside: rename `"$PublishDir`" -> `"$retiredPath`""
    Rename-Item -LiteralPath $PublishDir -NewName $retiredName -ErrorAction Stop
    return $retiredPath
}

function Remove-SvrRetiredPublishDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$ParentDir,
        [string]$LeafName = "SelectiveVpnRouter"
    )

    foreach ($dir in @(Get-ChildItem -LiteralPath $ParentDir -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "$LeafName.retired-*"
    })) {
        Write-SvrUpdateLogLine "publish-cleanup: removing retired $($dir.FullName)"
        Remove-DirectoryWithRetry -Path $dir.FullName -PublishDir $dir.FullName
    }
}

function Invoke-SvrRobocopyMirror {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [int]$RetryCount = 5,
        [int]$WaitSeconds = 2
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "robocopy source not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $logLine = "robocopy `"$Source`" -> `"$Destination`" /MIR /R:$RetryCount /W:$WaitSeconds"
    Write-SvrUpdateLogLine $logLine

    $detailLog = Join-Path ([System.IO.Path]::GetTempPath()) ("svr-robocopy-{0}.log" -f [Guid]::NewGuid().ToString("N"))
    try {
        & robocopy $Source $Destination /MIR /R:$RetryCount /W:$WaitSeconds /NFL /NDL /NJH /NJS /NP /LOG:$detailLog | Out-Null
        $code = $LASTEXITCODE
        if ($code -ge 8) {
            if (Test-Path -LiteralPath $detailLog) {
                Write-SvrUpdateLogLine "robocopy detail log ($detailLog):"
                Get-Content -LiteralPath $detailLog -ErrorAction SilentlyContinue | ForEach-Object {
                    Write-SvrUpdateLogLine $_
                }
            }
            throw "robocopy failed with exit code $code ($logLine)"
        }
    }
    finally {
        Remove-Item -LiteralPath $detailLog -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SvrPromotePublishDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [switch]$RecreateSource
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "promote source not found: $Source"
    }

    Wait-SvrPublishDirectoryUnlocked -PublishDir $Destination -TimeoutSeconds 20
    Ensure-SvrPublishDirectoryUnlocked -PublishDir $Source

    $retiredPath = $null
    if (Test-Path -LiteralPath $Destination) {
        try {
            $retiredPath = Move-SvrPublishDirectoryAside -PublishDir $Destination
        }
        catch {
            $locking = @(Get-SvrPublishLockingProcesses -PublishDir $Destination)
            foreach ($proc in $locking) {
                Write-SvrLockingProcess -Process $proc
            }
            throw "promote failed to retire live publish directory: $($_.Exception.Message)"
        }
    }

    $destParent = Split-Path $Destination -Parent
    New-Item -ItemType Directory -Force -Path $destParent | Out-Null

    try {
        Write-SvrUpdateLogLine "promote: move `"$Source`" -> `"$Destination`""
        Move-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop
    }
    catch {
        if ($retiredPath -and (Test-Path -LiteralPath $retiredPath) -and -not (Test-Path -LiteralPath $Destination)) {
            Write-SvrUpdateLogLine "promote: restoring retired publish directory after failed move"
            Rename-Item -LiteralPath $retiredPath -NewName (Split-Path $Destination -Leaf) -ErrorAction SilentlyContinue
        }
        throw
    }

    if ($RecreateSource) {
        New-Item -ItemType Directory -Force -Path $Source | Out-Null
    }

    return $retiredPath
}

function Invoke-SvrRestorePublishFromBackup {
    param(
        [Parameter(Mandatory = $true)][string]$BackupDir,
        [Parameter(Mandatory = $true)][string]$LiveDir
    )

    if (-not (Test-Path -LiteralPath $BackupDir)) {
        throw "publish backup not found: $BackupDir"
    }

    Wait-SvrPublishDirectoryUnlocked -PublishDir $LiveDir -TimeoutSeconds 20
    if (Test-Path -LiteralPath $LiveDir) {
        Move-SvrPublishDirectoryAside -PublishDir $LiveDir | Out-Null
    }

    Write-SvrUpdateLogLine "restore: robocopy backup -> live"
    Invoke-SvrRobocopyMirror -Source $BackupDir -Destination $LiveDir
}

function Write-SvrLockingProcess {
    param($Process)
    $lines = @(
        "LOCKING PROCESS:",
        "PID=$($Process.ProcessId)",
        "Name=$($Process.Name)",
        "Path=$($Process.ExecutablePath)"
    )
    foreach ($line in $lines) {
        if (Get-Command Write-SvrUpdateLogLine -ErrorAction SilentlyContinue) {
            Write-SvrUpdateLogLine $line
        }
        elseif (-not $global:SvrUpdateQuiet) {
            Write-Host $line
        }
    }
}

function Ensure-SvrPublishDirectoryUnlocked {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [int]$ProcessExitTimeoutSeconds = 10
    )

    $known = @(
        "SelectiveVpnRouter.App.exe", "SelectiveVpnRouter.Service.exe", "SelectiveVpnRouter.Probe.exe"
        "SelectiveVpnRouter.App", "SelectiveVpnRouter.Service", "SelectiveVpnRouter.Probe"
        "SelectiveVpnRouter"
    )
    $locking = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
    if ($locking.Count -eq 0) {
        return
    }

    foreach ($proc in $locking) {
        Write-SvrLockingProcess -Process $proc
        if ($proc.Name -notin $known) {
            Invoke-SvrPipelineFailure -Name "publish-lock-check" -Message "unknown process holds publish directory lock: PID=$($proc.ProcessId) Name=$($proc.Name)"
        }

        Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
    }

    $ids = @($locking | ForEach-Object { [int]$_.ProcessId })
    if (-not (Wait-SvrProcessExit -ProcessIds $ids -TimeoutSeconds $ProcessExitTimeoutSeconds)) {
        $still = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
        foreach ($proc in $still) {
            Write-SvrLockingProcess -Process $proc
        }
        Invoke-SvrPipelineFailure -Name "publish-lock-check" -Message "processes still using publish directory after stop attempt"
    }

    $remaining = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
    if ($remaining.Count -gt 0) {
        foreach ($proc in $remaining) {
            Write-SvrLockingProcess -Process $proc
        }
        Invoke-SvrPipelineFailure -Name "publish-lock-check" -Message "publish directory still locked"
    }
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$PublishDir,
        [int]$MaxAttempts = 5,
        [int]$DelayMs = 750
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $lockDir = if ($PublishDir) { $PublishDir } else { $Path }
    $lastError = $null

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        Ensure-SvrPublishDirectoryUnlocked -PublishDir $lockDir
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            $locking = @(Get-SvrPublishLockingProcesses -PublishDir $lockDir)
            foreach ($proc in $locking) {
                Write-SvrLockingProcess -Process $proc
            }
            if ($attempt -lt $MaxAttempts) {
                Start-Sleep -Milliseconds $DelayMs
            }
        }
    }

    $failedFile = $lastError.Exception.Message
    Invoke-SvrPipelineFailure -Name "publish-clean" -Message "Remove-Item failed after $MaxAttempts attempts: $failedFile"
}

function Update-VpnRouteDesktopShortcuts {
    param(
        [Parameter(Mandatory = $true)][string]$AppExe,
        [string]$ProductName = "VPN Route",
        [string]$IconPath
    )

    if (-not (Test-Path -LiteralPath $AppExe)) {
        Write-SvrResult -Outcome FAIL -Name "shortcut" -Message "App exe not found: $AppExe"
        return
    }

    $shell = New-Object -ComObject WScript.Shell
    $iconLocation = if ($IconPath -and (Test-Path -LiteralPath $IconPath)) { "$IconPath,0" } else { "$AppExe,0" }
    $targets = @(
        @{ Path = Join-Path ([Environment]::GetFolderPath('Desktop')) "$ProductName.lnk"; Scope = "desktop" }
        @{ Path = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) "$ProductName\$ProductName.lnk"; Scope = "start-menu" }
    )

    $legacyNames = @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) "SelectiveVpnRouter.App.lnk")
        (Join-Path ([Environment]::GetFolderPath('Desktop')) "Selective VPN Router.lnk")
        (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) "Selective VPN Router.lnk")
        (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) "SelectiveVpnRouter.App.lnk")
    )
    foreach ($legacy in $legacyNames) {
        if (Test-Path -LiteralPath $legacy) {
            Remove-Item -LiteralPath $legacy -Force -ErrorAction SilentlyContinue
        }
    }

    foreach ($target in $targets) {
        $dir = Split-Path $target.Path -Parent
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }

        $shortcut = $shell.CreateShortcut($target.Path)
        $shortcut.TargetPath = $AppExe
        $shortcut.WorkingDirectory = Split-Path $AppExe -Parent
        $shortcut.IconLocation = $iconLocation
        $shortcut.Description = $ProductName
        $shortcut.Save()
        Write-SvrUpdateDetail ("shortcut $($target.Scope): $($target.Path)")
        if (-not $global:SvrUpdateQuiet) {
            Write-SvrResult -Outcome PASS -Name "shortcut" -Message "$($target.Scope): $($target.Path)"
        }
    }
}

. (Join-Path $PSScriptRoot "_update-helpers.ps1")
