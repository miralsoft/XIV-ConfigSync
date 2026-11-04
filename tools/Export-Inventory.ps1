<#
.SYNOPSIS
    Writes a read-only inventory of a configuration folder to a JSON file.

.DESCRIPTION
    Without -Path it inventories the game's configuration folder below Documents. Nothing is
    written to the inventoried folder. Character content IDs are replaced by pseudonyms unless
    -KeepIdentifiers is given.

.PARAMETER Path
    The folder to inventory. Defaults to the game's configuration folder.

.PARAMETER OutputPath
    The JSON file to write. It must not lie inside the inventoried folder.

.PARAMETER Label
    A free label, for example "A-before-V01".

.PARAMETER KeepIdentifiers
    Keeps character content IDs in folder names.

.PARAMETER NoConfigSnapshot
    Skips the copy of the folder's text settings files (*.cfg). By default they are copied into a
    folder next to the output file, named like it with the extension .cfg, so that two snapshots
    can be compared key by key with Compare-ConfigText.ps1.

.PARAMETER Force
    Overwrites an existing output file and snapshot.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Export-Inventory.ps1 -OutputPath .\out\A-before-V01.inventory.json -Label A-before-V01
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param (
    [Parameter()]
    [string] $Path = (Join-Path -Path ([Environment]::GetFolderPath('MyDocuments')) -ChildPath 'My Games\FINAL FANTASY XIV - A Realm Reborn'),

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter()]
    [string] $Label = '',

    [Parameter()]
    [switch] $KeepIdentifiers,

    [Parameter()]
    [switch] $NoConfigSnapshot,

    [Parameter()]
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'XivInventory.psm1') -Force

$written = Export-XivInventory -Path $Path -OutputPath $OutputPath -Label $Label -KeepIdentifiers:$KeepIdentifiers -Force:$Force -WhatIf:$WhatIfPreference
if ($written) {
    $written.FullName
}

# A folder with text settings files also gets a snapshot of them next to the inventory, so that a
# change can later be traced to the single setting behind it.
if (-not $NoConfigSnapshot -and @(Get-ChildItem -LiteralPath $Path -File -Force -Filter '*.cfg').Count -gt 0) {
    $full = [System.IO.Path]::GetFullPath($OutputPath)
    $snapshot = ($full -replace '(\.inventory)?\.json$', '') + '.cfg'
    $copied = @(Save-XivConfigSnapshot -Path $Path -OutputDirectory $snapshot -Force:$Force -WhatIf:$WhatIfPreference)
    if ($copied.Count -gt 0) {
        '{0} ({1} files)' -f $snapshot, $copied.Count
    }
}
exit 0
