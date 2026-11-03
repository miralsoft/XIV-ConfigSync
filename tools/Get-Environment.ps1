<#
.SYNOPSIS
    Reports where the game and plugin configuration live and whether OneDrive already syncs them.

.DESCRIPTION
    Read-only. Reads no file contents.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Get-Environment.ps1
#>
#Requires -Version 5.1
[CmdletBinding()]
param ()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'XivInventory.psm1') -Force

Get-XivEnvironment | Format-List | Out-String -Width 400
exit 0
