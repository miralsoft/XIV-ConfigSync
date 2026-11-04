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
    public static IEnumerable<string> Walk(string folder)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(folder));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            List<FileSystemInfo> entries;
            try
            {
                entries = [.. directory.EnumerateFileSystemInfos()];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (entry.LinkTarget is not null)
                {
                    continue;
                }

                if (entry is DirectoryInfo sub)
                {
                    pending.Push(sub);
                }
                else if (!entry.Attributes.HasFlag(FileAttributes.Device))
                {
                    yield return entry.FullName;
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

            foreach (var full in Walk(folder))
            {
                if (!RelativePath.TryNormalize(Path.GetRelativePath(folder, full), out var relative))
                {
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
