<#
.SYNOPSIS
    Runs PSScriptAnalyzer over the given files in a separate Windows PowerShell process, and
    starts a fresh process when the analyzer is known to have broken its own.

.DESCRIPTION
    Used by build.ps1 and by the CI workflow, so both run the same analysis.

    In CI, PSScriptAnalyzer (1.24.0 and 1.25.0 alike) intermittently fails with "The term
    'Get-Command' is not recognized". Once it appears, every later analysis in the same process
    fails the same way, and in one CI job it came back in fresh processes too (docs/decisions.md,
    2026-11-04). So the analysis runs in a child process (a worker) that writes one result line per
    file as it goes, and this script decides:

    - A file whose only analyzer errors are that one: this file and every file after it are
      analysed again in a fresh worker. Results the broken worker gave before it are kept. Every
      worker after the first gets a module analysis cache of its own, empty, instead of the one
      the machine shares; that costs time, so the first worker keeps the shared one.
    - A worker that ends or is stopped (timeout) before reporting every file: the files it did not
      report are analysed again in a fresh worker.
    - Any other analyzer error, an unreadable result, or a worker that cannot load the analyzer:
      fails at once, without a retry.
    - A file that needs more than MaxAttempts workers: fails.

    The script fails on any finding of severity Error, Warning or ParseError, on any suppression
    without a Justification, and on any analyzer error that remains (R-23).

    For the tests only: when the environment variable XIVCS_ANALYZER_FAKE names a plan file, the
    worker does not run the analyzer but plays the plan (see tests/ProjectAnalyzer.Tests.ps1). The
    script refuses that in GitHub Actions.

.PARAMETER Path
    The files to analyse.

.PARAMETER SettingsPath
    PSScriptAnalyzerSettings.psd1.

.PARAMETER ModulePath
    The PSScriptAnalyzer.psd1 to load, from the restored pinned modules.

.PARAMETER MaxAttempts
    How many workers a single file may need before the script gives up.

.PARAMETER TimeoutSeconds
    How long one worker may run before it is stopped.

.EXAMPLE
    .\Invoke-ProjectAnalyzer.ps1 -Path .\build.ps1 -SettingsPath .\PSScriptAnalyzerSettings.psd1 -ModulePath .\.modules\PSScriptAnalyzer\1.25.0\PSScriptAnalyzer.psd1
#>
#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Run')]
param (
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')]
    [string[]] $Path,

    [Parameter(Mandatory = $true, ParameterSetName = 'Run')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Worker')]
    [string] $SettingsPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Run')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Worker')]
    [string] $ModulePath,

    [Parameter(ParameterSetName = 'Run')]
    [ValidateRange(1, 10)]
    [int] $MaxAttempts = 3,

    [Parameter(ParameterSetName = 'Run')]
    [ValidateRange(1, 3600)]
    [int] $TimeoutSeconds = 600,

    # Internal: the file that lists the files a worker analyses, one full path per line.
    [Parameter(Mandatory = $true, ParameterSetName = 'Worker')]
    [string] $WorkerListPath,

    # Internal: the file a worker appends its result lines to.
    [Parameter(Mandatory = $true, ParameterSetName = 'Worker')]
    [string] $WorkerResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'Worker') {
    # Loaded by path, so the worker's own commands need no command discovery: with an empty module
    # analysis cache, discovery scans every module on the machine, which took about 20 s per
    # worker on the CI runner (2026-11-04). The path is built without Join-Path, which lives in
    # one of these modules.
    foreach ($baseModule in 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Management') {
        Import-Module -Name ('{0}\Modules\{1}\{1}.psd1' -f $PSHOME, $baseModule)
    }
}

$transient = "The term 'Get-Command' is not recognized"
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Test-TransientError {
    [CmdletBinding()]
    [OutputType([bool])]
    param (
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Message
    )

    $other = @($Message | Where-Object { -not $_.StartsWith($transient, [System.StringComparison]::Ordinal) })
    $Message.Count -gt 0 -and $other.Count -eq 0
}

function ConvertTo-ErrorText {
    [CmdletBinding()]
    [OutputType([string])]
    param (
        [Parameter(Mandatory = $true)]
        [object] $ErrorRecord
    )

    # The known fault is recognised by its exception, not by its text, which Windows shows in the
    # language of the machine: on a German one the message did not match and the fault failed the
    # gate at once (2026-11-05). It is reported under the one English text the caller looks for.
    $exception = $null
    if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $exception = $ErrorRecord.Exception }
    if ($exception -is [System.Management.Automation.CommandNotFoundException] -and
        ($exception.CommandName -eq 'Get-Command' -or $exception.Message -match '\WGet-Command\W')) {
        return ('{0} ({1})' -f $transient, $exception.Message)
    }
    [string]$ErrorRecord
}

function Add-ResultLine {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string] $ResultPath,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Record
    )

    # One line per call and nothing else, so a worker that dies mid-way leaves at most one
    # unfinished last line, which the caller treats as missing.
    [System.IO.File]::AppendAllText($ResultPath, ((ConvertTo-Json -InputObject $Record -Compress -Depth 5) + "`n"), $utf8)
}

function Invoke-FakeWorker {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string[]] $File,

        [Parameter(Mandatory = $true)]
        [string] $ResultPath
    )

    # Plays one entry of the plan per worker start: Mode is ok, transient, localised, other, finding,
    # unexplained, crash, hang or fatal; At is the position in this worker's list where it happens.
    # Windows PowerShell 5.1 passes a JSON array on as one object; ForEach-Object unrolls it.
    $plan = @(ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($env:XIVCS_ANALYZER_FAKE, $utf8)) | ForEach-Object { $_ })
    $counter = $env:XIVCS_ANALYZER_FAKE + '.count'
    $attempt = 0
    if (Test-Path -LiteralPath $counter) { $attempt = @([System.IO.File]::ReadAllLines($counter, $utf8)).Count }
    [System.IO.File]::AppendAllText($counter, ((@($File | ForEach-Object { Split-Path -Path $_ -Leaf }) -join ',') + "`n"), $utf8)
    [System.IO.File]::AppendAllText($env:XIVCS_ANALYZER_FAKE + '.cache', ([string]$env:PSModuleAnalysisCachePath + "`n"), $utf8)
    $step = $plan[[System.Math]::Min($attempt, $plan.Count - 1)]
    $at = 0
    if ($step.PSObject.Properties['At']) { $at = [int]$step.At }

    if ($step.Mode -eq 'fatal') {
        Add-ResultLine -ResultPath $ResultPath -Record ([ordered]@{ Fatal = 'cannot load the analyzer: fake' })
        exit 3
    }
    if ($step.Mode -eq 'hang') { Start-Sleep -Seconds 600 }
    for ($i = 0; $i -lt $File.Count; $i++) {
        $record = [ordered]@{ File = $File[$i]; Findings = @(); Unexplained = @(); Errors = @() }
        if ($i -ge $at) {
            switch ($step.Mode) {
                'crash' { exit 5 }
                'transient' { $record.Errors = @("$transient as the name of a cmdlet (fake)") }
                'localised' {
                    # The fault as a German Windows reports it, through the same conversion as a real
                    # worker.
                    $exception = [System.Management.Automation.CommandNotFoundException]::new('Die Benennung "Get-Command" wurde nicht als Name eines Cmdlet erkannt. (fake)')
                    $record.Errors = @(ConvertTo-ErrorText -ErrorRecord ([System.Management.Automation.ErrorRecord]::new($exception, 'RULE_ERROR', 'ObjectNotFound', $null)))
                }
                'other' { if ($i -eq $at) { $record.Errors = @('Object reference not set to an instance of an object. (fake)') } }
                'finding' { if ($i -eq $at) { $record.Findings = @([ordered]@{ Line = 1; RuleName = 'FakeRule'; Severity = 'Warning'; Message = 'fake' }) } }
                'unexplained' { if ($i -eq $at) { $record.Unexplained = @([ordered]@{ Line = 1; RuleName = 'FakeRule' }) } }
            }
        }
        Add-ResultLine -ResultPath $ResultPath -Record $record
    }
    exit 0
}

function Invoke-Worker {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string] $ListPath,

        [Parameter(Mandatory = $true)]
        [string] $ResultPath,

        [Parameter(Mandatory = $true)]
        [string] $SettingsPath,

        [Parameter(Mandatory = $true)]
        [string] $ModulePath
    )

    $files = @([System.IO.File]::ReadAllLines($ListPath, $utf8) | Where-Object { $_ })
    if ($env:XIVCS_ANALYZER_FAKE) { Invoke-FakeWorker -File $files -ResultPath $ResultPath }

    try {
        Import-Module -Name $ModulePath -ErrorAction Stop
    } catch {
        Add-ResultLine -ResultPath $ResultPath -Record ([ordered]@{ Fatal = ('cannot load the analyzer: {0}' -f $_.Exception.Message) })
        exit 3
    }

    # Warm-up, results discarded: on the first analyses in a fresh process, UseCorrectCasing in
    # 1.25.0 intermittently throws a NullReferenceException (docs/decisions.md, 2026-11-03).
    foreach ($file in $files) {
        $null = Invoke-ScriptAnalyzer -Path $file -Settings $SettingsPath -ErrorAction SilentlyContinue
        $null = Invoke-ScriptAnalyzer -Path $file -Settings $SettingsPath -SuppressedOnly -ErrorAction SilentlyContinue
    }

    foreach ($file in $files) {
        $mainErrors = $null
        $found = @(Invoke-ScriptAnalyzer -Path $file -Settings $SettingsPath -ErrorVariable mainErrors -ErrorAction SilentlyContinue)
        # A suppression is an exception and carries its reason. PSScriptAnalyzer hides a finding
        # suppressed without a Justification all the same, so the reason is required here.
        $suppressedErrors = $null
        $suppressed = @(Invoke-ScriptAnalyzer -Path $file -Settings $SettingsPath -SuppressedOnly -ErrorVariable suppressedErrors -ErrorAction SilentlyContinue)
        Add-ResultLine -ResultPath $ResultPath -Record ([ordered]@{
                File        = $file
                Findings    = @($found | ForEach-Object { [ordered]@{ Line = $_.Line; RuleName = $_.RuleName; Severity = [string]$_.Severity; Message = $_.Message } })
                Unexplained = @($suppressed | Where-Object { [string]::IsNullOrWhiteSpace($_.Justification) } | ForEach-Object { [ordered]@{ Line = $_.Line; RuleName = $_.RuleName } })
                Errors      = @(@($mainErrors) + @($suppressedErrors) | Where-Object { $_ } | ForEach-Object { ConvertTo-ErrorText -ErrorRecord $_ })
            })
    }
    exit 0
}

function Invoke-WorkerProcess {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param (
        [Parameter(Mandatory = $true)]
        [string[]] $File,

        [Parameter(Mandatory = $true)]
        [string] $WorkDirectory,

        [Parameter(Mandatory = $true)]
        [int] $Number,

        [Parameter(Mandatory = $true)]
        [string] $SettingsPath,

        [Parameter(Mandatory = $true)]
        [string] $ModulePath,

        [Parameter(Mandatory = $true)]
        [int] $TimeoutSeconds,

        [switch] $PrivateCache
    )

    $list = Join-Path -Path $WorkDirectory -ChildPath ('worker{0}.list' -f $Number)
    $results = Join-Path -Path $WorkDirectory -ChildPath ('worker{0}.jsonl' -f $Number)
    $stdout = Join-Path -Path $WorkDirectory -ChildPath ('worker{0}.out' -f $Number)
    $stderr = Join-Path -Path $WorkDirectory -ChildPath ('worker{0}.err' -f $Number)
    [System.IO.File]::WriteAllLines($list, $File, $utf8)

    # Every path is quoted: Start-Process joins the arguments with spaces.
    $arguments = @(
        '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-SettingsPath', ('"{0}"' -f $SettingsPath),
        '-ModulePath', ('"{0}"' -f $ModulePath),
        '-WorkerListPath', ('"{0}"' -f $list),
        '-WorkerResultPath', ('"{0}"' -f $results)
    )
    $start = @{
        FilePath               = Join-Path -Path $PSHOME -ChildPath 'powershell.exe'
        ArgumentList           = $arguments
        NoNewWindow            = $true
        PassThru               = $true
        RedirectStandardOutput = $stdout
        RedirectStandardError  = $stderr
    }
    # In CI the fault stayed in every fresh worker of one job (2026-11-04), so it lives in state the
    # processes share. The module analysis cache is such state: the CI image points
    # PSModuleAnalysisCachePath at one pre-built cache for the whole machine. A worker after a
    # failure therefore gets an empty cache of its own. The worker inherits the variable from this
    # process, so it is set only around the start.
    $savedCache = $env:PSModuleAnalysisCachePath
    if ($PrivateCache) {
        $env:PSModuleAnalysisCachePath = Join-Path -Path $WorkDirectory -ChildPath ('worker{0}.analysiscache' -f $Number)
    }
    try {
        $process = Start-Process @start
    } finally {
        $env:PSModuleAnalysisCachePath = $savedCache
    }
    # Windows PowerShell 5.1 reports no exit code unless the handle was taken while the process
    # ran.
    $null = $process.Handle
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill() } catch { Write-Verbose -Message ('Stopping the worker failed: {0}' -f $_.Exception.Message) }
        if (-not $process.WaitForExit(30000)) {
            throw ('The analyzer worker (process {0}) ran longer than {1} s and could not be stopped.' -f $process.Id, $TimeoutSeconds)
        }
    }
    $exitCode = $process.ExitCode

    $lines = @()
    if (Test-Path -LiteralPath $results) { $lines = @([System.IO.File]::ReadAllLines($results, $utf8) | Where-Object { $_ }) }
    $records = New-Object -TypeName System.Collections.Generic.List[object]
    $fatal = $null
    foreach ($line in $lines) {
        try {
            $record = ConvertFrom-Json -InputObject $line
        } catch {
            # Only an unfinished last line can be unreadable; anything after it does not exist.
            break
        }
        if ($record.PSObject.Properties['Fatal']) {
            $fatal = [string]$record.Fatal
            break
        }
        $records.Add($record)
    }
    $errorText = ''
    if (Test-Path -LiteralPath $stderr) { $errorText = [System.IO.File]::ReadAllText($stderr).Trim() }
    @{ Records = $records.ToArray(); Fatal = $fatal; ExitCode = $exitCode; TimedOut = $timedOut; ErrorText = $errorText }
}

function Invoke-Analysis {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Path,

        [Parameter(Mandatory = $true)]
        [string] $SettingsPath,

        [Parameter(Mandatory = $true)]
        [string] $ModulePath,

        [Parameter(Mandatory = $true)]
        [int] $MaxAttempts,

        [Parameter(Mandatory = $true)]
        [int] $TimeoutSeconds
    )

    if ($env:XIVCS_ANALYZER_FAKE) {
        if ($env:GITHUB_ACTIONS -eq 'true') { throw 'XIVCS_ANALYZER_FAKE is set in CI, which would replace the analysis with a fake.' }
        Write-Warning -Message 'XIVCS_ANALYZER_FAKE is set: the analyzer does not run, a test plan is played.'
    }
    if ($Path.Count -eq 0) { throw 'No files given, so nothing would be analysed (R-23).' }
    foreach ($required in @($SettingsPath, $ModulePath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw ('Not found: {0}' -f $required) }
    }
    $SettingsPath = (Resolve-Path -LiteralPath $SettingsPath).ProviderPath
    $ModulePath = (Resolve-Path -LiteralPath $ModulePath).ProviderPath

    # Full paths for the worker, the given spelling for the report.
    $display = @{}
    $pending = New-Object -TypeName System.Collections.Generic.List[string]
    foreach ($item in $Path) {
        if (-not (Test-Path -LiteralPath $item -PathType Leaf)) { throw ('Not found: {0}' -f $item) }
        $full = (Resolve-Path -LiteralPath $item).ProviderPath
        if ($display.ContainsKey($full)) { continue }
        $display[$full] = $item
        $pending.Add($full)
    }

    $attempts = @{}
    $done = New-Object -TypeName System.Collections.Generic.List[object]
    $workDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('xivcs-analyzer-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $workDirectory
    $number = 0
    try {
        while ($pending.Count -gt 0) {
            $number++
            $worker = @{
                File           = $pending.ToArray()
                WorkDirectory  = $workDirectory
                Number         = $number
                SettingsPath   = $SettingsPath
                ModulePath     = $ModulePath
                TimeoutSeconds = $TimeoutSeconds
                PrivateCache   = $number -gt 1
            }
            $run = Invoke-WorkerProcess @worker
            if ($null -ne $run.Fatal) {
                throw ('The analyzer worker could not start: {0}' -f $run.Fatal)
            }

            # Keep the results up to the first one the known fault spoiled; that file and all after
            # it go to a fresh worker.
            $kept = 0
            $reason = $null
            foreach ($record in $run.Records) {
                if ($kept -ge $pending.Count -or [string]$record.File -ne $pending[$kept]) {
                    throw ('The analyzer worker reported {0} where {1} was expected.' -f $record.File, $pending[[System.Math]::Min($kept, $pending.Count - 1)])
                }
                if (Test-TransientError -Message @($record.Errors | ForEach-Object { [string]$_ })) {
                    $reason = 'analyzer error "{0}"' -f $transient
                    break
                }
                $done.Add($record)
                $kept++
            }
            $pending.RemoveRange(0, $kept)
            if ($pending.Count -eq 0 -and ($run.TimedOut -or $run.ExitCode -ne 0)) {
                # Every file was reported before the worker failed, so its results stand.
                Write-Information -MessageData ('The analyzer worker ended with exit code {0} after reporting every file; its results are used.' -f $run.ExitCode) -InformationAction Continue
            }
            if ($pending.Count -gt 0) {
                if ($null -eq $reason) {
                    if ($run.TimedOut) {
                        $reason = 'no result within {0} s, the worker was stopped' -f $TimeoutSeconds
                    } else {
                        $reason = 'the worker ended with exit code {0} before reporting it' -f $run.ExitCode
                    }
                    if ($run.ErrorText) { $reason += (': {0}' -f $run.ErrorText) }
                }
                # Counted for the file the worker stopped at, so MaxAttempts is the number of
                # workers that tried that file.
                $stuck = $pending[0]
                $attempts[$stuck] = 1 + [int]$attempts[$stuck]
                if ($attempts[$stuck] -ge $MaxAttempts) {
                    throw ('{0}: {1}. It could not be analysed in {2} worker(s).' -f $display[$stuck], $reason, $attempts[$stuck])
                }
                Write-Information -MessageData ('{0}: {1}; analysing it and the {2} file(s) after it in a fresh worker.' -f $display[$stuck], $reason, ($pending.Count - 1)) -InformationAction Continue
            }
        }
    } finally {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    $failed = $false
    foreach ($record in $done) {
        $name = $display[[string]$record.File]
        foreach ($finding in @($record.Findings)) {
            '{0}:{1} {2} [{3}] {4}' -f $name, $finding.Line, $finding.RuleName, $finding.Severity, $finding.Message
            if ($finding.Severity -in 'Error', 'Warning', 'ParseError') { $failed = $true }
        }
        foreach ($suppression in @($record.Unexplained)) {
            '{0}:{1} {2} suppressed without a Justification' -f $name, $suppression.Line, $suppression.RuleName
            $failed = $true
        }
        # A crashed rule is a non-terminating analyzer error. Counting findings alone would pass
        # over it, so any analyzer error that remains fails (R-23).
        foreach ($message in @($record.Errors)) {
            'analyzer error in {0}: {1}' -f $name, $message
            $failed = $true
        }
    }
    if ($failed) { throw 'PSScriptAnalyzer reported issues (C-03).' }
    Write-Information -MessageData ('PSScriptAnalyzer: {0} file(s), no issue, {1} worker(s).' -f $done.Count, $number) -InformationAction Continue
}

if ($PSCmdlet.ParameterSetName -eq 'Worker') {
    Invoke-Worker -ListPath $WorkerListPath -ResultPath $WorkerResultPath -SettingsPath $SettingsPath -ModulePath $ModulePath
} else {
    Invoke-Analysis -Path $Path -SettingsPath $SettingsPath -ModulePath $ModulePath -MaxAttempts $MaxAttempts -TimeoutSeconds $TimeoutSeconds
}
