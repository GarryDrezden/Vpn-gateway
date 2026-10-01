# Self-test for run-runtime-diagnostic Add-Line / report blank lines.

$ErrorActionPreference = "Stop"

$consoleLines = New-Object System.Collections.Generic.List[string]

function Add-Line {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Line
    )
    if ($null -eq $Line) {
        $Line = ""
    }
    $script:consoleLines.Add($Line)
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -cne $Actual) {
        throw ("{0} (expected=''{1}'' actual=''{2}'')" -f $Message, $Expected, $Actual)
    }
}

Add-Line "abc"
Assert-Equal "abc" $consoleLines[0] "non-empty line"

Add-Line ""
Assert-Equal "" $consoleLines[1] "empty string line"
Assert-Equal 2 $consoleLines.Count "two lines after empty"

Add-Line $null
Assert-Equal "" $consoleLines[2] "null coerced to empty"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("header")
[void]$sb.AppendLine()
[void]$sb.AppendLine("message=")
foreach ($line in $consoleLines) {
    [void]$sb.AppendLine($line)
}
$report = $sb.ToString()
if ($report -notmatch "header(\r?\n){2}message=") {
    throw "report should contain blank separator line"
}

Write-Host "PASS test-run-runtime-diagnostic-logging"
exit 0