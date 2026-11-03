# The MIRAL Soft normal form for PowerShell, as PSScriptAnalyzer settings.
#
# Copied from: miralsoft-foundation-docs, enforcement/powershell/PSScriptAnalyzerSettings.psd1
# Foundation version: 5.1.0
# Copied on: 2026-11-03
#
# Keep the three lines above and fill in the date (M-19). Copy to the project root under this
# name. Verified on 2026-10-27 with PSScriptAnalyzer 1.25.0 on Windows PowerShell 5.1.26100: a
# script written to the normal form had no finding, a badly formatted one had five. Why each
# entry exists is in rules/languages/powershell.md.
@{
    # Everything is reported. CI fails on Error and Warning and on any analyzer error; a rule
    # that crashed has not passed (R-23).
    Severity     = @('Error', 'Warning', 'Information')

    Rules        = @{
        # Matters only where the analysis runs on PowerShell 7: run on 5.1 itself, syntax that
        # 5.1 does not know is a parse error there, or worse, is read as something else.
        PSUseCompatibleSyntax              = @{
            Enable         = $true
            TargetVersions = @('5.1')
        }

        # One True Brace Style: the opening brace on the line of its statement, `} else {`,
        # `} catch {` and `} finally {` on one line. Four spaces, never tabs.
        PSPlaceOpenBrace                   = @{
            Enable             = $true
            OnSameLine         = $true
            NewLineAfter       = $true
            IgnoreOneLineBlock = $true
        }
        PSPlaceCloseBrace                  = @{
            Enable             = $true
            NewLineAfter       = $false
            IgnoreOneLineBlock = $true
            NoEmptyLineBefore  = $false
        }
        PSUseConsistentIndentation         = @{
            Enable              = $true
            Kind                = 'space'
            IndentationSize     = 4
            PipelineIndentation = 'IncreaseIndentationForFirstPipeline'
        }
        PSUseConsistentWhitespace          = @{
            Enable          = $true
            CheckOpenBrace  = $true
            CheckOpenParen  = $true
            CheckOperator   = $true
            CheckSeparator  = $true
            CheckInnerBrace = $true
            CheckPipe       = $true
            # Aligned `=` in a hashtable, as Invoke-Formatter writes it, is not a whitespace error.
            IgnoreAssignmentOperatorInsideHashTable = $true
        }
        PSUseCorrectCasing                 = @{ Enable = $true }
        PSAvoidSemicolonsAsLineTerminators = @{ Enable = $true }
    }
}
