using System.Reflection;
using XivConfigSync.Core.Apply;
using XivConfigSync.Core.Discovery;
using XivConfigSync.Core.Inventory;
using XivConfigSync.Core.Logging;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Platform;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Settings;
using XivConfigSync.Core.Store;

namespace XivConfigSync.Core.Engine;

/// <summary>An operation was refused for a reason the user should see; the message is a resource key.</summary>
public sealed class RefusedException : Exception
{
    public RefusedException(string reasonKey)
        : base(reasonKey)
    {
        ReasonKey = reasonKey;
    }

    public RefusedException()
    {
        ReasonKey = string.Empty;
    }

    public RefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
        ReasonKey = message;
    }

    /// <summary>The resource key of the reason.</summary>
    public string ReasonKey { get; }
}

/// <summary>The result of a backup.</summary>
public sealed record BackupResult(string? RevisionId, int Files, int NewObjects, int Excluded, bool Unchanged);

/// <summary>The result of an apply.</summary>
public sealed record ApplyResult(string SnapshotId, int Applied, string? NewRevisionId);

/// <summary>What the overview shows about one device.</summary>
public sealed record DeviceStatus(
    DeviceInfo Info,
    bool IsThisDevice,
    string? HeadRevisionId,
    DateTimeOffset? HeadCreated,
    IReadOnlyList<string> ReceivedBy);

/// <summary>
/// Ties the store, the scanner, the planner and the applier together for one device. All
/// operations that write take the device lock, so the window, the tray and a later headless run
/// never write at the same time (FR-066).
/// </summary>
public sealed class SyncEngine
{
    private readonly LocalData local;
    private readonly IGameMonitor game;
    private readonly ICloudStateReader cloud;
    private readonly Log log;
    private readonly IApplyProbe? probe;

    public SyncEngine(LocalData local, AppSettings settings, RuleSet rules, IGameMonitor game, ICloudStateReader cloud, Log log, IApplyProbe? probe = null)
    {
        this.local = local;
        Settings = settings;
        Rules = rules;
        this.game = game;
        this.cloud = cloud;
        this.log = log;
        this.probe = probe;
    }

    /// <summary>The application version written into revisions.</summary>
    public static string AppVersion { get; } =
        typeof(SyncEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public AppSettings Settings { get; }

    public RuleSet Rules { get; private set; }

    /// <summary>The configuration roots of this device.</summary>
    public IReadOnlyDictionary<string, string?> Roots => new Dictionary<string, string?>
    {
        [Model.Roots.Game] = Settings.GameConfigFolder,
        [Model.Roots.Plugins] = Settings.BackUpPlugins ? Settings.PluginConfigFolder : null,
    };

    /// <summary>The game version on this device, if it can be read.</summary>
    public string? LocalGameVersion => GameFolders.ReadGameVersion(Settings.GameInstallFolder);

    /// <summary>Saves the settings.</summary>
    public void SaveSettings() => Settings.Save(local.SettingsPath);

    /// <summary>Writes this device's display name into its part of the sync folder.</summary>
    public void UpdateDeviceInfo()
    {
        var store = OpenStore();
        var info = store.ListDevices().FirstOrDefault(d => d.DeviceId == Settings.DeviceId);
        if (info is not null && info.DisplayName != Settings.DisplayName)
        {
            info.DisplayName = Settings.DisplayName;
            info.LastSeen = DateTimeOffset.UtcNow;
            store.WriteDeviceInfo(info);
        }
    }

    /// <summary>Replaces the rules and saves them.</summary>
    public void UpdateRules(RuleSet rules)
    {
        Rules = rules;
        rules.Save(local.RulesPath);
    }

    /// <summary>Opens the configured sync folder.</summary>
    public SyncStore OpenStore() =>
        Settings.SyncFolder is null ? throw new RefusedException("Refuse_NoSyncFolder") : SyncStore.Open(Settings.SyncFolder);

    /// <summary>Makes a new sync folder and sets this device up in it.</summary>
    public SyncStore CreateSyncFolder(string folder, string displayName)
    {
        EnsureDeviceId();
        var store = SyncStore.Create(folder, Settings.DeviceId);
        JoinAs(store, Settings.DeviceId, displayName);
        return store;
    }

    /// <summary>Sets this device up in an existing sync folder, as a new device or as one set up again.</summary>
    public SyncStore JoinSyncFolder(string folder, string displayName, string? existingDeviceId)
    {
        var store = SyncStore.Open(folder);
        if (existingDeviceId is not null)
        {
            if (!store.ListDevices().Any(d => d.DeviceId == existingDeviceId))
            {
                throw new RefusedException("Refuse_UnknownDevice");
            }

            Settings.DeviceId = existingDeviceId;
        }
        else
        {
            Settings.DeviceId = SyncStore.NewDeviceId();
        }

        JoinAs(store, Settings.DeviceId, displayName);
        return store;
    }

    /// <summary>Backs up this device into its part of the sync folder (architecture, "Back up").</summary>
    public BackupResult Backup(RevisionOrigin origin)
    {
        var correlation = Correlation();
        if (game.IsRunning)
        {
            throw new RefusedException("Refuse_GameRunning");
        }

        using var busy = TakeLock();
        var store = OpenStore();
        var deviceId = Settings.DeviceId;
        var newObjects = 0;
        log.Info(correlation, $"Backup started ({origin}).");
        var scan = Scanner.Scan(Roots, Rules, (file, content) =>
        {
            if (store.PutObject(deviceId, content, file.Hash))
            {
                newObjects++;
            }
        });

        var head = store.ReadHead(deviceId);
        var previous = head is null ? null : store.ReadRevision(deviceId, head);
        if (previous is not null && SameContent(previous, scan))
        {
            log.Info(correlation, $"Backup: nothing changed since {head}.");
            Touch(store);
            return new BackupResult(head, scan.Files.Count, 0, scan.Excluded.Count, true);
        }

        var now = DateTimeOffset.UtcNow;
        var manifest = new RevisionManifest
        {
            RevisionId = SyncStore.NewRevisionId(deviceId, now),
            DeviceId = deviceId,
            Created = now,
            Parent = head,
            Origin = origin,
            AppVersion = AppVersion,
            GameVersion = LocalGameVersion,
            Excluded = scan.Excluded,
        };
        foreach (var group in scan.Files.GroupBy(f => f.Root))
        {
            manifest.Roots[group.Key] = new RootEntry
            {
                Files = group
                    .OrderBy(f => f.Path, StringComparer.Ordinal)
                    .Select(f => new FileEntry { Path = f.Path, Hash = f.Hash, Size = f.Size, Flags = [.. f.Flags] })
                    .ToList(),
            };
        }

        if (head is not null && string.CompareOrdinal(manifest.RevisionId, head) <= 0)
        {
            // The clock went back; keep IDs ordered so the newest revision stays the newest.
            throw new RefusedException("Refuse_ClockBehind");
        }

        store.Publish(manifest);
        Touch(store);
        var (removedRevisions, removedObjects) = store.ApplyRetention(deviceId, Settings.KeepRevisions, new HashSet<string>(StringComparer.Ordinal));
        log.Info(correlation, $"Backup published {manifest.RevisionId}: {scan.Files.Count} files, {newObjects} new, {scan.Excluded.Count} left out; retention removed {removedRevisions} revisions and {removedObjects} objects.");
        return new BackupResult(manifest.RevisionId, scan.Files.Count, newObjects, scan.Excluded.Count, false);
    }

    /// <summary>The devices in the sync folder with their latest revision and who received it.</summary>
    public IReadOnlyList<DeviceStatus> GetDevices()
    {
        var store = OpenStore();
        var devices = store.ListDevices();
        var result = new List<DeviceStatus>();
        foreach (var device in devices)
        {
            var head = store.ReadHead(device.DeviceId);
            DateTimeOffset? created = null;
            if (head is not null)
            {
                try
                {
                    created = store.ReadRevision(device.DeviceId, head).Created;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
                {
                    // Shown without a date; the revision is checked again when it is applied.
                }
            }

            var receivedBy = devices
                .Where(other => other.DeviceId != device.DeviceId && head is not null && store.ReadReceived(other.DeviceId, device.DeviceId)?.RevisionId == head)
                .Select(other => other.DisplayName)
                .ToList();
            result.Add(new DeviceStatus(device, device.DeviceId == Settings.DeviceId, head, created, receivedBy));
        }

        return result;
    }

    /// <summary>The upload state of this device's latest revision: the worst state of its files.</summary>
    public CloudFileState GetOwnUploadState()
    {
        var store = OpenStore();
        var head = store.ReadHead(Settings.DeviceId);
        if (head is null)
        {
            return CloudFileState.Unknown;
        }

        // Reading the state costs about 11 ms per file (gate 3), so only the files this revision
        // added are checked; the others were checked with an earlier revision.
        var folder = store.DeviceFolder(Settings.DeviceId);
        var manifest = store.ReadRevision(Settings.DeviceId, head);
        var earlier = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Parent is not null && store.ListRevisions(Settings.DeviceId).Contains(manifest.Parent))
        {
            earlier.UnionWith(store.ReadRevision(Settings.DeviceId, manifest.Parent).AllFiles().Select(f => f.File.Hash));
        }

        var paths = new List<string> { Path.Combine(folder, "HEAD"), Path.Combine(folder, "revisions", head + ".json") };
        paths.AddRange(manifest.AllFiles().Select(f => f.File.Hash).Where(h => !earlier.Contains(h)).Distinct().Select(h => store.ObjectPath(Settings.DeviceId, h)));
        var states = paths.Select(cloud.GetState).ToList();
        if (states.Any(s => s == CloudFileState.Unknown))
        {
            return CloudFileState.Unknown;
        }

        return states.Any(s => s == CloudFileState.Pending) ? CloudFileState.Pending : CloudFileState.InSync;
    }

    /// <summary>
    /// Checks the latest revision of every other device and, when all its files are present and
    /// intact, records that this device received it (architecture, "Upload and receipt state").
    /// </summary>
    public int ConfirmReceived()
    {
        using var busy = TakeLock();
        var store = OpenStore();
        var confirmed = 0;
        foreach (var device in store.ListDevices().Where(d => d.DeviceId != Settings.DeviceId))
        {
            var head = store.ReadHead(device.DeviceId);
            if (head is null || store.ReadReceived(Settings.DeviceId, device.DeviceId)?.RevisionId == head)
            {
                continue;
            }

            var manifest = store.ReadRevision(device.DeviceId, head);
            if (store.FindDamaged(manifest).Count == 0)
            {
                store.WriteReceived(Settings.DeviceId, device.DeviceId, head);
                confirmed++;
            }
        }

        return confirmed;
    }

    /// <summary>
    /// Finds other devices whose latest revision this device has not received yet, confirms the
    /// intact ones, and returns their names and IDs.
    /// </summary>
    public IReadOnlyList<DeviceInfo> FindNewRevisions()
    {
        var store = OpenStore();
        var fresh = new List<DeviceInfo>();
        foreach (var device in store.ListDevices().Where(d => d.DeviceId != Settings.DeviceId))
        {
            var head = store.ReadHead(device.DeviceId);
            if (head is not null && store.ReadReceived(Settings.DeviceId, device.DeviceId)?.RevisionId != head)
            {
                fresh.Add(device);
            }
        }

        if (fresh.Count > 0)
        {
            ConfirmReceived();
        }

        return fresh;
    }

    /// <summary>
    /// Applies from another device only what the rules allow without asking: files planned as
    /// <see cref="PlanAction.Apply"/>. Returns how many were applied and how many wait for the user.
    /// </summary>
    public (int Applied, int Waiting) AutoApply(string sourceDeviceId)
    {
        var plan = Plan(sourceDeviceId, null);
        // Offers are not counted: the game changes some of them on every login (V00), so a
        // notification for them would come every time. They wait on the transfer page.
        var waiting = plan.Items.Count(i => i.Action == PlanAction.Conflict && i.Rule == ApplyRule.Always);
        if (plan.Refusals.Count > 0 || !plan.Items.Any(i => i.Selected))
        {
            return (0, waiting);
        }

        foreach (var item in plan.Items)
        {
            item.Selected = item.Action == PlanAction.Apply;
        }

        var result = Apply(plan);
        return (result.Applied, waiting);
    }

    /// <summary>Plans applying a revision of any device (the latest when <paramref name="revisionId"/> is null).</summary>
    public ApplyPlan Plan(string sourceDeviceId, string? revisionId)
    {
        var store = OpenStore();
        var id = revisionId ?? store.ReadHead(sourceDeviceId) ?? throw new RefusedException("Refuse_NoRevision");
        var source = store.ReadRevision(sourceDeviceId, id);
        var scan = Scanner.Scan(Roots, Rules);
        var plan = ApplyPlanner.Plan(
            source,
            scan,
            BaseTable.Load(local.BasePath(sourceDeviceId)),
            History(store, sourceDeviceId, exclude: id),
            History(store, Settings.DeviceId, exclude: null),
            Rules,
            Settings.DeviceId,
            LocalGameVersion);

        var damaged = store.FindDamaged(source);
        if (damaged.Count > 0)
        {
            plan.Refusals.Add("Refuse_Damaged");
        }

        // A root this device does not back up (plugins turned off, or no Dalamud) is not offered.
        var unknownRoots = plan.Items.Select(i => i.Root).Distinct().Where(root => !Roots.TryGetValue(root, out var folder) || folder is null).ToList();
        if (unknownRoots.Count > 0)
        {
            plan.Items.RemoveAll(i => unknownRoots.Contains(i.Root));
            plan.Warnings.Add("Warn_PluginsOff");
        }

        return plan;
    }

    /// <summary>Applies the selected files of a plan (architecture, "Apply").</summary>
    public ApplyResult Apply(ApplyPlan plan)
    {
        var correlation = Correlation();
        if (plan.Refusals.Count > 0)
        {
            throw new RefusedException(plan.Refusals[0]);
        }

        if (game.IsRunning)
        {
            throw new RefusedException("Refuse_GameRunning");
        }

        var selected = plan.Selected.ToList();
        if (selected.Count == 0)
        {
            throw new RefusedException("Refuse_NothingSelected");
        }

        string snapshotId;
        using (TakeLock())
        {
            var store = OpenStore();
            var applier = new Applier(store, local.JournalPath, local.SnapshotsFolder, Roots, probe);
            if (applier.HasOpenJournal)
            {
                throw new RefusedException("Refuse_OpenJournal");
            }

            log.Info(correlation, $"Apply of {plan.SourceRevisionId}: {selected.Count} files.");
            snapshotId = applier.Apply(plan.SourceDeviceId, plan.SourceRevisionId, selected.Select(i => new ApplyTarget(i.Root, i.Path, i.SourceHash)).ToList());

            if (!plan.IsOwnDevice)
            {
                var bases = BaseTable.Load(local.BasePath(plan.SourceDeviceId));
                foreach (var item in plan.Items.Where(i => i.Selected || i.Reason == PlanReason.UpToDate))
                {
                    bases.Set(item.Root, item.Path, item.SourceHash);
                }

                bases.Save(local.BasePath(plan.SourceDeviceId));
            }

            applier.PruneSnapshots(20, TimeSpan.FromDays(30), DateTimeOffset.UtcNow);
            log.Info(correlation, $"Apply done; snapshot {snapshotId}.");
        }

        var backup = Backup(RevisionOrigin.AfterApply);
        return new ApplyResult(snapshotId, selected.Count, backup.RevisionId);
    }

    /// <summary>The snapshots that can be restored, newest first.</summary>
    public IReadOnlyList<SnapshotManifest> ListSnapshots() => NewApplier(OpenStore()).ListSnapshots();

    /// <summary>Undoes an apply by restoring its snapshot, then backs up the restored state.</summary>
    public void Undo(string snapshotId)
    {
        if (game.IsRunning)
        {
            throw new RefusedException("Refuse_GameRunning");
        }

        using (TakeLock())
        {
            var applier = NewApplier(OpenStore());
            var snapshot = applier.ListSnapshots().FirstOrDefault(s => s.SnapshotId == snapshotId)
                ?? throw new RefusedException("Refuse_NoSnapshot");
            applier.Restore(snapshotId);

            // The files are no longer in the state both devices shared: forget that state, so the
            // next comparison offers them again for a decision instead of skipping them.
            if (snapshot.SourceDeviceId is not null && snapshot.SourceDeviceId != Settings.DeviceId && SyncStore.IsDeviceId(snapshot.SourceDeviceId))
            {
                var bases = BaseTable.Load(local.BasePath(snapshot.SourceDeviceId));
                foreach (var file in snapshot.Files)
                {
                    bases.Files.Remove(BaseTable.Key(file.Root, file.Path));
                }

                bases.Save(local.BasePath(snapshot.SourceDeviceId));
            }

            // An undone apply is not offered for undo again; the next "undo" takes the one before.
            applier.DeleteSnapshot(snapshotId);
            log.Info(Correlation(), $"Snapshot {snapshotId} restored.");
        }

        Backup(RevisionOrigin.AfterApply);
    }

    /// <summary>Whether an interrupted apply waits (FR-066).</summary>
    public bool HasInterruptedApply => File.Exists(local.JournalPath);

    /// <summary>Finishes or rolls back an interrupted apply.</summary>
    public void ResolveInterruptedApply(bool rollBack)
    {
        if (game.IsRunning)
        {
            throw new RefusedException("Refuse_GameRunning");
        }

        using var busy = TakeLock();
        var applier = NewApplier(OpenStore());
        if (rollBack)
        {
            applier.RollBackOpenJournal();
        }
        else
        {
            applier.FinishOpenJournal();
        }

        log.Info(Correlation(), rollBack ? "Interrupted apply rolled back." : "Interrupted apply finished.");
    }

    /// <summary>Revisions of a device, newest first, with their dates.</summary>
    public IReadOnlyList<(string RevisionId, DateTimeOffset Created, RevisionOrigin Origin, int Files)> ListRevisions(string deviceId)
    {
        var store = OpenStore();
        var list = new List<(string, DateTimeOffset, RevisionOrigin, int)>();
        foreach (var id in store.ListRevisions(deviceId))
        {
            try
            {
                var manifest = store.ReadRevision(deviceId, id);
                list.Add((id, manifest.Created, manifest.Origin, manifest.AllFiles().Count()));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                // Not offered.
            }
        }

        return list;
    }

    /// <summary>A scan of this device without storing anything; for diagnostics.</summary>
    public ScanResult ScanOnly() => Scanner.Scan(Roots, Rules);

    private static Dictionary<string, HashSet<string>> History(SyncStore store, string deviceId, string? exclude)
    {
        var history = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in store.ListRevisions(deviceId).Where(id => id != exclude))
        {
            RevisionManifest manifest;
            try
            {
                manifest = store.ReadRevision(deviceId, id);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                continue;
            }

            foreach (var (root, file) in manifest.AllFiles())
            {
                var key = BaseTable.Key(root, file.Path);
                if (!history.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    history[key] = set;
                }

                set.Add(file.Hash);
            }
        }

        return history;
    }

    private static bool SameContent(RevisionManifest previous, ScanResult scan)
    {
        var before = previous.AllFiles().Select(f => BaseTable.Key(f.Root, f.File.Path) + "|" + f.File.Hash).ToHashSet(StringComparer.Ordinal);
        var now = scan.Files.Select(f => BaseTable.Key(f.Root, f.Path) + "|" + f.Hash).ToHashSet(StringComparer.Ordinal);
        return before.SetEquals(now);
    }

    private static string Correlation() => Guid.NewGuid().ToString("N")[..8];

    private Applier NewApplier(SyncStore store) => new(store, local.JournalPath, local.SnapshotsFolder, Roots, probe);

    private void EnsureDeviceId()
    {
        if (!SyncStore.IsDeviceId(Settings.DeviceId))
        {
            Settings.DeviceId = SyncStore.NewDeviceId();
        }
    }

    private void JoinAs(SyncStore store, string deviceId, string displayName)
    {
        var existing = store.ListDevices().FirstOrDefault(d => d.DeviceId == deviceId);
        store.WriteDeviceInfo(new DeviceInfo
        {
            DeviceId = deviceId,
            DisplayName = displayName,
            Created = existing?.Created ?? DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
        });
        Settings.DisplayName = displayName;
        Settings.SyncFolder = store.Root;
        Settings.Save(local.SettingsPath);
    }

    private void Touch(SyncStore store)
    {
        var info = store.ListDevices().FirstOrDefault(d => d.DeviceId == Settings.DeviceId);
        if (info is not null && DateTimeOffset.UtcNow - info.LastSeen > TimeSpan.FromHours(1))
        {
            info.LastSeen = DateTimeOffset.UtcNow;
            store.WriteDeviceInfo(info);
        }
    }

    private FileStream TakeLock()
    {
        try
        {
            return new FileStream(local.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex)
        {
            throw new RefusedException("Refuse_Busy", ex);
        }
    }
}
