<#
.SYNOPSIS
    Compares two inventory files and lists what was added, removed, changed or only touched.

.PARAMETER ReferencePath
    The earlier inventory.

.PARAMETER DifferencePath
    The later inventory.

.PARAMETER IncludeUnchanged
    Also lists identical entries.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Compare-Inventory.ps1 -ReferencePath .\out\A-before-V01.inventory.json -DifferencePath .\out\A-after-V01.inventory.json
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

Compare-XivInventory -ReferencePath $ReferencePath -DifferencePath $DifferencePath -IncludeUnchanged:$IncludeUnchanged |
    Format-Table -AutoSize -Property Status, RelativePath, ReferenceLength, DifferenceLength |
    Out-String -Width 400
exit 0
