using XivConfigSync.Core.IO;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.Inventory;

/// <summary>
/// A conflict copy a cloud client left in the game folder, such as <c>FFXIV-PC2.cfg</c> next to
/// <c>FFXIV.cfg</c>. <paramref name="Original"/> is the file it is a copy of.
/// </summary>
public sealed record ConflictCopy(string Path, string FullPath, long Size, DateTimeOffset Modified, string Original, bool OriginalExists)
{
    /// <summary>Only a copy whose original is still there may be removed: otherwise it may be the only version left.</summary>
    public bool Removable => OriginalExists;
}

/// <summary>Finds conflict copies in the game folder, read-only, by the same rule that keeps them out of a backup.</summary>
public static class ConflictCopies
{
    public const string ReasonKey = "Exclude_ConflictCopy";

    /// <summary>Every conflict copy below the game folder, ordered by path. A missing folder has none.</summary>
    public static IReadOnlyList<ConflictCopy> Find(string? gameFolder, RuleSet rules)
    {
        if (gameFolder is null || !Directory.Exists(gameFolder))
        {
            return [];
        }

        var found = new List<ConflictCopy>();
        foreach (var full in Scanner.Walk(gameFolder))
        {
            if (!RelativePath.TryNormalize(System.IO.Path.GetRelativePath(gameFolder, full), out var relative)
                || rules.ExclusionReason(Roots.Game, relative) != ReasonKey)
            {
                continue;
            }

            var info = new FileInfo(full);
            var original = OriginalOf(relative);
            var originalFull = System.IO.Path.Combine(gameFolder, original.Replace('/', System.IO.Path.DirectorySeparatorChar));
            found.Add(new ConflictCopy(relative, full, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), original, File.Exists(originalFull)));
        }

        return [.. found.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The file a conflict copy belongs to: the name up to its first '-', with the copy's
    /// extension, in the same folder ("FFXIV-PC2-1.cfg" belongs to "FFXIV.cfg").
    /// </summary>
    public static string OriginalOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        var folder = slash < 0 ? string.Empty : relativePath[..(slash + 1)];
        var name = relativePath[(slash + 1)..];
        var dash = name.IndexOf('-', StringComparison.Ordinal);
        var dot = name.LastIndexOf('.');
        return dash <= 0 || dot < dash ? relativePath : folder + name[..dash] + name[dot..];
    }
}
