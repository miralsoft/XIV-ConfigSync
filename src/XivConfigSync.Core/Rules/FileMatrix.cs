using XivConfigSync.Core.Model;

namespace XivConfigSync.Core.Rules;

/// <summary>What may happen to a file when a revision is applied.</summary>
public enum ApplyRule
{
    /// <summary>Applied without asking when the source changed it and the local copy did not.</summary>
    Always,

    /// <summary>Offered; applied only when the user picks it.</summary>
    OnRequest,

    /// <summary>Not offered for apply from another device; can be restored on the device that made it.</summary>
    Never,
}

/// <summary>What a file is, from the file matrix (<c>docs/file-matrix.md</c>).</summary>
public enum FileClass
{
    Shared,
    Device,
    Mixed,
    Sensitive,
    Unknown,
}

/// <summary>How well a matrix row is known.</summary>
public enum Evidence
{
    Confirmed,
    Unconfirmed,
    Unknown,
}

/// <summary>One row of the built-in file matrix.</summary>
public sealed record MatrixEntry(string Root, string Pattern, FileClass Class, ApplyRule DefaultRule, Evidence Evidence, string NoteKey)
{
    /// <summary>The compiled pattern.</summary>
    public Glob Glob { get; } = new(Pattern);
}

/// <summary>
/// The built-in file matrix: what each known file is and its default rule. It mirrors
/// <c>docs/file-matrix.md</c> and the decisions of 2026-11-04.
/// </summary>
public static class FileMatrix
{
    private const string Chr = "FFXIV_CHR*/";

    /// <summary>All rows, most specific first within a root.</summary>
    public static IReadOnlyList<MatrixEntry> Entries { get; } =
    [
        // Device files: never applied from another device.
        new(Roots.Game, "FFXIV.cfg", FileClass.Mixed, ApplyRule.Never, Evidence.Confirmed, "Matrix_FfxivCfg"),
        new(Roots.Game, "FFXIV_BOOT.cfg", FileClass.Device, ApplyRule.Never, Evidence.Confirmed, "Matrix_BootCfg"),
        new(Roots.Game, "cfgcopy/**", FileClass.Sensitive, ApplyRule.Never, Evidence.Confirmed, "Matrix_Cfgcopy"),

        // Shared, confirmed by the transfer test, and not changed by the game on its own (V00).
        new(Roots.Game, "MACROSYS.dat", FileClass.Shared, ApplyRule.Always, Evidence.Confirmed, "Matrix_MacroSys"),
        new(Roots.Game, Chr + "HOTBAR.DAT", FileClass.Shared, ApplyRule.Always, Evidence.Confirmed, "Matrix_Hotbar"),
        new(Roots.Game, Chr + "KEYBIND.DAT", FileClass.Shared, ApplyRule.Always, Evidence.Confirmed, "Matrix_Keybind"),
        new(Roots.Game, Chr + "CONTROL0.DAT", FileClass.Shared, ApplyRule.Always, Evidence.Confirmed, "Matrix_Control0"),

        // Shared candidates: on request until one transfer each is seen in the game (specification 6.3).
        new(Roots.Game, Chr + "MACRO.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Macro"),
        new(Roots.Game, Chr + "GEARSET.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Gearset"),
        new(Roots.Game, Chr + "COMMON.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Common"),
        new(Roots.Game, Chr + "CONTROL1.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Control1"),
        new(Roots.Game, Chr + "LOGFLTR.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_LogFilter"),
        new(Roots.Game, Chr + "ITEMFDR.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_ItemFinder"),
        new(Roots.Game, Chr + "ACQ.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Acq"),
        new(Roots.Game, Chr + "GS.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Gs"),

        // Changed by the game on its own (V00): only on request.
        new(Roots.Game, Chr + "ADDON.DAT", FileClass.Shared, ApplyRule.OnRequest, Evidence.Confirmed, "Matrix_Addon"),
        new(Roots.Game, Chr + "UISAVE.DAT", FileClass.Unknown, ApplyRule.OnRequest, Evidence.Confirmed, "Matrix_UiSave"),
        new(Roots.Game, Chr + "ITEMODR.DAT", FileClass.Unknown, ApplyRule.OnRequest, Evidence.Confirmed, "Matrix_ItemOrder"),

        // Character creation presets: shareable, not a target of 0.1.
        new(Roots.Game, "FFXIV_CHARA_*.dat", FileClass.Shared, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_CharaPreset"),

        // Plugins: always on request (specification 6.2).
        new(Roots.Plugins, "**", FileClass.Unknown, ApplyRule.OnRequest, Evidence.Unconfirmed, "Matrix_Plugin"),
    ];

    /// <summary>
    /// Files never backed up: the game's own previous generations, conflict copies a cloud client
    /// left behind, logs, caches and lock files (specification 3).
    /// </summary>
    public static IReadOnlyList<(string Root, Glob Glob, string ReasonKey)> Exclusions { get; } =
    [
        (Roots.Game, new Glob("**/*.old"), "Exclude_Old"),
        (Roots.Game, new Glob("**/.*.tmp"), "Exclude_Temp"),
        (Roots.Plugins, new Glob("**/.*.tmp"), "Exclude_Temp"),
        (Roots.Game, new Glob("screenshots/**"), "Exclude_NotConfig"),
        (Roots.Game, new Glob("log/**"), "Exclude_NotConfig"),
        (Roots.Game, new Glob("logs/**"), "Exclude_NotConfig"),
        (Roots.Game, new Glob("downloads/**"), "Exclude_NotConfig"),
        // Conflict copies a cloud client left behind: "<name>-<computer>[-n].<ext>". The game's own
        // files have no '-' in their names.
        (Roots.Game, new Glob("**/*-*.cfg"), "Exclude_ConflictCopy"),
        (Roots.Game, new Glob("**/*-*.dat"), "Exclude_ConflictCopy"),
        (Roots.Plugins, new Glob("**/*.log"), "Exclude_Log"),
        (Roots.Plugins, new Glob("**/*.lock"), "Exclude_Lock"),
        (Roots.Plugins, new Glob("**/*.tmp"), "Exclude_Temp"),
        (Roots.Plugins, new Glob("**/*.bak"), "Exclude_Backup"),
        (Roots.Plugins, new Glob("**/cache/**"), "Exclude_Cache"),
        (Roots.Plugins, new Glob("**/Cache/**"), "Exclude_Cache"),
        (Roots.Plugins, new Glob("**/*.navmesh"), "Exclude_Cache"),
        (Roots.Plugins, new Glob("**/*.tex"), "Exclude_Cache"),
        (Roots.Plugins, new Glob("**/*.db-journal"), "Exclude_Lock"),
        (Roots.Plugins, new Glob("**/*.db-wal"), "Exclude_Lock"),
        (Roots.Plugins, new Glob("**/*.db-shm"), "Exclude_Lock"),
    ];

    /// <summary>A single file above this size is left out and reported (cache rule by size).</summary>
    public const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>A file above this size is backed up and flagged.</summary>
    public const long LargeFileSize = 8L * 1024 * 1024;

    /// <summary>The matrix row for a file, or a generic row for an unknown file.</summary>
    public static MatrixEntry Lookup(string root, string relativePath)
    {
        MatrixEntry? best = null;
        foreach (var entry in Entries)
        {
            if (entry.Root == root && entry.Glob.IsMatch(relativePath) && (best is null || entry.Glob.Specificity > best.Glob.Specificity))
            {
                best = entry;
            }
        }

        return best ?? new MatrixEntry(root, relativePath, FileClass.Unknown, ApplyRule.OnRequest, Evidence.Unknown, "Matrix_Unknown");
    }
}
