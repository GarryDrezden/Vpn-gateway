# Smoke test for Initialize-SvrProgramData (Windows PowerShell 5.1 compatible).
# Uses a temp directory; does not modify real ProgramData.

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

function Test-SvrProgramDataAclRule {
    param(
        [Parameter(Mandatory = $true)][System.Security.AccessControl.FileSystemSecurity]$Acl,
        [Parameter(Mandatory = $true)][string]$Sid,
        [Parameter(Mandatory = $true)][System.Security.AccessControl.FileSystemRights]$ExpectedRights
    )

    $identity = New-Object System.Security.Principal.SecurityIdentifier($Sid)
    $rules = $Acl.GetAccessRules(
        $true,
        $false,
        [System.Security.Principal.SecurityIdentifier]
    )

    foreach ($rule in $rules) {
        if ($rule.IdentityReference -eq $identity -and $rule.AccessControlType -eq "Allow") {
            return (($rule.FileSystemRights -band $ExpectedRights) -eq $ExpectedRights)
        }
    }

    return $false
}

$tempRoot = Join-Path $env:TEMP ("svr-programdata-smoke-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    Initialize-SvrProgramData -DataDir $tempRoot

    $applied = Get-Acl -Path $tempRoot
    if (-not $applied.AreAccessRulesProtected) {
        throw "Expected access rule protection to be enabled on $tempRoot"
    }

    $fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
    $readExecute = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute

    $checks = @(
        @{ Sid = "S-1-5-18"; Rights = $fullControl },
        @{ Sid = "S-1-5-32-544"; Rights = $fullControl },
        @{ Sid = "S-1-5-32-545"; Rights = $readExecute }
    )

    foreach ($check in $checks) {
        $ok = Test-SvrProgramDataAclRule -Acl $applied -Sid $check.Sid -ExpectedRights $check.Rights
        if (-not $ok) {
            throw ("Missing or incorrect ACL for SID {0}; expected Allow rights containing {1}" -f $check.Sid, $check.Rights)
        }
    }

    foreach ($sub in @("logs", "runtime")) {
        $subPath = Join-Path $tempRoot $sub
        if (-not (Test-Path $subPath)) {
            throw "Expected subdirectory was not created: $subPath"
        }
    }

    Write-SvrResult -Outcome PASS -Name "initialize-program-data-smoke" -Message "Initialize-SvrProgramData succeeded on temp dir $tempRoot"
    exit 0
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}