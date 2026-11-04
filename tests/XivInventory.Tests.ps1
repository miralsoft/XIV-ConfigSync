#Requires -Version 5.1
# Tests for tools/XivInventory.psm1. Every test works on a synthetic folder under TestDrive;
# no test reads or writes real game data.

[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification = 'Variables set in BeforeEach are read in the It blocks, which the analyzer does not follow.')]
param ()

BeforeAll {
    Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath '..\tools\XivInventory.psm1') -Force

    function Initialize-SampleTree {
        param ([string] $Root)
        $character = Join-Path -Path $Root -ChildPath 'FFXIV_CHR0040000000ABCDEF'
        $null = New-Item -ItemType Directory -Path $character -Force
        $null = New-Item -ItemType Directory -Path (Join-Path -Path $Root -ChildPath 'screenshots') -Force
        Set-Content -LiteralPath (Join-Path -Path $Root -ChildPath 'FFXIV.cfg') -Value 'UiScale 100' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path -Path $character -ChildPath 'ADDON.DAT') -Value 'hud' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path -Path $Root -ChildPath 'screenshots\shot.png') -Value 'png' -Encoding ASCII
    }

    function Get-TreeState {
        param ([string] $Root)
        Get-ChildItem -LiteralPath $Root -Recurse -Force -File | Sort-Object -Property FullName | ForEach-Object {
            '{0}|{1}|{2}' -f $_.FullName, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.LastWriteTimeUtc.Ticks
        }
    }
}

Describe 'ConvertTo-XivPseudonym' {
    It 'replaces the content ID with a stable pseudonym' {
        $first = ConvertTo-XivPseudonym -RelativePath 'FFXIV_CHR0040000000ABCDEF\ADDON.DAT'
        $second = ConvertTo-XivPseudonym -RelativePath 'FFXIV_CHR0040000000abcdef\ADDON.DAT'
        $first | Should -Match '^FFXIV_CHR#[0-9A-F]{8}\\ADDON\.DAT$'
        $first | Should -Not -Match '0040000000ABCDEF'
        $second | Should -Be $first
    }

    It 'leaves other path segments alone' {
        ConvertTo-XivPseudonym -RelativePath 'FFXIV.cfg' | Should -Be 'FFXIV.cfg'
        ConvertTo-XivPseudonym -RelativePath 'FFXIV_CHR123\x' | Should -Be 'FFXIV_CHR123\x'
    }
}

Describe 'Test-XivCloudPlaceholder' {
    It 'recognises a file whose content is not local' {
        Test-XivCloudPlaceholder -Attributes ([long] [System.IO.FileAttributes]::Offline) | Should -BeTrue
        Test-XivCloudPlaceholder -Attributes 0x00400000 | Should -BeTrue
        Test-XivCloudPlaceholder -Attributes 0x00040000 | Should -BeTrue
    }

    It 'accepts an ordinary file' {
        Test-XivCloudPlaceholder -Attributes ([long] [System.IO.FileAttributes]::Archive) | Should -BeFalse
    }
}

Describe 'Get-XivInventory' {
    BeforeEach {
        $root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        Initialize-SampleTree -Root $root
    }

    It 'lists files with their SHA-256 and skips excluded folders' {
        $inventory = @(Get-XivInventory -Path $root)
        $inventory.Count | Should -Be 2
        $config = $inventory | Where-Object { $_.RelativePath -eq 'FFXIV.cfg' }
        $expected = (Get-FileHash -LiteralPath (Join-Path -Path $root -ChildPath 'FFXIV.cfg') -Algorithm SHA256).Hash.ToLowerInvariant()
        $config.Sha256 | Should -Be $expected
        $inventory.RelativePath | Should -Not -Contain 'screenshots\shot.png'
    }

    It 'pseudonymises character folders unless told otherwise' {
        (Get-XivInventory -Path $root).RelativePath -join ';' | Should -Not -Match 'ABCDEF'
        (Get-XivInventory -Path $root -KeepIdentifiers).RelativePath | Should -Contain 'FFXIV_CHR0040000000ABCDEF\ADDON.DAT'
    }

    It 'hashes a file another process holds open for writing' {
        $file = Join-Path -Path $root -ChildPath 'FFXIV.cfg'
        $holder = [System.IO.File]::Open($file, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::ReadWrite)
        try {
            $entry = Get-XivInventory -Path $root | Where-Object { $_.RelativePath -eq 'FFXIV.cfg' }
        } finally {
            $holder.Dispose()
        }
        $entry.Sha256 | Should -Not -BeNullOrEmpty
        $entry.ReadError | Should -BeNullOrEmpty
    }

    It 'records a file it cannot read instead of failing' {
        $file = Join-Path -Path $root -ChildPath 'FFXIV.cfg'
        $holder = [System.IO.File]::Open($file, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        try {
            $entry = Get-XivInventory -Path $root | Where-Object { $_.RelativePath -eq 'FFXIV.cfg' }
        } finally {
            $holder.Dispose()
        }
        $entry.Sha256 | Should -BeNullOrEmpty
        $entry.ReadError | Should -Not -BeNullOrEmpty
    }

    It 'records a junction without following it' {
        $outside = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $outside
        Set-Content -LiteralPath (Join-Path -Path $outside -ChildPath 'elsewhere.txt') -Value 'x' -Encoding ASCII
        $null = New-Item -ItemType Junction -Path (Join-Path -Path $root -ChildPath 'link') -Value $outside

        $inventory = @(Get-XivInventory -Path $root)
        ($inventory | Where-Object { $_.RelativePath -eq 'link' }).Kind | Should -Be 'Link'
        $inventory.RelativePath | Should -Not -Contain 'link\elsewhere.txt'
    }

    It 'changes nothing in the inventoried folder' {
        $before = Get-TreeState -Root $root
        $null = Get-XivInventory -Path $root
        Get-TreeState -Root $root | Should -Be $before
    }
}

Describe 'Export-XivInventory' {
    BeforeEach {
        $root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        Initialize-SampleTree -Root $root
        $out = Join-Path -Path $TestDrive -ChildPath ('{0}.inventory.json' -f [guid]::NewGuid().ToString('N'))
    }

    It 'writes an inventory that reads back' {
        $null = Export-XivInventory -Path $root -OutputPath $out -Label 'A'
        $document = Read-XivInventory -LiteralPath $out
        $document.Label | Should -Be 'A'
        @($document.Files).Count | Should -Be 2
    }

    It 'refuses to write inside the inventoried folder' {
        $inside = Join-Path -Path $root -ChildPath 'x.json'
        { Export-XivInventory -Path $root -OutputPath $inside } | Should -Throw '*inside the inventoried folder*'
        Test-Path -LiteralPath $inside | Should -BeFalse
    }

    It 'refuses to overwrite without -Force' {
        $null = Export-XivInventory -Path $root -OutputPath $out
        { Export-XivInventory -Path $root -OutputPath $out } | Should -Throw '*use -Force*'
        { Export-XivInventory -Path $root -OutputPath $out -Force } | Should -Not -Throw
    }

    It 'writes nothing under -WhatIf' {
        $null = Export-XivInventory -Path $root -OutputPath $out -WhatIf
        Test-Path -LiteralPath $out | Should -BeFalse
    }

    It 'changes nothing in the inventoried folder' {
        $before = Get-TreeState -Root $root
        $null = Export-XivInventory -Path $root -OutputPath $out
        Get-TreeState -Root $root | Should -Be $before
    }
}

Describe 'Compare-XivInventory' {
    It 'reports added, removed, changed and touched files' {
        $root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        Initialize-SampleTree -Root $root
        Set-Content -LiteralPath (Join-Path -Path $root -ChildPath 'gone.txt') -Value 'x' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path -Path $root -ChildPath 'same.txt') -Value 'same' -Encoding ASCII
        $before = Join-Path -Path $TestDrive -ChildPath 'before.inventory.json'
        $after = Join-Path -Path $TestDrive -ChildPath 'after.inventory.json'
        $null = Export-XivInventory -Path $root -OutputPath $before -Force

        Remove-Item -LiteralPath (Join-Path -Path $root -ChildPath 'gone.txt')
        Set-Content -LiteralPath (Join-Path -Path $root -ChildPath 'new.txt') -Value 'x' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path -Path $root -ChildPath 'FFXIV.cfg') -Value 'UiScale 150' -Encoding ASCII
        (Get-Item -LiteralPath (Join-Path -Path $root -ChildPath 'same.txt')).LastWriteTimeUtc = [DateTime]::UtcNow.AddHours(1)
        $null = Export-XivInventory -Path $root -OutputPath $after -Force

        $result = @(Compare-XivInventory -ReferencePath $before -DifferencePath $after)
        ($result | Where-Object { $_.RelativePath -eq 'gone.txt' }).Status | Should -Be 'Removed'
        ($result | Where-Object { $_.RelativePath -eq 'new.txt' }).Status | Should -Be 'Added'
        ($result | Where-Object { $_.RelativePath -eq 'FFXIV.cfg' }).Status | Should -Be 'Changed'
        ($result | Where-Object { $_.RelativePath -eq 'same.txt' }).Status | Should -Be 'Touched'
        $result.Count | Should -Be 4
        @(Compare-XivInventory -ReferencePath $before -DifferencePath $after -IncludeUnchanged).Count | Should -Be 5
    }
}

Describe 'Get-XivEnvironment' {
    It 'reports the documents folder and whether OneDrive covers it' {
        $report = Get-XivEnvironment
        $report.DocumentsPath | Should -Be ([Environment]::GetFolderPath('MyDocuments'))
        $report.DocumentsRedirectedToOneDrive | Should -BeOfType ([bool])
    }
}

Describe 'Read-XivConfigText' {
    It 'reads sections, keys and values, including empty values' {
        $file = Join-Path -Path $TestDrive -ChildPath 'read.cfg'
        $text = "<Config File>`r`n`r`n<Display Settings>`r`nUiBaseScale`t2`r`nDktSessionId`t`r`n<Sound>`r`nSoundMaster`t50`r`nnot a key line`r`n"
        [System.IO.File]::WriteAllText($file, $text, (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false))
        $entries = @(Read-XivConfigText -LiteralPath $file)
        $entries.Count | Should -Be 3
        $entries[0].Section | Should -Be 'Display Settings'
        $entries[0].Key | Should -Be 'UiBaseScale'
        $entries[0].Value | Should -Be '2'
        $entries[1].Value | Should -Be ''
        $entries[2].Section | Should -Be 'Sound'
    }
}

Describe 'Compare-XivConfigText' {
    It 'reports added, removed and changed keys, matched by section' {
        $old = Join-Path -Path $TestDrive -ChildPath 'old.cfg'
        $new = Join-Path -Path $TestDrive -ChildPath 'new.cfg'
        Set-Content -LiteralPath $old -Encoding ASCII -Value @('<A>', "Scale`t1", "Gone`tx", "Same`t5", '<B>', "Scale`t1")
        Set-Content -LiteralPath $new -Encoding ASCII -Value @('<A>', "Scale`t2", "Same`t5", "New`ty", '<B>', "Scale`t1")
        $result = @(Compare-XivConfigText -ReferencePath $old -DifferencePath $new)
        $result.Count | Should -Be 3
        $changed = @($result | Where-Object { $_.Status -eq 'Changed' })
        $changed.Count | Should -Be 1
        $changed[0].Section | Should -Be 'A'
        $changed[0].ReferenceValue | Should -Be '1'
        $changed[0].DifferenceValue | Should -Be '2'
        @($result | Where-Object { $_.Status -eq 'Removed' })[0].Key | Should -Be 'Gone'
        @($result | Where-Object { $_.Status -eq 'Added' })[0].Key | Should -Be 'New'
        @(Compare-XivConfigText -ReferencePath $old -DifferencePath $new -IncludeUnchanged).Count | Should -Be 5
    }
}

Describe 'Save-XivConfigSnapshot' {
    BeforeEach {
        $root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        Initialize-SampleTree -Root $root
        $snapshot = Join-Path -Path $TestDrive -ChildPath ('{0}.cfg' -f [guid]::NewGuid().ToString('N'))
    }

    It 'copies only the top-level settings files, with their time stamp' {
        $copied = @(Save-XivConfigSnapshot -Path $root -OutputDirectory $snapshot)
        $copied.Count | Should -Be 1
        $copied[0].Name | Should -Be 'FFXIV.cfg'
        $source = Get-Item -LiteralPath (Join-Path -Path $root -ChildPath 'FFXIV.cfg')
        $copied[0].LastWriteTimeUtc | Should -Be $source.LastWriteTimeUtc
        (Get-FileHash -LiteralPath $copied[0].FullName).Hash | Should -Be (Get-FileHash -LiteralPath $source.FullName).Hash
    }

    It 'refuses to write inside the source folder' {
        $inside = Join-Path -Path $root -ChildPath 'snap'
        { Save-XivConfigSnapshot -Path $root -OutputDirectory $inside } | Should -Throw '*inside the source folder*'
        Test-Path -LiteralPath $inside | Should -BeFalse
    }

    It 'refuses an existing snapshot folder without -Force' {
        $null = Save-XivConfigSnapshot -Path $root -OutputDirectory $snapshot
        { Save-XivConfigSnapshot -Path $root -OutputDirectory $snapshot } | Should -Throw '*use -Force*'
        { Save-XivConfigSnapshot -Path $root -OutputDirectory $snapshot -Force } | Should -Not -Throw
    }

    It 'writes nothing under -WhatIf and changes nothing in the source folder' {
        $before = Get-TreeState -Root $root
        $null = Save-XivConfigSnapshot -Path $root -OutputDirectory $snapshot -WhatIf
        Test-Path -LiteralPath $snapshot | Should -BeFalse
        $null = Save-XivConfigSnapshot -Path $root -OutputDirectory $snapshot
        Get-TreeState -Root $root | Should -Be $before
    }
}

Describe 'Get-XivCloudState' {
    It 'reports every file without opening it, and no shell state outside a cloud folder' {
        $root = Join-Path -Path $TestDrive -ChildPath ([guid]::NewGuid().ToString('N'))
        Initialize-SampleTree -Root $root
        $before = Get-TreeState -Root $root
        $state = @(Get-XivCloudState -Path $root)
        $state.Count | Should -Be 1
        $state[0].Name | Should -Be 'FFXIV.cfg'
        $state[0].Attributes | Should -Match '^0x[0-9A-F]+$'
        $state[0].Placeholder | Should -BeFalse
        $state[0].Pinned | Should -BeFalse
        Get-TreeState -Root $root | Should -Be $before
    }
}
