using XivConfigSync.Core.IO;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.Inventory;

/// <summary>A file found by a scan.</summary>
public sealed record ScannedFile(string Root, string Path, string FullPath, long Size, string Hash, IReadOnlyList<string> Flags);

/// <summary>The result of scanning the configured roots.</summary>
public sealed class ScanResult
{
    public List<ScannedFile> Files { get; } = [];

    public List<ExcludedEntry> Excluded { get; } = [];

    /// <summary>Conflict copies found; reported in diagnostics, never backed up or removed.</summary>
    public int ConflictCopies { get; set; }

    /// <summary>Looks a file up by root and path.</summary>
    public ScannedFile? Find(string root, string path) =>
        Files.FirstOrDefault(f => f.Root == root && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Scans configuration folders read-only (X-05): path, size, SHA-256, flags.</summary>
public static class Scanner
{
    /// <summary>
    /// Every file below a folder. Symbolic links and junctions are not followed and not listed;
    /// other reparse points, such as cloud placeholders under OneDrive, are ordinary files here
    /// (review finding 13). Folders that cannot be read are skipped.
    /// </summary>
    public static IEnumerable<string> Walk(string folder) => WalkWithSkipped(folder).Where(e => e.Reason is null).Select(e => e.FullPath);

    /// <summary>
    /// As <see cref="Walk"/>, but also returns what was skipped and why ("Exclude_Link",
    /// "Exclude_Unreadable"), so a backup can show it instead of losing it silently (review 2, finding 9).
    /// </summary>
    /// <param name="folder">The folder to walk.</param>
    /// <param name="recursive">False to list only the files directly in the folder.</param>
    public static IEnumerable<(string FullPath, string? Reason)> WalkWithSkipped(string folder, bool recursive = true)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(folder));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            List<FileSystemInfo> entries;
            var unreadable = false;
            try
            {
                entries = [.. directory.EnumerateFileSystemInfos()];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entries = [];
                unreadable = true;
            }

            if (unreadable)
            {
                // Reported as a whole folder; a yield is not allowed inside the catch.
                yield return (directory.FullName, "Exclude_Unreadable");
                continue;
            }

            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                string? reason = null;
                try
                {
                    if (entry.LinkTarget is not null)
                    {
                        reason = "Exclude_Link";
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    reason = "Exclude_Unreadable";
                }

                if (reason is not null)
                {
                    yield return (entry.FullName, reason);
                }
                else if (entry is DirectoryInfo sub)
                {
                    if (recursive)
                    {
                        pending.Push(sub);
                    }
                }
                else if (!entry.Attributes.HasFlag(FileAttributes.Device))
                {
                    yield return (entry.FullName, null);
                }
            }
        }
    }
    /// <summary>Scans every root that has a folder. A root whose folder is null or missing is skipped.</summary>
    /// <param name="roots">Root name to folder.</param>
    /// <param name="rules">Exclusions to apply.</param>
    /// <param name="contentSink">Receives each file's content, so a backup can store it without reading it twice.</param>
    public static ScanResult Scan(IReadOnlyDictionary<string, string?> roots, RuleSet rules, Action<ScannedFile, byte[]>? contentSink = null)
    {
        var result = new ScanResult();
        foreach (var (root, folder) in roots)
        {
            if (folder is null || !Directory.Exists(folder))
            {
                continue;
            }

            foreach (var (full, skipped) in WalkWithSkipped(folder, !Roots.IsTopLevelOnly(root)))
            {
                if (!RelativePath.TryNormalize(Path.GetRelativePath(folder, full), out var relative))
                {
                    continue;
                }

                if (skipped is not null)
                {
                    result.Excluded.Add(new ExcludedEntry { Root = root, Path = relative, Reason = skipped });
                    continue;
                }

                var reason = rules.ExclusionReason(root, relative);
                if (reason is not null)
                {
                    if (reason == "Exclude_ConflictCopy")
                    {
                        result.ConflictCopies++;
                    }

                    if (reason != "Exclude_Old")
                    {
                        result.Excluded.Add(new ExcludedEntry { Root = root, Path = relative, Reason = reason });
                    }

                    continue;
                }

                var info = new FileInfo(full);
                if (info.Length > FileMatrix.MaxFileSize)
                {
                    result.Excluded.Add(new ExcludedEntry { Root = root, Path = relative, Reason = "Exclude_TooLarge" });
                    continue;
                }

                byte[] content;
                string hash;
                try
                {
                    (content, hash) = FileOps.ReadStable(full);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"Could not read {root}/{relative}: {ex.Message}", ex);
                }

                var scanned = new ScannedFile(root, relative, full, content.Length, hash, ContentScanner.Scan(root, relative, content));
                result.Files.Add(scanned);
                contentSink?.Invoke(scanned, content);
            }
        }

        return result;
    }
}
