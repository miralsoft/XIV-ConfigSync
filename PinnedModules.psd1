# The PowerShell modules a project pins, read by Restore-PinnedModules.ps1.
#
# Copied from: miralsoft-foundation-docs, enforcement/powershell/PinnedModules.psd1
# Foundation version: 5.1.0
# Copied on: 2026-11-03
#
# Exact versions only. Sha512 is the PackageHash the PowerShell Gallery publishes for that
# version, base64 as the gallery gives it, read from
# https://www.powershellgallery.com/api/v2/Packages(Id='<Name>',Version='<Version>').
# Changing a version is a recorded decision (C-16), with the new hash read the same way.
@{
    Modules = @(
        @{ Name = 'Pester'; Version = '6.2.0'; Sha512 = 'u9nl8XG2uMv86c1fQYOsnKcfs8acGWTWuhxyjIm0DqLdq5vR/q9dzPqrNf0rZ36gzXcF3yCs0kH53FjQ+fsT6A==' }
        @{ Name = 'PSScriptAnalyzer'; Version = '1.25.0'; Sha512 = '5/tXMmLmBLqymRSuYpIdJLl8L4i1GbQSd7QD72zhY/FLfRs23HNEmmjlrlqNlQKJGxDCZBMf0YE63ym/ExwWYw==' }
    )
}
