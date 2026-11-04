using System.Globalization;
using XivConfigSync.Core.IO;
using XivConfigSync.Core.Store;

namespace XivConfigSync.Core.Apply;

/// <summary>One file saved in a snapshot.</summary>
public sealed class SnapshotFile
{
    public string Root { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? Hash { get; set; }

    public bool Existed { get; set; }
}

/// <summary>Content of <c>snapshot.json</c>.</summary>
public sealed class SnapshotManifest
{
    public int Schema { get; set; } = 1;

    public string SnapshotId { get; set; } = string.Empty;

    public string Reason { get; set; } = "apply";

    public DateTimeOffset Created { get; set; }

    public string? SourceDeviceId { get; set; }

    public string? SourceRevisionId { get; set; }

    public List<SnapshotFile> Files { get; set; } = [];

    public bool Complete { get; set; }
}

/// <summary>One step of an apply in the journal.</summary>
public sealed class JournalStep
{
    public string Root { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public bool Done { get; set; }
}

/// <summary>Content of <c>journal.json</c>, present only while an apply runs.</summary>
public sealed class Journal
{
    public int Schema { get; set; } = 1;

    public string ApplyId { get; set; } = string.Empty;

    public string SnapshotId { get; set; } = string.Empty;

    public string SourceDeviceId { get; set; } = string.Empty;

    public string SourceRevisionId { get; set; } = string.Empty;

    public List<JournalStep> Steps { get; set; } = [];
}

/// <summary>A file to write in an apply.</summary>
public sealed record ApplyTarget(string Root, string Path, string Hash);

/// <summary>Test seam: called before each stage, so a test can interrupt an apply anywhere.</summary>
public interface IApplyProbe
{
    /// <summary>Called before a stage; <paramref name="index"/> is the file index for per-file stages.</summary>
    void Before(string stage, int index);
}

/// <summary>
/// Applies files from the store to the configuration folders: a verified snapshot first (X-02),
/// a journal, one atomic replace per file, a final check. An interrupted apply is finished or rolled
/// back at the next start; any apply can be undone from its snapshot.
/// </summary>
public sealed class Applier
{
    private readonly SyncStore store;
    private readonly string journalPath;
    private readonly string snapshotsFolder;
    private readonly IReadOnlyDictionary<string, string?> roots;
    private readonly IApplyProbe? probe;

    public Applier(SyncStore store, string journalPath, string snapshotsFolder, IReadOnlyDictionary<string, string?> roots, IApplyProbe? probe = null)
    {
        this.store = store;
        this.journalPath = journalPath;
        this.snapshotsFolder = snapshotsFolder;
        this.roots = roots;
        this.probe = probe;
    }

    /// <summary>Whether an interrupted apply is waiting to be finished or rolled back.</summary>
    public bool HasOpenJournal => File.Exists(journalPath);

    /// <summary>Reads the open journal, if any.</summary>
    public Journal? ReadJournal() => FileOps.ReadJson<Journal>(journalPath);

    /// <summary>Applies files from a source device's objects. Returns the snapshot ID.</summary>
    public string Apply(string sourceDeviceId, string sourceRevisionId, IReadOnlyList<ApplyTarget> targets)
    {
        if (HasOpenJournal)
        {
            throw new InvalidOperationException("An interrupted apply must be finished or rolled back first.");
        }

        // Read and verify every source object before anything local is touched.
        probe?.Before("verify", -1);
        var contents = new Dictionary<ApplyTarget, byte[]>();
        foreach (var target in targets)
        {
            contents[target] = store.ReadObject(sourceDeviceId, target.Hash);
        }

        probe?.Before("snapshot", -1);
        var snapshot = TakeSnapshot(targets.Select(t => (t.Root, t.Path)), "apply", sourceDeviceId, sourceRevisionId);

        var journal = new Journal
        {
            ApplyId = Guid.NewGuid().ToString("N"),
            SnapshotId = snapshot.SnapshotId,
            SourceDeviceId = sourceDeviceId,
            SourceRevisionId = sourceRevisionId,
            Steps = targets.Select(t => new JournalStep { Root = t.Root, Path = t.Path, Hash = t.Hash }).ToList(),
        };
        probe?.Before("journal", -1);
        FileOps.WriteJsonAtomic(journalPath, journal);

        for (var i = 0; i < targets.Count; i++)
        {
            probe?.Before("write", i);
            var target = targets[i];
            FileOps.WriteAtomic(TargetPath(target.Root, target.Path), contents[target], target.Hash);
            journal.Steps[i].Done = true;
            FileOps.WriteJsonAtomic(journalPath, journal);
        }

        probe?.Before("check", -1);
        foreach (var target in targets)
        {
            if (!string.Equals(FileOps.HashFile(TargetPath(target.Root, target.Path)), target.Hash, StringComparison.Ordinal))
            {
                throw new IOException("An applied file does not match its expected hash.");
            }
        }

        File.Delete(journalPath);
        return snapshot.SnapshotId;
    }

    /// <summary>Finishes an interrupted apply from its journal: writes the steps not done yet.</summary>
    public void FinishOpenJournal()
    {
        var journal = ReadJournal() ?? throw new InvalidOperationException("There is no interrupted apply.");
        foreach (var step in journal.Steps)
        {
            var path = TargetPath(step.Root, step.Path);
            if (File.Exists(path) && string.Equals(FileOps.HashFile(path), step.Hash, StringComparison.Ordinal))
            {
                continue;
            }

            var content = store.ReadObject(journal.SourceDeviceId, step.Hash);
            FileOps.WriteAtomic(path, content, step.Hash);
        }

        File.Delete(journalPath);
    }

    /// <summary>Rolls an interrupted apply back to its snapshot.</summary>
    public void RollBackOpenJournal()
    {
        var journal = ReadJournal() ?? throw new InvalidOperationException("There is no interrupted apply.");
        Restore(journal.SnapshotId);
        File.Delete(journalPath);
    }

    /// <summary>Restores every file of a snapshot exactly; files the apply created are removed.</summary>
    public void Restore(string snapshotId)
    {
        var folder = SnapshotFolder(snapshotId);
        var manifest = FileOps.ReadJson<SnapshotManifest>(Path.Combine(folder, "snapshot.json"))
            ?? throw new FileNotFoundException("Snapshot not found.", snapshotId);
        if (!manifest.Complete)
        {
            // An incomplete snapshot means the apply never started writing; nothing to restore.
            return;
        }

        foreach (var file in manifest.Files)
        {
            var target = TargetPath(file.Root, file.Path);
            if (file.Existed)
            {
                var saved = RelativePath.Combine(Path.Combine(folder, "files", file.Root), file.Path);
                FileOps.CopyAtomic(saved, target, file.Hash!);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    /// <summary>The snapshots, newest first.</summary>
    public IReadOnlyList<SnapshotManifest> ListSnapshots()
    {
        if (!Directory.Exists(snapshotsFolder))
        {
            return [];
        }

        var list = new List<SnapshotManifest>();
        foreach (var folder in Directory.GetDirectories(snapshotsFolder))
        {
            try
            {
                var manifest = FileOps.ReadJson<SnapshotManifest>(Path.Combine(folder, "snapshot.json"));
                if (manifest is { Complete: true })
                {
                    list.Add(manifest);
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // A damaged snapshot is not offered.
            }
        }

        return list.OrderByDescending(s => s.Created).ToList();
    }

    /// <summary>Removes snapshots beyond the newest <paramref name="keep"/> that are older than <paramref name="minAge"/>.</summary>
    public int PruneSnapshots(int keep, TimeSpan minAge, DateTimeOffset now)
    {
        var removed = 0;
        foreach (var snapshot in ListSnapshots().Skip(keep).Where(s => now - s.Created > minAge))
        {
            Directory.Delete(SnapshotFolder(snapshot.SnapshotId), recursive: true);
            removed++;
        }

        return removed;
    }

    /// <summary>Copies the current state of the given files into a new, verified snapshot.</summary>
    public SnapshotManifest TakeSnapshot(IEnumerable<(string Root, string Path)> files, string reason, string? sourceDeviceId, string? sourceRevisionId)
    {
        var now = DateTimeOffset.UtcNow;
        var id = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        var folder = SnapshotFolder(id);
        Directory.CreateDirectory(folder);
        var manifest = new SnapshotManifest
        {
            SnapshotId = id,
            Reason = reason,
            Created = now,
            SourceDeviceId = sourceDeviceId,
            SourceRevisionId = sourceRevisionId,
        };
        FileOps.WriteJsonAtomic(Path.Combine(folder, "snapshot.json"), manifest);

        foreach (var (root, path) in files)
        {
            var current = TargetPath(root, path);
            if (File.Exists(current))
            {
                var (content, hash) = FileOps.ReadStable(current);
                FileOps.WriteAtomic(RelativePath.Combine(Path.Combine(folder, "files", root), path), content, hash);
                manifest.Files.Add(new SnapshotFile { Root = root, Path = path, Hash = hash, Existed = true });
            }
            else
            {
                manifest.Files.Add(new SnapshotFile { Root = root, Path = path, Existed = false });
            }
        }

        manifest.Complete = true;
        FileOps.WriteJsonAtomic(Path.Combine(folder, "snapshot.json"), manifest);
        return manifest;
    }

    private string SnapshotFolder(string snapshotId)
    {
        if (snapshotId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || snapshotId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid snapshot ID.", nameof(snapshotId));
        }

        return Path.Combine(snapshotsFolder, snapshotId);
    }

    private string TargetPath(string root, string path)
    {
        if (!roots.TryGetValue(root, out var folder) || folder is null)
        {
            throw new InvalidOperationException($"The folder for '{root}' is not known on this device.");
        }

        return RelativePath.Combine(folder, path);
    }
}
