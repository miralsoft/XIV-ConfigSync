#Requires -Version 5.1
# Read-only inventory of game and plugin configuration folders, for the feasibility analysis.
#
# Nothing in this module writes to the folder it inventories. Files are opened for reading with
# every share mode granted, so a running game is never blocked, and cloud placeholders are never
# opened, because reading one makes the sync client download it. The only function that writes
# is Export-XivInventory, and it refuses an output path inside the inventoried folder.

Set-StrictMode -Version Latest

# File attributes of the Windows cloud files API that .NET Framework does not name.
$script:RecallOnOpen = 0x00040000
$script:RecallOnDataAccess = 0x00400000
$script:InventorySchema = 1

function ConvertTo-XivPseudonym {
    <#
    .SYNOPSIS
        Replaces character folder identifiers in a relative path with a stable pseudonym.

    .DESCRIPTION
        Character folders are named FFXIV_CHR followed by sixteen hex digits, the character's
        content ID. The same ID always yields the same pseudonym, so inventories of two devices
        can still be compared, but the ID itself does not appear in the output.

    .PARAMETER RelativePath
        A path relative to the inventory root, with backslashes as separators.

    .EXAMPLE
        ConvertTo-XivPseudonym -RelativePath 'FFXIV_CHR0040000000000001\ADDON.DAT'
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param (
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $segments = $RelativePath -split '\\'
    $mapped = foreach ($segment in $segments) {
        if ($segment -match '^FFXIV_CHR([0-9A-Fa-f]{16})$') {
            $bytes = [System.Text.Encoding]::ASCII.GetBytes($Matches[1].ToUpperInvariant())
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $digest = $sha.ComputeHash($bytes)
            } finally {
                $sha.Dispose()
            }
            'FFXIV_CHR#' + (($digest[0..3] | ForEach-Object { $_.ToString('X2') }) -join '')
        } else {
            $segment
        }
    }
    $mapped -join '\'
}

function Test-XivCloudPlaceholder {
    <#
    .SYNOPSIS
        Tells whether opening a file would make a cloud sync client download it.

    .PARAMETER Attributes
        The file's attributes as an integer.

    .EXAMPLE
        Test-XivCloudPlaceholder -Attributes ([int] (Get-Item -LiteralPath $file).Attributes)
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param (
        [Parameter(Mandatory = $true)]
        [long] $Attributes
    )

    $offline = [long] [System.IO.FileAttributes]::Offline
    $mask = $offline -bor $script:RecallOnOpen -bor $script:RecallOnDataAccess
    ($Attributes -band $mask) -ne 0
}

function Get-XivFileHash {
    <#
    .SYNOPSIS
        Computes the SHA-256 of a file without blocking any other process that has it open.

    .PARAMETER LiteralPath
        The full path of the file.

    .EXAMPLE
        Get-XivFileHash -LiteralPath 'C:\data\FFXIV.cfg'
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param (
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = [System.IO.File]::Open($LiteralPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $digest = $sha.ComputeHash($stream)
        } finally {
            $sha.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
    ($digest | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Get-XivInventory {
    <#
    .SYNOPSIS
        Lists every file below a folder with size, SHA-256, time stamp and attributes.

    .DESCRIPTION
        Read-only. Directories whose name matches an exclusion are skipped entirely. Junctions and
        symbolic links are recorded but not followed. Cloud placeholders are recorded without a
        hash, so the inventory never triggers a download. A file that cannot be read is recorded
        with the error instead of a hash, and the inventory continues.

    .PARAMETER Path
        The folder to inventory.

    .PARAMETER ExcludeDirectory
        Directory names to skip, wildcards allowed. The defaults leave out screenshots, logs and
        downloads, which are large and say nothing about configuration.

    .PARAMETER KeepIdentifiers
        Keeps character content IDs in folder names instead of replacing them with a pseudonym.

    .EXAMPLE
        Get-XivInventory -Path "$([Environment]::GetFolderPath('MyDocuments'))\My Games\FINAL FANTASY XIV - A Realm Reborn"
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter()]
        [string[]] $ExcludeDirectory = @('screenshots', 'log', 'logs', 'downloads'),

        [Parameter()]
        [switch] $KeepIdentifiers
    )

    $root = (Get-Item -LiteralPath $Path -ErrorAction Stop).FullName.TrimEnd('\')
    $pending = New-Object -TypeName 'System.Collections.Generic.Stack[string]'
    $pending.Push($root)

    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            $relative = $item.FullName.Substring($root.Length + 1)
            $shown = if ($KeepIdentifiers) { $relative } else { ConvertTo-XivPseudonym -RelativePath $relative }

            if ($item.PSIsContainer) {
                $excluded = $false
                foreach ($pattern in $ExcludeDirectory) {
                    if ($item.Name -like $pattern) { $excluded = $true }
                }
                if ($excluded) { continue }
                if ($item.LinkType -in 'Junction', 'SymbolicLink') {
                    [pscustomobject] @{
                        RelativePath       = $shown
                        Kind               = 'Link'
                        Length             = $null
                        Sha256             = $null
                        LastWriteTimeUtc   = $item.LastWriteTimeUtc.ToString('o')
                        Attributes         = $item.Attributes.ToString()
                        IsCloudPlaceholder = $false
                        ReadError          = $null
                    }
                    continue
                }
                $pending.Push($item.FullName)
                continue
            }

            $attributes = [long] $item.Attributes
            $placeholder = Test-XivCloudPlaceholder -Attributes $attributes
            $hash = $null
            $readError = $null
            if (-not $placeholder) {
                try {
                    $hash = Get-XivFileHash -LiteralPath $item.FullName
                } catch {
                    $readError = $_.Exception.Message
                }
            }

            [pscustomobject] @{
                RelativePath       = $shown
                Kind               = 'File'
                Length             = $item.Length
                Sha256             = $hash
                LastWriteTimeUtc   = $item.LastWriteTimeUtc.ToString('o')
                Attributes         = $item.Attributes.ToString()
                IsCloudPlaceholder = $placeholder
                ReadError          = $readError
            }
        }
    }
}

function Export-XivInventory {
    <#
    .SYNOPSIS
        Writes the inventory of a folder to a JSON file.

    .DESCRIPTION
        The only function in this module that writes. It refuses an output path inside the
        inventoried folder, so the game's own folder is never touched, and an existing file
        unless -Force is given. The user's profile path is replaced by %USERPROFILE% in the
        recorded root.

    .PARAMETER Path
        The folder to inventory.

    .PARAMETER OutputPath
        The JSON file to write.

    .PARAMETER Label
        A free label for the device or the step, for example "A-before-V01".

    .PARAMETER ExcludeDirectory
        Passed on to Get-XivInventory.

    .PARAMETER KeepIdentifiers
        Passed on to Get-XivInventory.

    .PARAMETER Force
        Overwrites an existing output file.

    .EXAMPLE
        Export-XivInventory -Path $ffxiv -OutputPath .\out\A-before-V01.inventory.json -Label 'A-before-V01'
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    [OutputType([System.IO.FileInfo])]
    param (
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $OutputPath,

        [Parameter()]
        [string] $Label = '',

        [Parameter()]
        [string[]] $ExcludeDirectory = @('screenshots', 'log', 'logs', 'downloads'),

        [Parameter()]
        [switch] $KeepIdentifiers,

        [Parameter()]
        [switch] $Force
    )

    $root = (Get-Item -LiteralPath $Path -ErrorAction Stop).FullName.TrimEnd('\')
    $target = [System.IO.Path]::GetFullPath($OutputPath)
    if ($target.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ('Refusing to write inside the inventoried folder: {0}' -f $target)
    }
    if ((Test-Path -LiteralPath $target) -and -not $Force) {
        throw ('Output file exists, use -Force to overwrite: {0}' -f $target)
    }

    $files = @(Get-XivInventory -Path $root -ExcludeDirectory $ExcludeDirectory -KeepIdentifiers:$KeepIdentifiers)
    $shownRoot = $root
    if ($env:USERPROFILE -and $root.StartsWith($env:USERPROFILE, [System.StringComparison]::OrdinalIgnoreCase)) {
        $shownRoot = '%USERPROFILE%' + $root.Substring($env:USERPROFILE.Length)
    }
    $document = [pscustomobject] @{
        Schema     = $script:InventorySchema
        Label      = $Label
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        Root       = $shownRoot
        Excluded   = $ExcludeDirectory
        Files      = $files
    }

    if ($PSCmdlet.ShouldProcess($target, 'Write inventory')) {
        $parent = Split-Path -Path $target -Parent
        if (-not (Test-Path -LiteralPath $parent)) {
            $null = New-Item -ItemType Directory -Path $parent
        }
        $json = ConvertTo-Json -InputObject $document -Depth 5
        Set-Content -LiteralPath $target -Value $json -Encoding UTF8
        Get-Item -LiteralPath $target
    }
}

function Compare-XivInventory {
    <#
    .SYNOPSIS
        Compares two inventory files and reports what was added, removed or changed.

    .DESCRIPTION
        Entries are matched by relative path. A file counts as changed when its hash or its length
        differs; a file whose only difference is its time stamp is reported as Touched, because the
        game rewriting identical bytes is itself a finding.

    .PARAMETER ReferencePath
        The earlier inventory.

    .PARAMETER DifferencePath
        The later inventory.

    .PARAMETER IncludeUnchanged
        Also reports entries that are identical in both.

    .EXAMPLE
        Compare-XivInventory -ReferencePath .\out\before.inventory.json -DifferencePath .\out\after.inventory.json
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string] $ReferencePath,

        [Parameter(Mandatory = $true)]
        [string] $DifferencePath,

        [Parameter()]
        [switch] $IncludeUnchanged
    )

    $reference = Read-XivInventory -LiteralPath $ReferencePath
    $difference = Read-XivInventory -LiteralPath $DifferencePath

    $before = @{}
    foreach ($entry in $reference.Files) { $before[$entry.RelativePath] = $entry }
    $after = @{}
    foreach ($entry in $difference.Files) { $after[$entry.RelativePath] = $entry }

    $paths = @($before.Keys) + @($after.Keys) | Sort-Object -Unique
    foreach ($relative in $paths) {
        $old = $before[$relative]
        $new = $after[$relative]
        if ($null -eq $old) {
            $status = 'Added'
        } elseif ($null -eq $new) {
            $status = 'Removed'
        } elseif ($old.Sha256 -ne $new.Sha256 -or $old.Length -ne $new.Length) {
            $status = 'Changed'
        } elseif ($old.LastWriteTimeUtc -ne $new.LastWriteTimeUtc) {
            $status = 'Touched'
        } else {
            $status = 'Unchanged'
        }
        if ($status -eq 'Unchanged' -and -not $IncludeUnchanged) { continue }

        [pscustomobject] @{
            Status          = $status
            RelativePath    = $relative
            ReferenceLength = if ($old) { $old.Length } else { $null }
            DifferenceLength = if ($new) { $new.Length } else { $null }
            ReferenceSha256 = if ($old) { $old.Sha256 } else { $null }
            DifferenceSha256 = if ($new) { $new.Sha256 } else { $null }
        }
    }
}

function Read-XivInventory {
    <#
    .SYNOPSIS
        Reads an inventory file written by Export-XivInventory and checks its schema.

    .PARAMETER LiteralPath
        The inventory file.

    .EXAMPLE
        Read-XivInventory -LiteralPath .\out\before.inventory.json
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    $document = Get-Content -LiteralPath $LiteralPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($document.Schema -ne $script:InventorySchema) {
        throw ('Unsupported inventory schema {0} in {1}' -f $document.Schema, $LiteralPath)
    }
    $document
}

function Get-XivEnvironment {
    <#
    .SYNOPSIS
        Reports where the game and plugin configuration live and whether OneDrive already covers them.

    .DESCRIPTION
        Read-only. Answers the questions that decide whether a second sync would run on top of an
        existing one: is the Documents folder redirected into OneDrive, and does the game's
        configuration folder therefore already sync. Reads no file contents.

    .EXAMPLE
        Get-XivEnvironment
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param ()

    $documents = [Environment]::GetFolderPath('MyDocuments')
    $shellFolders = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
    $registered = $null
    $properties = Get-ItemProperty -LiteralPath $shellFolders -ErrorAction SilentlyContinue
    if ($properties -and ($properties.PSObject.Properties.Name -contains 'Personal')) {
        $registered = $properties.Personal
    }

    $oneDriveRoots = @(
        foreach ($name in 'OneDrive', 'OneDriveConsumer', 'OneDriveCommercial') {
            $value = [Environment]::GetEnvironmentVariable($name)
            if ($value) { $value.TrimEnd('\') }
        }
    ) | Sort-Object -Unique

    $ffxiv = Join-Path -Path $documents -ChildPath 'My Games\FINAL FANTASY XIV - A Realm Reborn'
    $launcher = Join-Path -Path $env:APPDATA -ChildPath 'XIVLauncher'
    $pluginConfigs = Join-Path -Path $launcher -ChildPath 'pluginConfigs'

    $underOneDrive = $false
    foreach ($oneDriveRoot in $oneDriveRoots) {
        if ($documents.StartsWith($oneDriveRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            $underOneDrive = $true
        }
    }

    $pluginConfigCount = $null
    if (Test-Path -LiteralPath $pluginConfigs) {
        $pluginConfigCount = @(Get-ChildItem -LiteralPath $pluginConfigs -Force -Filter '*.json').Count
    }

    [pscustomobject] @{
        DocumentsPath                  = $documents
        DocumentsRegistered            = $registered
        OneDriveRoots                  = $oneDriveRoots
        DocumentsRedirectedToOneDrive  = $underOneDrive
        FfxivConfigPath                = $ffxiv
        FfxivConfigExists              = Test-Path -LiteralPath $ffxiv
        XivLauncherPath                = $launcher
        XivLauncherExists              = Test-Path -LiteralPath $launcher
        PluginConfigJsonCount          = $pluginConfigCount
        GameRunning                    = @(Get-Process -Name 'ffxiv_dx11', 'ffxiv' -ErrorAction SilentlyContinue).Count -gt 0
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-XivPseudonym'
    'Test-XivCloudPlaceholder'
    'Get-XivFileHash'
    'Get-XivInventory'
    'Export-XivInventory'
    'Compare-XivInventory'
    'Read-XivInventory'
    'Get-XivEnvironment'
)
