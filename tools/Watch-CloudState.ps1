<#
.SYNOPSIS
    Records the cloud sync state of the files in one folder at a fixed interval.

.DESCRIPTION
    Read-only towards the observed folder: no file in it is opened. Every sample is one JSON line
    in the output file, holding the time and, per file, the attributes and the two shell sync
    properties reported by Get-XivCloudState. Meant for the OneDrive experiment, in a folder of its
    own, never in a folder with game data.

.PARAMETER Path
    The folder to observe.

.PARAMETER OutputPath
    The JSON Lines file to append to. It must not lie inside the observed folder.

.PARAMETER IntervalSeconds
    Seconds between two samples.

.PARAMETER Count
    Number of samples.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Watch-CloudState.ps1 -Path "$env:OneDriveCommercial\Games\ConfigSync-MA09" -OutputPath .\out\MA09.jsonl -IntervalSeconds 2 -Count 60
#>
#Requires -Version 5.1
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter()]
    [ValidateRange(1, 3600)]
    [int] $IntervalSeconds = 2,

    [Parameter()]
    [ValidateRange(1, 100000)]
    [int] $Count = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'XivInventory.psm1') -Force

$root = (Get-Item -LiteralPath $Path).FullName.TrimEnd('\')
$target = [System.IO.Path]::GetFullPath($OutputPath)
if ($target.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw ('Refusing to write inside the observed folder: {0}' -f $target)
}

$encoding = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
for ($sample = 1; $sample -le $Count; $sample++) {
    $line = ConvertTo-Json -Compress -Depth 4 -InputObject ([pscustomobject] @{
            Utc   = [DateTime]::UtcNow.ToString('o')
            Files = @(Get-XivCloudState -Path $root)
        })
    [System.IO.File]::AppendAllText($target, $line + "`n", $encoding)
    if ($sample -lt $Count) { Start-Sleep -Seconds $IntervalSeconds }
}
$target
exit 0
