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
# In a separate process, with a fresh one when the analyzer breaks its own; see the script.
$analyzerPin = $manifest.Modules | Where-Object { $_.Name -eq 'PSScriptAnalyzer' }
$analyzer = @{
    Path         = @($powershellFiles | ForEach-Object { Join-Path -Path $root -ChildPath $_ })
    SettingsPath = Join-Path -Path $root -ChildPath 'PSScriptAnalyzerSettings.psd1'
    ModulePath   = Join-Path -Path $root -ChildPath ('.modules\PSScriptAnalyzer\{0}\PSScriptAnalyzer.psd1' -f $analyzerPin.Version)
}
& (Join-Path -Path $root -ChildPath 'Invoke-ProjectAnalyzer.ps1') @analyzer

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

Write-Information -MessageData 'Gate: .NET (format, build, tests, XAML, vulnerable packages)' -InformationAction Continue
# The two halves of the C# check (C# profile), the XAML check (WPF profile), the tests and the
# dependency audit. Every native call is checked by its exit code.
function Invoke-Native {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string] $Description,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('{0} failed with exit code {1}.' -f $Description, $LASTEXITCODE) }
}

$solution = Join-Path -Path $root -ChildPath 'XivConfigSync.slnx'
Invoke-Native -Description 'dotnet tool restore' -Arguments 'tool', 'restore', '--tool-manifest', (Join-Path -Path $root -ChildPath '.config\dotnet-tools.json')
Invoke-Native -Description 'dotnet restore' -Arguments 'restore', $solution, '--locked-mode'
Invoke-Native -Description 'dotnet format whitespace' -Arguments 'format', 'whitespace', $solution, '--verify-no-changes', '--no-restore'
Invoke-Native -Description 'dotnet build' -Arguments 'build', $solution, '--no-restore', '--configuration', 'Release', '-nologo'
Invoke-Native -Description 'dotnet test' -Arguments 'test', '--project', (Join-Path -Path $root -ChildPath 'tests\XivConfigSync.Core.Tests\XivConfigSync.Core.Tests.csproj'), '--no-build', '--configuration', 'Release'
Invoke-Native -Description 'XamlStyler' -Arguments 'xstyler', '--passive', '-d', (Join-Path -Path $root -ChildPath 'src'), '-r', '--config', (Join-Path -Path $root -ChildPath 'Settings.XamlStyler')
$vulnerable = & dotnet list $solution package --vulnerable --include-transitive 2>&1
if ($LASTEXITCODE -ne 0) { throw 'dotnet list package --vulnerable failed.' }
if (($vulnerable | Out-String) -match 'has the following vulnerable packages') { throw ('Vulnerable packages: {0}' -f ($vulnerable | Out-String)) }

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
