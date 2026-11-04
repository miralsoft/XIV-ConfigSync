<#
.SYNOPSIS
    Compares two text settings files of the game, such as FFXIV.cfg, key by key.

.DESCRIPTION
    Read-only. Lists every key that was added, removed or changed between the two files, with
    section and both values. The files usually come from two snapshots written by
    Export-Inventory.ps1.

.PARAMETER ReferencePath
    The earlier file.

.PARAMETER DifferencePath
    The later file.

.PARAMETER IncludeUnchanged
    Also lists keys with the same value in both.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Compare-ConfigText.ps1 -ReferencePath .\out\A-before-V01.cfg\FFXIV.cfg -DifferencePath .\out\A-after-V01.cfg\FFXIV.cfg
#>
#Requires -Version 5.1
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string] $ReferencePath,

    [Parameter(Mandatory = $true)]
    [string] $DifferencePath,

    [Parameter()]
    [switch] $IncludeUnchanged
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'XivInventory.psm1') -Force

Compare-XivConfigText -ReferencePath $ReferencePath -DifferencePath $DifferencePath -IncludeUnchanged:$IncludeUnchanged |
    Format-Table -AutoSize -Property Status, Section, Key, ReferenceValue, DifferenceValue |
    Out-String -Width 400
exit 0
