<#
.SYNOPSIS
    Runs the same gates CI runs, in one command.

.DESCRIPTION
    Restores the pinned modules, then checks that every PowerShell file is UTF-8 with a byte-order
    mark, runs PSScriptAnalyzer with the project settings, runs the Pester tests, and checks the
    configured content files for em-dashes. Files that git tracks or would track are checked, so a
    new file is covered before its first commit. Fails on the first gate that fails.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
#>
#Requires -Version 5.1
[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Get-ProjectFile {
    [CmdletBinding()]
    [OutputType([string])]
    param (
        [Parameter(Mandatory = $true)]
        [string[]] $Pattern
    )

    $listed = @(git -C $root ls-files --cached --others --exclude-standard -- $Pattern)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    $listed | Where-Object { Test-Path -LiteralPath (Join-Path -Path $root -ChildPath $_) } | Sort-Object -Unique
}

function Test-Bom {
    [CmdletBinding()]
    [OutputType([bool])]
    param (
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [byte[]] $Bytes
    )

    $Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF
}

Write-Information -MessageData 'Gate: restore pinned modules' -InformationAction Continue
& (Join-Path -Path $root -ChildPath 'Restore-PinnedModules.ps1') -ManifestPath (Join-Path -Path $root -ChildPath 'PinnedModules.psd1') -Destination (Join-Path -Path $root -ChildPath '.modules')
$manifest = Import-PowerShellDataFile -LiteralPath (Join-Path -Path $root -ChildPath 'PinnedModules.psd1')

Write-Information -MessageData 'Gate: UTF-8 with byte-order mark' -InformationAction Continue
if (-not (Test-Bom -Bytes ([byte[]](0xEF, 0xBB, 0xBF, 0x41)))) { throw 'BOM detector is dead.' }
if (Test-Bom -Bytes ([byte[]](0x41, 0x42, 0x43))) { throw 'BOM detector accepts anything.' }
$powershellFiles = @(Get-ProjectFile -Pattern '*.ps1', '*.psm1', '*.psd1')
if ($powershellFiles.Count -eq 0) { throw 'No PowerShell files found, so nothing was checked (R-23).' }
$unmarked = @($powershellFiles | Where-Object { -not (Test-Bom -Bytes ([System.IO.File]::ReadAllBytes((Join-Path -Path $root -ChildPath $_)))) })
if ($unmarked.Count -gt 0) { throw ('Not UTF-8 with BOM: {0}' -f ($unmarked -join ', ')) }

Write-Information -MessageData 'Gate: PSScriptAnalyzer' -InformationAction Continue
$analyzerPin = $manifest.Modules | Where-Object { $_.Name -eq 'PSScriptAnalyzer' }
Import-Module -Name (Join-Path -Path $root -ChildPath ('.modules\PSScriptAnalyzer\{0}\PSScriptAnalyzer.psd1' -f $analyzerPin.Version))
$settings = Join-Path -Path $root -ChildPath 'PSScriptAnalyzerSettings.psd1'
# Warm-up pass, results discarded. On the first analyses in a fresh process, UseCorrectCasing in
# PSScriptAnalyzer 1.25.0 intermittently throws a NullReferenceException from
# CommandInfo.Parameters (observed 2026-11-03, docs/decisions.md). After one pass over the files
# it did not recur in 162 analyses. The strict pass below still fails on any analyzer error.
# The warm-up also runs the -SuppressedOnly analysis, which failed once in CI with a RULE_ERROR
# ("Get-Command is not recognized") after the plain warm-up (observed 2026-11-04).
foreach ($file in $powershellFiles) {
    $null = Invoke-ScriptAnalyzer -Path (Join-Path -Path $root -ChildPath $file) -Settings $settings -ErrorAction SilentlyContinue
    $null = Invoke-ScriptAnalyzer -Path (Join-Path -Path $root -ChildPath $file) -Settings $settings -SuppressedOnly -ErrorAction SilentlyContinue
}
$failed = $false
foreach ($file in $powershellFiles) {
    $full = Join-Path -Path $root -ChildPath $file
    $analyzerErrors = $null
    $found = @(Invoke-ScriptAnalyzer -Path $full -Settings $settings -ErrorVariable analyzerErrors -ErrorAction SilentlyContinue)
    $found | ForEach-Object { '{0}:{1} {2} [{3}] {4}' -f $file, $_.Line, $_.RuleName, $_.Severity, $_.Message }
    if ($found | Where-Object { $_.Severity -in 'Error', 'Warning', 'ParseError' }) { $failed = $true }
    $suppressedErrors = $null
    $unexplained = @(Invoke-ScriptAnalyzer -Path $full -Settings $settings -SuppressedOnly -ErrorVariable suppressedErrors -ErrorAction SilentlyContinue |
            Where-Object { [string]::IsNullOrWhiteSpace($_.Justification) })
    $analyzerErrors = @($analyzerErrors) + @($suppressedErrors) | Where-Object { $_ }
    $unexplained | ForEach-Object { '{0}:{1} {2} suppressed without a Justification' -f $file, $_.Line, $_.RuleName }
    if ($unexplained.Count -gt 0) { $failed = $true }
    if (@($analyzerErrors).Count -gt 0) {
        $analyzerErrors | ForEach-Object { 'analyzer error in {0}: {1} [{2}] {3}' -f $file, $_, $_.FullyQualifiedErrorId, $_.TargetObject }
        $failed = $true
    }
}
if ($failed) { throw 'PSScriptAnalyzer reported issues (C-03).' }

Write-Information -MessageData 'Gate: Pester' -InformationAction Continue
$pesterPin = $manifest.Modules | Where-Object { $_.Name -eq 'Pester' }
Import-Module -Name (Join-Path -Path $root -ChildPath ('.modules\Pester\{0}\Pester.psd1' -f $pesterPin.Version))
$loaded = (Get-Module -Name Pester).Version.ToString()
if ($loaded -ne $pesterPin.Version) { throw ('Pester {0} is loaded, {1} is pinned.' -f $loaded, $pesterPin.Version) }
$configuration = New-PesterConfiguration
$configuration.Run.Path = Join-Path -Path $root -ChildPath 'tests'
$configuration.Run.PassThru = $true
$configuration.Output.Verbosity = 'Normal'
$result = Invoke-Pester -Configuration $configuration
if ($result.TotalCount -eq 0) { throw 'No tests ran, so nothing was tested (R-23).' }
if ($result.FailedCount -gt 0) { throw ('{0} test(s) failed.' -f $result.FailedCount) }

Write-Information -MessageData 'Gate: no em-dash in content files' -InformationAction Continue
$config = Get-Content -LiteralPath (Join-Path -Path $root -ChildPath '.miralsoft-enforcement') -Encoding UTF8
$globs = @((($config | Where-Object { $_ -like 'CONTENT_GLOBS=*' }) -replace '^CONTENT_GLOBS=', '') -split '\s+' | Where-Object { $_ })
if ($globs.Count -eq 0) { throw 'CONTENT_GLOBS is empty, so the em-dash check would examine nothing (R-23).' }
$dash = [string][char]0x2014
if (-not ('a{0}b' -f $dash).Contains($dash)) { throw 'Em-dash detector is dead.' }
$dashed = @(Get-ProjectFile -Pattern $globs | Where-Object {
        [System.IO.File]::ReadAllText((Join-Path -Path $root -ChildPath $_), [System.Text.Encoding]::UTF8).Contains($dash)
    })
if ($dashed.Count -gt 0) { throw ('Em-dash found in: {0}' -f ($dashed -join ', ')) }

Write-Information -MessageData ('All gates passed: {0} PowerShell file(s), {1} test(s).' -f $powershellFiles.Count, $result.PassedCount) -InformationAction Continue
exit 0
