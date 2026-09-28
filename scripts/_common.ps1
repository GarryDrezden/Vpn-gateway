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
