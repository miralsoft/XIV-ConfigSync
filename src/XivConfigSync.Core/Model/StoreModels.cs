namespace XivConfigSync.Core.Model;

/// <summary>The configuration roots the application backs up.</summary>
public static class Roots
{
    /// <summary>The game's configuration folder below Documents.</summary>
    public const string Game = "game";

    /// <summary>Dalamud's plugin configuration folder.</summary>
    public const string Plugins = "plugins";

    /// <summary>All roots in a fixed order.</summary>
    public static IReadOnlyList<string> All { get; } = [Game, Plugins];
}

/// <summary>Flags a scan sets on a file.</summary>
public static class FileFlags
{
    /// <summary>A credential pattern was found in the file.</summary>
    public const string Credential = "credential";

    /// <summary>The file names an absolute path on this device.</summary>
    public const string AbsolutePath = "absolute-path";

    /// <summary>The file is above the warning size.</summary>
    public const string Large = "large";
}

/// <summary>Content of the sync file (<c>*.xivsync</c>) that marks a sync folder.</summary>
public sealed class SyncFileContent
{
    /// <summary>The value of <see cref="Kind"/> in every sync file.</summary>
    public const string ExpectedKind = "xiv-configsync-store";

    public int Schema { get; set; } = 1;

    public string Kind { get; set; } = ExpectedKind;

    public DateTimeOffset Created { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>Content of <c>device.json</c> in a device's part of the sync folder.</summary>
public sealed class DeviceInfo
{
    public int Schema { get; set; } = 1;

    public string DeviceId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    /// <summary>
    /// Changes when the device is set up again (for example after Windows was reinstalled). Other
    /// devices treat revisions of a new epoch as having no common state with them, so a fresh
    /// installation's default files are never applied over their settings without asking.
    /// </summary>
    public string Epoch { get; set; } = string.Empty;
}

/// <summary>One file in a revision.</summary>
public sealed class FileEntry
{
    public string Path { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public long Size { get; set; }

    public List<string> Flags { get; set; } = [];
}

/// <summary>The files of one root in a revision.</summary>
public sealed class RootEntry
{
    public List<FileEntry> Files { get; set; } = [];
}

/// <summary>A file left out of a revision, and why.</summary>
public sealed class ExcludedEntry
{
    public string Root { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>Screen information recorded with a revision (FR-014).</summary>
public sealed class DisplayInfo
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int ScalePercent { get; set; }
}

/// <summary>Why a revision was made.</summary>
public enum RevisionOrigin
{
    Manual,
    GameExit,
    AppStart,
    AfterApply,
}

/// <summary>A revision manifest (<c>revisions\&lt;id&gt;.json</c>). Never changed after it is written.</summary>
public sealed class RevisionManifest
{
    public int Schema { get; set; } = 1;

    public string RevisionId { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public string? Parent { get; set; }

    public RevisionOrigin Origin { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    /// <summary>The device's epoch when the revision was made; see <see cref="DeviceInfo.Epoch"/>.</summary>
    public string Epoch { get; set; } = string.Empty;

    public string? GameVersion { get; set; }

    public DisplayInfo? Display { get; set; }

    public Dictionary<string, RootEntry> Roots { get; set; } = [];

    public List<ExcludedEntry> Excluded { get; set; } = [];

    /// <summary>Every file of the revision with its root.</summary>
    public IEnumerable<(string Root, FileEntry File)> AllFiles() =>
        Roots.SelectMany(root => root.Value.Files.Select(file => (root.Key, file)));
}

/// <summary>Content of <c>received\&lt;device&gt;.json</c>: which revision of that device was verified here.</summary>
public sealed class ReceivedMarker
{
    public int Schema { get; set; } = 1;

    public string RevisionId { get; set; } = string.Empty;

    public DateTimeOffset Verified { get; set; }
}
