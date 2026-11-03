<#
.SYNOPSIS
    Restores the PowerShell modules a project pins, verifying each package before it is used.

.DESCRIPTION
    PowerShell has no lock file. This script is the substitute: a committed manifest names every
    module with its exact version and the SHA-512 the PowerShell Gallery publishes for that
    package, and this script fetches each package, verifies the hash of exactly the bytes it then
    extracts (S-17), and places the module where it is imported from by path. It never calls
    Install-Module, which writes into the user's module path and resolves versions by itself.

    Copied from: miralsoft-foundation-docs, enforcement/powershell/Restore-PinnedModules.ps1
    Foundation version: 5.1.0
    Copied on: 2026-11-03

.PARAMETER ManifestPath
    The committed manifest, a .psd1 file with a Modules list of Name, Version and Sha512.

.PARAMETER Destination
    The folder modules are restored into, normally git-ignored. Each module lands in
    <Destination>\<Name>\<Version>.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Restore-PinnedModules.ps1 -ManifestPath .\PinnedModules.psd1 -Destination .\.modules
#>
#Requires -Version 5.1
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $Destination
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$manifest = Import-PowerShellDataFile -LiteralPath $ManifestPath
foreach ($module in $manifest.Modules) {
    $target = Join-Path -Path (Join-Path -Path $Destination -ChildPath $module.Name) -ChildPath $module.Version
    $marker = Join-Path -Path $target -ChildPath '.verified-sha512'

    # Idempotent (R-13): a module restored and verified before is left alone, as long as the
    # marker still names the hash the manifest pins. A changed pin restores again.
    if ((Test-Path -LiteralPath $marker) -and
        ((Get-Content -LiteralPath $marker -Raw).Trim() -eq $module.Sha512)) {
        Write-Verbose -Message ('{0} {1}: already restored' -f $module.Name, $module.Version)
        continue
    }

    $uri = 'https://www.powershellgallery.com/api/v2/package/{0}/{1}' -f $module.Name, $module.Version
    # Up to three attempts. The gallery redirects to a CDN, which once answered 404 for a package
    # that it served a minute later, measured; a single failed request would turn CI red with
    # nothing wrong. Every attempt that does answer is still verified below, so retrying cannot
    # let a different package through.
    $response = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $uri -UseBasicParsing
            break
        } catch {
            if ($attempt -eq 3) {
                throw ('{0} {1}: download failed three times: {2}' -f $module.Name, $module.Version, $_.Exception.Message)
            }
            Write-Verbose -Message ('{0} {1}: attempt {2} failed, retrying' -f $module.Name, $module.Version, $attempt)
            Start-Sleep -Seconds 5
        }
    }
    $bytes = $response.Content
    if ($bytes -isnot [byte[]]) {
        throw ('{0} {1}: the gallery did not answer with a package' -f $module.Name, $module.Version)
    }

    # The hash is computed over the bytes held in memory, and the same bytes are extracted.
    # Nothing is written to disk and read back in between, so what runs is what was verified.
    $sha512 = [System.Security.Cryptography.SHA512]::Create()
    try {
        $actual = [Convert]::ToBase64String($sha512.ComputeHash($bytes))
    } finally {
        $sha512.Dispose()
    }
    if ($actual -ne $module.Sha512) {
        throw ('{0} {1}: SHA-512 mismatch, nothing extracted. Expected {2}, got {3}' -f
            $module.Name, $module.Version, $module.Sha512, $actual)
    }

    $stream = New-Object -TypeName System.IO.MemoryStream -ArgumentList (, $bytes)
    $archive = New-Object -TypeName System.IO.Compression.ZipArchive -ArgumentList $stream
    try {
        # Windows PowerShell 5.1 runs on .NET Framework and stops at 260 characters per path
        # unless long paths are enabled for the system, which they were not on the machine this
        # was measured on. PSScriptAnalyzer 1.25.0 carries entries 95 characters long, so a long
        # destination fails halfway with "part of the path could not be found". Measured before
        # anything is written, so the refusal names the cause instead.
        $fullTarget = [System.IO.Path]::GetFullPath($target)
        $longest = ($archive.Entries | ForEach-Object -Process { $_.FullName.Length } |
                Measure-Object -Maximum).Maximum
        $length = $fullTarget.Length + 1 + $longest
        if ($length -ge 260) {
            throw ('{0} {1}: the destination is too long for Windows PowerShell 5.1, {2} characters for the longest file against a limit of 260. Restore into a shorter path.' -f
                $module.Name, $module.Version, $length)
        }
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        $null = New-Item -ItemType Directory -Path $target -Force
        [System.IO.Compression.ZipFileExtensions]::ExtractToDirectory($archive, $target)
    } finally {
        $archive.Dispose()
        $stream.Dispose()
    }
    Set-Content -LiteralPath $marker -Value $module.Sha512 -Encoding ASCII
    Write-Verbose -Message ('{0} {1}: verified and restored' -f $module.Name, $module.Version)
}
