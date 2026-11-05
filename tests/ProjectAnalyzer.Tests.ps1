#Requires -Version 5.1
# Tests for Invoke-ProjectAnalyzer.ps1. Most play a fake worker (XIVCS_ANALYZER_FAKE), so every
# failure the script must handle is shown to be handled, without waiting for the real analyzer to
# fail by chance. Two tests run the real analyzer, to show the script works beyond the fake.

[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification = 'Variables set in BeforeAll and BeforeEach are read in the It blocks, which the analyzer does not follow.')]
param ()

BeforeAll {
    $root = Split-Path -Path $PSScriptRoot -Parent
    $analyzerScript = Join-Path -Path $root -ChildPath 'Invoke-ProjectAnalyzer.ps1'
    $settings = Join-Path -Path $root -ChildPath 'PSScriptAnalyzerSettings.psd1'
    $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path -Path $root -ChildPath 'PinnedModules.psd1')
    $pin = $manifest.Modules | Where-Object { $_.Name -eq 'PSScriptAnalyzer' }
    $module = Join-Path -Path $root -ChildPath ('.modules\PSScriptAnalyzer\{0}\PSScriptAnalyzer.psd1' -f $pin.Version)
    # Filled while the script runs, so the lines written before a failure are kept.
    $captured = New-Object -TypeName System.Collections.Generic.List[string]
    $bom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $true

    function Invoke-Analyzer {
        param (
            [string[]] $Path,
            [int] $TimeoutSeconds = 600
        )
        $captured.Clear()
        & $analyzerScript -Path $Path -SettingsPath $settings -ModulePath $module -TimeoutSeconds $TimeoutSeconds -WarningAction SilentlyContinue 6>&1 |
            ForEach-Object { $captured.Add([string]$_) }
    }

    function Invoke-Fake {
        param (
            [object[]] $Plan,
            [int] $TimeoutSeconds = 60,
            [switch] $InCi
        )
        $planPath = Join-Path -Path $TestDrive -ChildPath 'plan.json'
        Remove-Item -LiteralPath ($planPath + '.count'), ($planPath + '.cache'), ($planPath + '.modulepath') -ErrorAction SilentlyContinue
        [System.IO.File]::WriteAllText($planPath, (ConvertTo-Json -InputObject @($Plan) -Depth 3))
        # The tests run in CI too, where the script refuses the fake unless told otherwise here.
        $savedCi = $env:GITHUB_ACTIONS
        $env:GITHUB_ACTIONS = if ($InCi) { 'true' } else { $null }
        $env:XIVCS_ANALYZER_FAKE = $planPath
        try {
            Invoke-Analyzer -Path $files -TimeoutSeconds $TimeoutSeconds
        } finally {
            $env:XIVCS_ANALYZER_FAKE = $null
            $env:GITHUB_ACTIONS = $savedCi
        }
    }

    function Get-WorkerList {
        @(Get-Content -LiteralPath (Join-Path -Path $TestDrive -ChildPath 'plan.json.count'))
    }
}

Describe 'Invoke-ProjectAnalyzer.ps1 with a fake worker' {
    BeforeEach {
        $files = @('a.ps1', 'b.ps1', 'c.ps1' | ForEach-Object {
                $file = Join-Path -Path $TestDrive -ChildPath $_
                [System.IO.File]::WriteAllText($file, "# empty`r`n", $bom)
                $file
            })
    }

    It 'passes a clean run with one worker' {
        { Invoke-Fake -Plan @(@{ Mode = 'ok' }) } | Should -Not -Throw
        Get-WorkerList | Should -Be @('a.ps1,b.ps1,c.ps1')
        $captured -join "`n" | Should -Match '3 file\(s\), no issue, 1 worker\(s\)'
    }

    It 'keeps the results before the known fault and analyses the rest in a fresh worker' {
        { Invoke-Fake -Plan @(@{ Mode = 'transient'; At = 1 }, @{ Mode = 'ok' }) } | Should -Not -Throw
        Get-WorkerList | Should -Be @('a.ps1,b.ps1,c.ps1', 'b.ps1,c.ps1')
        $captured -join "`n" | Should -Match 'b\.ps1: analyzer error "The term .Get-Command. is not recognized"; analysing it and the 1 file'
    }

    It 'fails when the known fault persists for one file' {
        { Invoke-Fake -Plan @(@{ Mode = 'transient'; At = 0 }) } | Should -Throw '*a.ps1*could not be analysed in 3 worker(s)*'
        @(Get-WorkerList).Count | Should -Be 3
    }

    It 'counts the workers per file, not from the first worker' {
        # At counts within each worker's own list: b.ps1 is second in the first, first after that.
        { Invoke-Fake -Plan @(@{ Mode = 'transient'; At = 1 }, @{ Mode = 'transient'; At = 0 }) } | Should -Throw '*b.ps1*could not be analysed in 3 worker(s)*'
        Get-WorkerList | Should -Be @('a.ps1,b.ps1,c.ps1', 'b.ps1,c.ps1', 'b.ps1,c.ps1')
    }

    It 'keeps the shared module cache for the first worker and gives later ones their own' {
        $shared = [string]$env:PSModuleAnalysisCachePath
        { Invoke-Fake -Plan @(@{ Mode = 'transient'; At = 1 }, @{ Mode = 'transient'; At = 0 }, @{ Mode = 'ok' }) } | Should -Not -Throw
        $caches = @(Get-Content -LiteralPath (Join-Path -Path $TestDrive -ChildPath 'plan.json.cache'))
        $caches.Count | Should -Be 3
        $caches[0] | Should -Be $shared
        $caches[1] | Should -Match 'worker2\.analysiscache$'
        $caches[2] | Should -Match 'worker3\.analysiscache$'
        [string]$env:PSModuleAnalysisCachePath | Should -Be $shared
    }

    It 'lets every worker search only the modules of Windows PowerShell and the pinned ones' {
        $own = $env:PSModulePath
        { Invoke-Fake -Plan @(@{ Mode = 'transient'; At = 1 }, @{ Mode = 'ok' }) } | Should -Not -Throw
        $expected = '{0}\Modules;{1}' -f $PSHOME, (Join-Path -Path $root -ChildPath '.modules')
        Get-Content -LiteralPath (Join-Path -Path $TestDrive -ChildPath 'plan.json.modulepath') | Should -Be @($expected, $expected)
        $env:PSModulePath | Should -Be $own
    }

    It 'analyses again what a crashed worker did not report' {
        { Invoke-Fake -Plan @(@{ Mode = 'crash'; At = 2 }, @{ Mode = 'ok' }) } | Should -Not -Throw
        Get-WorkerList | Should -Be @('a.ps1,b.ps1,c.ps1', 'c.ps1')
        $captured -join "`n" | Should -Match 'c\.ps1: the worker ended with exit code 5'
    }

    It 'fails when a worker keeps crashing' {
        { Invoke-Fake -Plan @(@{ Mode = 'crash'; At = 0 }) } | Should -Throw '*a.ps1*could not be analysed in 3 worker(s)*'
    }

    It 'stops a worker that hangs and analyses again in a fresh one' {
        # Ten seconds leave a fresh worker on a slow CI runner time to start.
        { Invoke-Fake -Plan @(@{ Mode = 'hang' }, @{ Mode = 'ok' }) -TimeoutSeconds 10 } | Should -Not -Throw
        @(Get-WorkerList).Count | Should -Be 2
        $captured -join "`n" | Should -Match 'no result within 10 s'
    }

    It 'fails at once on any other analyzer error' {
        { Invoke-Fake -Plan @(@{ Mode = 'other'; At = 1 }, @{ Mode = 'ok' }) } | Should -Throw '*reported issues*'
        @(Get-WorkerList).Count | Should -Be 1
        $captured -join "`n" | Should -Match 'analyzer error in .*b\.ps1: Object reference'
    }

    It 'fails at once when the worker cannot load the analyzer' {
        { Invoke-Fake -Plan @(@{ Mode = 'fatal' }, @{ Mode = 'ok' }) } | Should -Throw '*could not start*'
        @(Get-WorkerList).Count | Should -Be 1
    }

    It 'fails on a finding and names it' {
        { Invoke-Fake -Plan @(@{ Mode = 'finding'; At = 2 }) } | Should -Throw '*reported issues*'
        $captured -join "`n" | Should -Match 'c\.ps1:1 FakeRule \[Warning\]'
    }

    It 'fails on a suppression without a Justification' {
        { Invoke-Fake -Plan @(@{ Mode = 'unexplained'; At = 0 }) } | Should -Throw '*reported issues*'
        $captured -join "`n" | Should -Match 'a\.ps1:1 FakeRule suppressed without a Justification'
    }

    It 'refuses the fake in GitHub Actions' {
        { Invoke-Fake -Plan @(@{ Mode = 'ok' }) -InCi } | Should -Throw '*set in CI*'
    }

    It 'refuses a missing file' {
        { Invoke-Analyzer -Path (Join-Path -Path $TestDrive -ChildPath 'missing.ps1') } | Should -Throw '*Not found*'
    }
}

Describe 'Invoke-ProjectAnalyzer.ps1 with the real analyzer' {
    It 'passes a file in the normal form' {
        $file = Join-Path -Path $TestDrive -ChildPath 'clean.ps1'
        [System.IO.File]::WriteAllText($file, "Set-StrictMode -Version Latest`r`nGet-ChildItem -Path .`r`n", $bom)
        { Invoke-Analyzer -Path $file } | Should -Not -Throw
    }

    It 'fails a file with a finding and names it' {
        $file = Join-Path -Path $TestDrive -ChildPath 'alias.ps1'
        [System.IO.File]::WriteAllText($file, "gci .`r`n", $bom)
        { Invoke-Analyzer -Path $file } | Should -Throw '*reported issues*'
        $captured -join "`n" | Should -Match 'alias\.ps1:1 PSAvoidUsingCmdletAliases \[Warning\]'
    }
}
