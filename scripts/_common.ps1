# Shared helpers for Selective VPN Router driver/service scripts.
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
    $color = switch ($Outcome) {
        "PASS" { "Green" }
        "FAIL" { "Red" }
        "WARNING" { "Yellow" }
        default { "Gray" }
    }
    Write-Host ("{0,-8} {1}: {2}" -f $Outcome, $Name, $Message) -ForegroundColor $color
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

function Get-SvrNormalizedDirectoryPrefix {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.EndsWith('\')) {
        $full += '\'
    }
    return $full
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
        Write-SvrResult -Outcome INFO -Name "gui-stop" -Message "no SelectiveVpnRouter.App processes running"
        return
    }

    foreach ($proc in $procs) {
        $path = try { $proc.Path } catch { "<unknown>" }
        Write-SvrResult -Outcome INFO -Name "gui-stop" -Message "stopping PID $($proc.Id) path=$path"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    $ids = @($procs | ForEach-Object { $_.Id })
    if (-not (Wait-SvrProcessExit -ProcessIds $ids -TimeoutSeconds $TimeoutSeconds)) {
        $alive = @(Get-Process -Name "SelectiveVpnRouter.App" -ErrorAction SilentlyContinue)
        $details = ($alive | ForEach-Object {
            $path = try { $_.Path } catch { "<unknown>" }
            "PID=$($_.Id) path=$path"
        }) -join "; "
        Write-SvrResult -Outcome FAIL -Name "gui-stop" -Message "SelectiveVpnRouter.App still running after ${TimeoutSeconds}s: $details"
        exit 1
    }

    Write-SvrResult -Outcome INFO -Name "gui-stop" -Message "stopped $($ids.Count) process(es)"
}

function Stop-SvrServiceForPublish {
    param(
        [string]$ServiceName = "SelectiveVpnRouter",
        [int]$TimeoutSeconds = 15
    )

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-SvrResult -Outcome INFO -Name "service-stop" -Message "service not installed"
        return $false
    }

    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ($svc.Status -eq "Stopped") {
        Write-SvrResult -Outcome INFO -Name "service-stop" -Message "already Stopped"
        return $true
    }

    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Service -Name $ServiceName).Status -ne "Stopped" -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
    }

    if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
        $servicePid = [int]$wmi.ProcessId
        Write-SvrResult -Outcome INFO -Name "service-stop" -Message "forcing service process PID=$servicePid Path=$($wmi.PathName)"
        if ($servicePid -gt 0) {
            Stop-Process -Id $servicePid -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
        }
    }

    if ((Get-Service -Name $ServiceName).Status -ne "Stopped") {
        Write-SvrResult -Outcome FAIL -Name "service-stop" -Message "service did not reach Stopped; PID=$($wmi.ProcessId) Path=$($wmi.PathName)"
        exit 1
    }

    if ($wmi.ProcessId -gt 0) {
        Wait-SvrProcessExit -ProcessIds @([int]$wmi.ProcessId) -TimeoutSeconds 5 | Out-Null
    }

    Write-SvrResult -Outcome INFO -Name "service-stop" -Message "Stopped"
    return $true
}

function Get-SvrPublishLockingProcesses {
    param([Parameter(Mandatory = $true)][string]$PublishDir)

    if (-not (Test-Path -LiteralPath $PublishDir)) {
        return @()
    }

    $prefix = Get-SvrNormalizedDirectoryPrefix -Path $PublishDir
    return @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Write-SvrLockingProcess {
    param($Process)
    Write-Host "LOCKING PROCESS:"
    Write-Host "PID=$($Process.ProcessId)"
    Write-Host "Name=$($Process.Name)"
    Write-Host "Path=$($Process.ExecutablePath)"
}

function Ensure-SvrPublishDirectoryUnlocked {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [int]$ProcessExitTimeoutSeconds = 10
    )

    $known = @("SelectiveVpnRouter.App.exe", "SelectiveVpnRouter.Service.exe")
    $locking = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
    if ($locking.Count -eq 0) {
        return
    }

    foreach ($proc in $locking) {
        Write-SvrLockingProcess -Process $proc
        if ($proc.Name -notin $known) {
            Write-SvrResult -Outcome FAIL -Name "publish-lock-check" -Message "unknown process holds publish directory lock: PID=$($proc.ProcessId) Name=$($proc.Name)"
            exit 1
        }

        Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
    }

    $ids = @($locking | ForEach-Object { [int]$_.ProcessId })
    if (-not (Wait-SvrProcessExit -ProcessIds $ids -TimeoutSeconds $ProcessExitTimeoutSeconds)) {
        $still = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
        foreach ($proc in $still) {
            Write-SvrLockingProcess -Process $proc
        }
        Write-SvrResult -Outcome FAIL -Name "publish-lock-check" -Message "processes still using publish directory after stop attempt"
        exit 1
    }

    $remaining = @(Get-SvrPublishLockingProcesses -PublishDir $PublishDir)
    if ($remaining.Count -gt 0) {
        foreach ($proc in $remaining) {
            Write-SvrLockingProcess -Process $proc
        }
        Write-SvrResult -Outcome FAIL -Name "publish-lock-check" -Message "publish directory still locked"
        exit 1
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
    Write-SvrResult -Outcome FAIL -Name "publish-clean" -Message "Remove-Item failed after $MaxAttempts attempts: $failedFile"
    exit 1
}
