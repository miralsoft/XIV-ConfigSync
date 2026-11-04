using System.Globalization;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.IO;
using XivConfigSync.Core.Store;

namespace XivConfigSync.Core.Apply;

/// <summary>One file saved in a snapshot.</summary>
public sealed class SnapshotFile
{
    public string Root { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>The hash of the saved file, or null when the file did not exist.</summary>
    public string? Hash { get; set; }

    public bool Existed { get; set; }

    /// <summary>What the apply wrote; lets an undo tell whether the file changed since.</summary>
    public string? AppliedHash { get; set; }
}

/// <summary>Content of <c>snapshot.json</c>.</summary>
public sealed class SnapshotManifest
{
    public int Schema { get; set; } = 1;

    public string SnapshotId { get; set; } = string.Empty;

    /// <summary><c>apply</c>, or <c>safety</c> for the state saved before an undo or a recovery.</summary>
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

/// <summary>A file to write in an apply, with the hash the file had when the plan was made (null: absent).</summary>
public sealed record ApplyTarget(string Root, string Path, string Hash, string? ExpectedLocalHash);

/// <summary>Test seam: called before each stage, so a test can interrupt an apply anywhere.</summary>
public interface IApplyProbe
{
    /// <summary>Called before a stage; <paramref name="index"/> is the file index for per-file stages.</summary>
    void Before(string stage, int index);
}

/// <summary>
/// Applies files from the store to the configuration folders: a check that nothing changed since
/// the plan, a verified snapshot (X-02), a journal, one atomic replace per file with the game checked
/// before each, a final check. A failure in between rolls back at once; an interruption (power cut)
/// is finished or rolled back at the next start. Undo and recovery save the current state first, so
/// they can be undone too.
/// </summary>
public sealed class Applier
{
    private readonly SyncStore? store;
    private readonly string journalPath;
    private readonly string snapshotsFolder;
    private readonly IReadOnlyDictionary<string, string?> roots;
    private readonly Func<bool> gameRunning;
    private readonly IApplyProbe? probe;

    /// <summary>Creates an applier for this device.</summary>
    /// <param name="store">The sync folder; only needed to apply or to finish an interrupted apply.</param>
    /// <param name="journalPath">Where the journal of a running apply is kept.</param>
    /// <param name="snapshotsFolder">Where snapshots are kept.</param>
    /// <param name="roots">Root name to folder on this device.</param>
    /// <param name="gameRunning">Whether the game runs; checked before every write (X-01).</param>
    /// <param name="probe">Test seam, null in normal use.</param>
    public Applier(SyncStore? store, string journalPath, string snapshotsFolder, IReadOnlyDictionary<string, string?> roots, Func<bool> gameRunning, IApplyProbe? probe = null)
    {
        this.store = store;
        this.journalPath = journalPath;
        this.snapshotsFolder = snapshotsFolder;
        this.roots = roots;
        this.gameRunning = gameRunning;
        this.probe = probe;
    }

    /// <summary>Whether an interrupted apply is waiting to be finished or rolled back.</summary>
    public bool HasOpenJournal => File.Exists(journalPath);

    /// <summary>Reads the open journal, if any.</summary>
    public Journal? ReadJournal() => FileOps.ReadJson<Journal>(journalPath);

    /// <summary>Applies files from a source device's objects. Returns the snapshot ID.</summary>
    public string Apply(string sourceDeviceId, string sourceRevisionId, IReadOnlyList<ApplyTarget> targets)
    {
        var source = store ?? throw new InvalidOperationException("Applying needs the sync folder.");
        if (HasOpenJournal)
        {
            throw new RefusedException("Refuse_OpenJournal");
        }

        // Read and verify every source object before anything local is touched.
        probe?.Before("verify", -1);
        var contents = new Dictionary<ApplyTarget, byte[]>();
        foreach (var target in targets)
        {
            contents[target] = source.ReadObject(sourceDeviceId, target.Hash);
        }

        // Nothing may have changed since the plan was shown (review finding 4).
        foreach (var target in targets)
        {
            if (CurrentHash(target.Root, target.Path) != target.ExpectedLocalHash)
            {
                throw new RefusedException("Refuse_ChangedSincePlan");
            }
        }

        EnsureGameClosed();
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

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                probe?.Before("write", i);
                EnsureGameClosed();
                var target = targets[i];
                FileOps.WriteAtomic(TargetPath(target.Root, target.Path), contents[target], target.Hash);
                journal.Steps[i].Done = true;
                FileOps.WriteJsonAtomic(journalPath, journal);
            }

            probe?.Before("check", -1);
            foreach (var target in targets)
            {
                if (CurrentHash(target.Root, target.Path) != target.Hash)
                {
                    throw new IOException("An applied file does not match its expected hash.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RefusedException)
        {
            // A failure in the same session: back to the state before, at once (review finding 6).
            RestoreFiles(snapshot);
            File.Delete(journalPath);
            throw;
        }

        foreach (var file in snapshot.Files)
        {
            file.AppliedHash = targets.First(t => t.Root == file.Root && t.Path == file.Path).Hash;
        }

        FileOps.WriteJsonAtomic(Path.Combine(SnapshotFolder(snapshot.SnapshotId), "snapshot.json"), snapshot);
        File.Delete(journalPath);
        return snapshot.SnapshotId;
    }

    /// <summary>
    /// Finishes an interrupted apply: writes the steps not done yet, but only where the file is still
    /// as it was before the apply. Returns the files left alone because they changed since.
    /// </summary>
    public IReadOnlyList<string> FinishOpenJournal()
    {
        var source = store ?? throw new InvalidOperationException("Finishing needs the sync folder.");
        var journal = ReadJournal() ?? throw new InvalidOperationException("There is no interrupted apply.");
        EnsureGameClosed();
        var before = ReadSnapshot(journal.SnapshotId);
        TakeSnapshot(journal.Steps.Select(s => (s.Root, s.Path)), "safety", journal.SourceDeviceId, journal.SourceRevisionId);
        var leftAlone = new List<string>();
        foreach (var step in journal.Steps.Where(s => !s.Done))
        {
            var current = CurrentHash(step.Root, step.Path);
            if (current == step.Hash)
            {
                continue;
            }

            var saved = before?.Files.FirstOrDefault(f => f.Root == step.Root && f.Path == step.Path);
            if (saved is not null && current != saved.Hash)
            {
                leftAlone.Add(step.Root + "/" + step.Path);
                continue;
            }

            FileOps.WriteAtomic(TargetPath(step.Root, step.Path), source.ReadObject(journal.SourceDeviceId, step.Hash), step.Hash);
        }

        File.Delete(journalPath);
        return leftAlone;
    }

    /// <summary>Rolls an interrupted apply back to its snapshot, after saving the current state.</summary>
    public void RollBackOpenJournal()
    {
        var journal = ReadJournal() ?? throw new InvalidOperationException("There is no interrupted apply.");
        EnsureGameClosed();
        var snapshot = ReadSnapshot(journal.SnapshotId);
        if (snapshot is { Complete: true })
        {
            TakeSnapshot(snapshot.Files.Select(f => (f.Root, f.Path)), "safety", journal.SourceDeviceId, journal.SourceRevisionId);
            RestoreFiles(snapshot);
        }

        File.Delete(journalPath);
    }

    /// <summary>
    /// Restores every file of a snapshot exactly, after saving the current state in a new snapshot,
    /// so an undo can be undone. Returns the files that changed since the apply wrote them.
    /// </summary>
    public IReadOnlyList<string> Restore(string snapshotId)
    {
        EnsureGameClosed();
        var snapshot = ReadSnapshot(snapshotId) ?? throw new RefusedException("Refuse_NoSnapshot");
        if (!snapshot.Complete)
        {
            throw new RefusedException("Refuse_NoSnapshot");
        }

        var changedSince = ChangedSinceApply(snapshot);
        TakeSnapshot(snapshot.Files.Select(f => (f.Root, f.Path)), "safety", snapshot.SourceDeviceId, snapshot.SourceRevisionId);
        RestoreFiles(snapshot);
        return changedSince;
    }

    /// <summary>The files of a snapshot that changed since the apply wrote them.</summary>
    public IReadOnlyList<string> ChangedSinceApply(SnapshotManifest snapshot) =>
        snapshot.Files
            .Where(f => f.AppliedHash is not null && CurrentHash(f.Root, f.Path) != f.AppliedHash)
            .Select(f => f.Root + "/" + f.Path)
            .ToList();

    /// <summary>The snapshots of applies that can be undone, newest first.</summary>
    public IReadOnlyList<SnapshotManifest> ListSnapshots() =>
        ReadAllSnapshots().Where(s => s.Reason == "apply").ToList();

    /// <summary>Removes one snapshot.</summary>
    public void DeleteSnapshot(string snapshotId)
    {
        var folder = SnapshotFolder(snapshotId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Removes snapshots beyond the newest <paramref name="keep"/> that are older than <paramref name="minAge"/>.</summary>
    public int PruneSnapshots(int keep, TimeSpan minAge, DateTimeOffset now)
    {
        var removed = 0;
        foreach (var snapshot in ReadAllSnapshots().Skip(keep).Where(s => now - s.Created > minAge))
        {
            DeleteSnapshot(snapshot.SnapshotId);
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

        foreach (var (root, path) in files.Distinct())
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

    private IEnumerable<SnapshotManifest> ReadAllSnapshots()
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

        return list.OrderByDescending(s => s.Created).ThenByDescending(s => s.SnapshotId, StringComparer.Ordinal);
    }

    private SnapshotManifest? ReadSnapshot(string snapshotId) =>
        FileOps.ReadJson<SnapshotManifest>(Path.Combine(SnapshotFolder(snapshotId), "snapshot.json"));

    private void RestoreFiles(SnapshotManifest snapshot)
    {
        if (!snapshot.Complete)
        {
            // An incomplete snapshot means the apply never started writing; nothing to restore.
            return;
        }

        var folder = SnapshotFolder(snapshot.SnapshotId);
        foreach (var file in snapshot.Files)
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

    private void EnsureGameClosed()
    {
        if (gameRunning())
        {
            throw new RefusedException("Refuse_GameRunning");
        }
    }

    private string? CurrentHash(string root, string path)
    {
        var full = TargetPath(root, path);
        return File.Exists(full) ? FileOps.HashFile(full) : null;
    }

    private string SnapshotFolder(string snapshotId)
    {
        if (snapshotId.Length == 0 || snapshotId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || snapshotId.Contains("..", StringComparison.Ordinal))
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
