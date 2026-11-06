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
    /// <summary>Snapshot of an apply; offered for undo.</summary>
    public const string ApplyReason = "apply";

    /// <summary>The state saved before an undo or a recovery; kept on disk for manual recovery.</summary>
    public const string SafetyReason = "safety";

    public int Schema { get; set; } = 1;

    public string SnapshotId { get; set; } = string.Empty;

    public string Reason { get; set; } = ApplyReason;

    public DateTimeOffset Created { get; set; }

    public string? SourceDeviceId { get; set; }

    public string? SourceRevisionId { get; set; }

    /// <summary>The folder each root had when the snapshot was taken; a restore refuses another one.</summary>
    public Dictionary<string, string> RootFolders { get; set; } = [];

    public List<SnapshotFile> Files { get; set; } = [];

    public bool Complete { get; set; }
}

/// <summary>One step of an apply in the journal.</summary>
public sealed class JournalStep
{
    public string Root { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    /// <summary>The device whose part holds the object, when it is not the source (a merged file in this device's part).</summary>
    public string? ObjectDevice { get; set; }

    public bool Done { get; set; }
}

/// <summary>
/// Content of <c>journal.json</c>, present only while an apply or an undo writes. Finishing it or
/// rolling it back is decided by the user at the next start (FR-066).
/// </summary>
public sealed class Journal
{
    /// <summary>An apply from the store.</summary>
    public const string ApplyKind = "apply";

    /// <summary>An undo (a restore of a snapshot).</summary>
    public const string RestoreKind = "restore";

    public int Schema { get; set; } = 1;

    public string Kind { get; set; } = ApplyKind;

    public string ApplyId { get; set; } = string.Empty;

    /// <summary>The state before the writes: the apply's snapshot, or for a restore the safety snapshot.</summary>
    public string SnapshotId { get; set; } = string.Empty;

    /// <summary>For a restore: the snapshot being restored.</summary>
    public string? TargetSnapshotId { get; set; }

    public string SourceDeviceId { get; set; } = string.Empty;

    public string SourceRevisionId { get; set; } = string.Empty;

    /// <summary>The folder each root had when the journal was written; recovery refuses another one.</summary>
    public Dictionary<string, string> RootFolders { get; set; } = [];

    public List<JournalStep> Steps { get; set; } = [];
}

/// <summary>A file to write in an apply, with the hash the file had when the plan was made (null: absent).</summary>
/// <param name="Root">The root.</param>
/// <param name="Path">The path below the root.</param>
/// <param name="Hash">The object written.</param>
/// <param name="ExpectedLocalHash">The hash the file had when the plan was made; null: absent.</param>
/// <param name="ObjectDevice">The device whose part holds the object, when it is not the source.</param>
public sealed record ApplyTarget(string Root, string Path, string Hash, string? ExpectedLocalHash, string? ObjectDevice = null);

/// <summary>Test seam: called before each stage, so a test can interrupt an apply anywhere.</summary>
public interface IApplyProbe
{
    /// <summary>Called before a stage; <paramref name="index"/> is the file index for per-file stages.</summary>
    void Before(string stage, int index);
}

/// <summary>
/// Writes into the configuration folders: applies from the store and restores snapshots. Every
/// write run has a verified snapshot of the state before and a journal; the game is checked before
/// every single file (X-01). An I/O failure rolls back at once; a game started in between, a crash
/// or a power cut leave the journal, and the user finishes or rolls back at the next start.
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
    /// <param name="journalPath">Where the journal of a running write is kept.</param>
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

    /// <summary>Whether an interrupted write is waiting to be finished or rolled back.</summary>
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
            contents[target] = source.ReadObject(target.ObjectDevice ?? sourceDeviceId, target.Hash);
        }

        // Nothing may have changed since the plan was shown.
        foreach (var target in targets)
        {
            if (CurrentHash(target.Root, target.Path) != target.ExpectedLocalHash)
            {
                throw new RefusedException("Refuse_ChangedSincePlan");
            }
        }

        EnsureGameClosed();
        probe?.Before("snapshot", -1);
        var snapshot = TakeSnapshot(targets.Select(t => (t.Root, t.Path)), SnapshotManifest.ApplyReason, sourceDeviceId, sourceRevisionId);

        // The snapshot is the state the plan saw, also for a change in the moment between the check
        // and the copy.
        if (snapshot.Files.Any(f => f.Hash != targets.First(t => t.Root == f.Root && t.Path == f.Path).ExpectedLocalHash))
        {
            DeleteSnapshot(snapshot.SnapshotId);
            throw new RefusedException("Refuse_ChangedSincePlan");
        }

        var journal = NewJournal(Journal.ApplyKind, snapshot.SnapshotId, null, sourceDeviceId, sourceRevisionId);
        journal.Steps = targets.Select(t => new JournalStep { Root = t.Root, Path = t.Path, Hash = t.Hash, ObjectDevice = t.ObjectDevice }).ToList();
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
        catch (RefusedException refused) when (refused.ReasonKey == "Refuse_GameRunning")
        {
            // The game was started: nothing more is written now, not even a rollback. The journal
            // stays, and the user finishes or rolls back once the game is closed.
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RefusedException)
        {
            // A failure in the same session: back to the state before, at once. If the rollback
            // itself fails, the journal stays for the recovery at the next start.
            RestoreFiles(snapshot);
            File.Delete(journalPath);
            DeleteSnapshot(snapshot.SnapshotId);
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
    /// Finishes an interrupted write. An apply: writes the steps not done yet, but only where the
    /// file is still as it was before the apply. An undo: restores its snapshot. Returns the files
    /// left alone because they changed since.
    /// </summary>
    public IReadOnlyList<string> FinishOpenJournal()
    {
        var journal = ReadJournal() ?? throw new InvalidOperationException("There is no interrupted write.");
        EnsureGameClosed();
        EnsureSameFolders(journal.RootFolders);
        if (journal.Kind == Journal.RestoreKind)
        {
            var target = ReadSnapshot(journal.TargetSnapshotId ?? string.Empty);
            if (target is { Complete: true })
            {
                RestoreFiles(target);
                DeleteSnapshot(target.SnapshotId);
            }

            File.Delete(journalPath);
            return [];
        }

        var source = store ?? throw new InvalidOperationException("Finishing needs the sync folder.");
        var before = ReadSnapshot(journal.SnapshotId);
        TakeSnapshot(journal.Steps.Select(s => (s.Root, s.Path)), SnapshotManifest.SafetyReason, journal.SourceDeviceId, journal.SourceRevisionId);
        var leftAlone = new List<string>();
        foreach (var step in journal.Steps.Where(s => !s.Done))
        {
            var current = CurrentHash(step.Root, step.Path);
            if (current == step.Hash)
            {
                continue;
            }

            // Without a readable state from before, nothing proves the file is unchanged: leave it.
            var saved = before is { Complete: true } ? before.Files.FirstOrDefault(f => f.Root == step.Root && f.Path == step.Path) : null;
            if (saved is null || current != saved.Hash)
            {
                leftAlone.Add(step.Root + "/" + step.Path);
                continue;
            }

            EnsureGameClosed();
            FileOps.WriteAtomic(TargetPath(step.Root, step.Path), source.ReadObject(step.ObjectDevice ?? journal.SourceDeviceId, step.Hash), step.Hash);
        }

        if (before is { Complete: true })
        {
            foreach (var file in before.Files)
            {
                var step = journal.Steps.FirstOrDefault(s => s.Root == file.Root && s.Path == file.Path);
                file.AppliedHash = step is not null && CurrentHash(file.Root, file.Path) == step.Hash ? step.Hash : null;
            }

            FileOps.WriteJsonAtomic(Path.Combine(SnapshotFolder(before.SnapshotId), "snapshot.json"), before);
        }

        File.Delete(journalPath);
        return leftAlone;
    }

    /// <summary>Rolls an interrupted write back to the state before it, after saving the current state.</summary>
    public void RollBackOpenJournal()
    {
        var journal = ReadJournal() ?? throw new InvalidOperationException("There is no interrupted write.");
        EnsureGameClosed();
        EnsureSameFolders(journal.RootFolders);
        var snapshot = ReadSnapshot(journal.SnapshotId);
        if (snapshot is { Complete: true })
        {
            TakeSnapshot(snapshot.Files.Select(f => (f.Root, f.Path)), SnapshotManifest.SafetyReason, journal.SourceDeviceId, journal.SourceRevisionId);
            RestoreFiles(snapshot);
            if (journal.Kind == Journal.ApplyKind)
            {
                // A rolled-back apply is not offered for undo (review 2, finding 2).
                DeleteSnapshot(snapshot.SnapshotId);
            }
        }

        File.Delete(journalPath);
    }

    /// <summary>
    /// Undoes an apply: saves the current state (safety snapshot), writes a journal, restores the
    /// apply's snapshot file by file. Returns the files that changed since the apply wrote them.
    /// </summary>
    public IReadOnlyList<string> Restore(string snapshotId)
    {
        if (HasOpenJournal)
        {
            throw new RefusedException("Refuse_OpenJournal");
        }

        EnsureGameClosed();
        var target = ReadSnapshot(snapshotId);
        if (target is not { Complete: true })
        {
            throw new RefusedException("Refuse_NoSnapshot");
        }

        EnsureSameFolders(target.RootFolders);
        var changedSince = ChangedSinceApply(target);
        var safety = TakeSnapshot(target.Files.Select(f => (f.Root, f.Path)), SnapshotManifest.SafetyReason, target.SourceDeviceId, target.SourceRevisionId);
        FileOps.WriteJsonAtomic(journalPath, NewJournal(Journal.RestoreKind, safety.SnapshotId, target.SnapshotId, target.SourceDeviceId ?? string.Empty, target.SourceRevisionId ?? string.Empty));
        try
        {
            RestoreFiles(target);
        }
        catch (RefusedException refused) when (refused.ReasonKey == "Refuse_GameRunning")
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RestoreFiles(safety);
            File.Delete(journalPath);
            throw;
        }

        File.Delete(journalPath);
        return changedSince;
    }

    /// <summary>
    /// The files of an apply's snapshot that changed since the apply wrote them. A file whose
    /// written state is not known counts as changed, so the user is warned rather than not.
    /// </summary>
    public IReadOnlyList<string> ChangedSinceApply(SnapshotManifest snapshot) =>
        (snapshot.Files ?? [])
            .Where(f => f.AppliedHash is null || CurrentHash(f.Root, f.Path) != f.AppliedHash)
            .Select(f => f.Root + "/" + f.Path)
            .ToList();

    /// <summary>The snapshots of applies that can be undone, newest first.</summary>
    public IReadOnlyList<SnapshotManifest> ListSnapshots() =>
        ReadAllSnapshots().Where(s => s.Reason == SnapshotManifest.ApplyReason).ToList();

    /// <summary>Removes one snapshot.</summary>
    public void DeleteSnapshot(string snapshotId)
    {
        var folder = SnapshotFolder(snapshotId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Keeps the newest <paramref name="keep"/> snapshots of each kind and every one younger than
    /// <paramref name="minAge"/>; removes incomplete snapshot folders older than a day. Best effort.
    /// </summary>
    public int PruneSnapshots(int keep, TimeSpan minAge, DateTimeOffset now)
    {
        var removed = 0;
        var all = ReadAllSnapshots().ToList();
        var old = all.GroupBy(s => s.Reason).SelectMany(group => group.Skip(keep)).Where(s => now - s.Created > minAge);
        foreach (var snapshot in old)
        {
            removed += TryDelete(Path.Combine(snapshotsFolder, snapshot.SnapshotId)) ? 1 : 0;
        }

        if (Directory.Exists(snapshotsFolder))
        {
            var complete = all.Select(s => s.SnapshotId).ToHashSet(StringComparer.Ordinal);
            foreach (var folder in Directory.GetDirectories(snapshotsFolder))
            {
                if (!complete.Contains(Path.GetFileName(folder)) && now.UtcDateTime - Directory.GetLastWriteTimeUtc(folder) > TimeSpan.FromDays(1))
                {
                    removed += TryDelete(folder) ? 1 : 0;
                }
            }
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
        var list = files.Distinct().ToList();
        var manifest = new SnapshotManifest
        {
            SnapshotId = id,
            Reason = reason,
            Created = now,
            SourceDeviceId = sourceDeviceId,
            SourceRevisionId = sourceRevisionId,
            RootFolders = CurrentFolders(list.Select(f => f.Root)),
        };
        FileOps.WriteJsonAtomic(Path.Combine(folder, "snapshot.json"), manifest);

        foreach (var (root, path) in list)
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

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Journal NewJournal(string kind, string snapshotId, string? targetSnapshotId, string sourceDeviceId, string sourceRevisionId) => new()
    {
        Kind = kind,
        ApplyId = Guid.NewGuid().ToString("N"),
        SnapshotId = snapshotId,
        TargetSnapshotId = targetSnapshotId,
        SourceDeviceId = sourceDeviceId,
        SourceRevisionId = sourceRevisionId,
        RootFolders = CurrentFolders(roots.Keys),
    };

    private Dictionary<string, string> CurrentFolders(IEnumerable<string> rootNames) =>
        rootNames.Distinct()
            .Where(r => roots.TryGetValue(r, out var folder) && folder is not null)
            .ToDictionary(r => r, r => Path.GetFullPath(roots[r]!));

    /// <summary>A recovery or undo must write into the folders it saved from (review 2, finding 10).</summary>
    private void EnsureSameFolders(Dictionary<string, string>? recorded)
    {
        foreach (var (root, folder) in recorded ?? [])
        {
            if (!roots.TryGetValue(root, out var now) || now is null
                || !string.Equals(Path.GetFullPath(now), folder, StringComparison.OrdinalIgnoreCase))
            {
                throw new RefusedException("Refuse_FolderChanged");
            }
        }
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
                if (manifest is { Complete: true } && manifest.SnapshotId == Path.GetFileName(folder))
                {
                    manifest.Files ??= [];
                    manifest.RootFolders ??= [];
                    list.Add(manifest);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // A damaged snapshot is not offered.
            }
        }

        return list.OrderByDescending(s => s.Created).ThenByDescending(s => s.SnapshotId, StringComparer.Ordinal);
    }

    private SnapshotManifest? ReadSnapshot(string snapshotId)
    {
        if (snapshotId.Length == 0)
        {
            return null;
        }

        try
        {
            var manifest = FileOps.ReadJson<SnapshotManifest>(Path.Combine(SnapshotFolder(snapshotId), "snapshot.json"));
            if (manifest is not null)
            {
                manifest.Files ??= [];
                manifest.RootFolders ??= [];
            }

            return manifest;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void RestoreFiles(SnapshotManifest snapshot)
    {
        if (!snapshot.Complete)
        {
            // An incomplete snapshot means the write never started; nothing to restore.
            return;
        }

        var folder = SnapshotFolder(snapshot.SnapshotId);
        foreach (var file in snapshot.Files)
        {
            var target = TargetPath(file.Root, file.Path);
            if (file.Existed)
            {
                if (CurrentHash(file.Root, file.Path) == file.Hash)
                {
                    continue;
                }

                EnsureGameClosed();
                var saved = RelativePath.Combine(Path.Combine(folder, "files", file.Root), file.Path);
                FileOps.CopyAtomic(saved, target, file.Hash!);
            }
            else if (File.Exists(target))
            {
                EnsureGameClosed();
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
            throw new InvalidDataException("Invalid snapshot ID.");
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
