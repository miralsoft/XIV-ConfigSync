using System.Text;
using XivConfigSync.Core.Apply;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Inventory;
using XivConfigSync.Core.IO;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Store;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>One test per finding of the independent review (2026-11-04); each fails without its fix.</summary>
public sealed class ReviewTests
{
    private const string Hotbar = Device.Chr + "/HOTBAR.DAT";
    private const string Keybind = Device.Chr + "/KEYBIND.DAT";

    [Fact]
    public void Finding1AnOldSourceStateEqualToANewLocalOneIsAConflictNotAnApply()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        Align(a, b);

        // A goes to X1; B, without taking it, happens to set the same content X1; A goes on to X2.
        var x1 = Device.Random();
        a.WriteGame(Hotbar, x1);
        a.Engine.Backup(RevisionOrigin.Manual);
        b.WriteGame(Hotbar, x1);
        b.Engine.Backup(RevisionOrigin.Manual);
        a.WriteGame(Hotbar, Device.Random());
        a.Engine.Backup(RevisionOrigin.Manual);

        var item = Item(b.Engine.Plan(a.Settings.DeviceId, null), Hotbar);
        Assert.Equal(PlanAction.Conflict, item.Action);
        Assert.False(item.Selected);
        Assert.Equal((0, 1), b.Engine.AutoApply(a.Settings.DeviceId));
        Assert.Equal(x1, b.ReadGame(Hotbar));
    }

    [Fact]
    public void Finding2AFileLeftOutHereIsNeverAppliedOnItsOwn()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        Align(a, b);
        b.Engine.UpdateRules(b.Engine.Rules.WithExclusion(Roots.Game, Hotbar, true));
        var mine = b.ReadGame(Hotbar);

        a.WriteGame(Hotbar, Device.Random());
        a.Engine.Backup(RevisionOrigin.Manual);
        var item = Item(b.Engine.Plan(a.Settings.DeviceId, null), Hotbar);
        Assert.Equal(PlanAction.Offer, item.Action);
        Assert.Equal(PlanReason.NotScanned, item.Reason);
        Assert.NotNull(item.LocalHash);
        b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.Equal(mine, b.ReadGame(Hotbar));
    }

    [Fact]
    public void Finding3ADeviceSetUpAgainDoesNotPushItsDefaultsToTheOthers()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        Align(a, b);
        var bHotbar = b.ReadGame(Hotbar);

        // A's Windows is reinstalled: fresh default files, set up again under A's old identity.
        var again = new Device(temp, "a-again");
        again.Populate();
        again.Engine.JoinSyncFolder(temp.Sub("sync"), "A", a.Settings.DeviceId);
        again.Engine.Backup(RevisionOrigin.Manual);

        var item = Item(b.Engine.Plan(a.Settings.DeviceId, null), Hotbar);
        Assert.Equal(PlanAction.Conflict, item.Action);
        Assert.Equal(PlanReason.NoCommonState, item.Reason);
        Assert.Equal(0, b.Engine.AutoApply(a.Settings.DeviceId).Applied);
        Assert.Equal(bHotbar, b.ReadGame(Hotbar));
    }

    [Fact]
    public void Finding4AFileChangedAfterTheComparisonIsNotOverwritten()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        Align(a, b);
        a.WriteGame(Hotbar, Device.Random());
        a.Engine.Backup(RevisionOrigin.Manual);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.True(Item(plan, Hotbar).Selected);
        var played = Device.Random();
        b.WriteGame(Hotbar, played); // The user played between "compare" and "apply".

        Assert.Equal("Refuse_ChangedSincePlan", Assert.Throws<RefusedException>(() => b.Engine.Apply(plan)).ReasonKey);
        Assert.Equal(played, b.ReadGame(Hotbar));
        Assert.False(b.Engine.HasInterruptedApply);
    }

    [Fact]
    public void Finding4AGameStartedDuringTheWritesStopsAndRollsBack()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);

        FakeGame? game = null;
        var b = new Device(temp, "b", new ActionProbe("write", 1, () => game!.IsRunning = true));
        game = b.Game;
        b.Populate();
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);
        var before = State(b);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        foreach (var path in new[] { Hotbar, Keybind, "MACROSYS.dat" })
        {
            Item(plan, path).Selected = true;
        }

        Assert.Equal("Refuse_GameRunning", Assert.Throws<RefusedException>(() => b.Engine.Apply(plan)).ReasonKey);
        Assert.Equal(before, State(b));
        Assert.False(b.Engine.HasInterruptedApply);
    }

    [Fact]
    public void Finding5UndoSavesWhatChangedSinceTheApply()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Item(plan, Hotbar).Selected = true;
        var original = b.ReadGame(Hotbar);
        var result = b.Engine.Apply(plan);

        var played = Device.Random();
        b.WriteGame(Hotbar, played);
        var snapshot = b.Engine.ListSnapshots().Single(s => s.SnapshotId == result.SnapshotId);
        Assert.Single(b.Engine.ChangedSinceApply(snapshot));

        b.Engine.Undo(result.SnapshotId);
        Assert.Equal(original, b.ReadGame(Hotbar));

        // The played state was saved in a safety snapshot before the undo.
        var safety = Directory.GetDirectories(b.Local.SnapshotsFolder)
            .Select(d => FileOps.ReadJson<SnapshotManifest>(Path.Combine(d, "snapshot.json"))!)
            .Single(s => s.Reason == "safety");
        Assert.Equal(FileOps.HashBytes(played), safety.Files.Single().Hash);
    }

    [Fact]
    public void Finding6AWriteErrorRollsBackAtOnceAndNothingHalfAppliedIsBackedUp()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = new Device(temp, "b", new ActionProbe("write", 1, () => throw new IOException("Disk full (simulated).")));
        b.Populate();
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);
        var before = State(b);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        foreach (var path in new[] { Hotbar, Keybind })
        {
            Item(plan, path).Selected = true;
        }

        Assert.Throws<IOException>(() => b.Engine.Apply(plan));
        Assert.Equal(before, State(b));
        Assert.False(b.Engine.HasInterruptedApply);

        // With a journal left open (a crash), no backup is made.
        File.WriteAllText(b.Local.JournalPath, "{}");
        Assert.Equal("Refuse_OpenJournal", Assert.Throws<RefusedException>(() => b.Engine.Backup(RevisionOrigin.Manual)).ReasonKey);
    }

    [Fact]
    public void Finding7OneDeviceWithAMissingManifestDoesNotBlockTheOthers()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var c = new Device(temp, "c");
        c.Populate();
        c.Engine.JoinSyncFolder(temp.Sub("sync"), "C", null);
        var cHead = c.Engine.Backup(RevisionOrigin.Manual).RevisionId!;

        // C's HEAD arrived, its manifest not yet.
        File.Delete(Path.Combine(c.Engine.OpenStore().DeviceFolder(c.Settings.DeviceId), "revisions", cHead + ".json"));
        var fresh = b.Engine.FindNewRevisions();
        Assert.Contains(fresh, d => d.DeviceId == a.Settings.DeviceId);
        Assert.Contains("B", a.Engine.GetDevices().Single(d => d.IsThisDevice).ReceivedBy);
    }

    [Fact]
    public void Finding8AClockThatWentBackDoesNotLockBackupsOut()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        var store = a.Engine.OpenStore();

        // A revision from a clock one day ahead.
        var content = Encoding.UTF8.GetBytes("future");
        var hash = FileOps.HashBytes(content);
        store.PutObject(a.Settings.DeviceId, content, hash);
        var future = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(a.Settings.DeviceId, DateTimeOffset.UtcNow.AddDays(1)), DeviceId = a.Settings.DeviceId };
        future.Roots[Roots.Game] = new RootEntry { Files = [new FileEntry { Path = "x.dat", Hash = hash, Size = content.Length }] };
        store.Publish(future);

        var next = a.Engine.Backup(RevisionOrigin.Manual).RevisionId!;
        Assert.True(string.CompareOrdinal(next, future.RevisionId) > 0);
        Assert.Equal(next, store.ReadHead(a.Settings.DeviceId));
    }

    [Fact]
    public void Finding8AFailedBackupAfterASuccessfulApplyIsNotAFailedApply()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);

        // The game starts right after the last write: the apply is done, the backup must wait.
        FakeGame? game = null;
        var b = new Device(temp, "b", new ActionProbe("check", -1, () => game!.IsRunning = true));
        game = b.Game;
        b.Populate();
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Item(plan, Hotbar).Selected = true;
        var result = b.Engine.Apply(plan);
        Assert.Equal(a.ReadGame(Hotbar), b.ReadGame(Hotbar));
        Assert.Null(result.NewRevisionId);
        Assert.Equal(1, result.Applied);
    }
    [Fact]
    public void Finding11PlanningAnOlderRevisionIgnoresTheSourcesNewerOnes()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var older = a.Engine.ListRevisions(a.Settings.DeviceId)[0].RevisionId;
        var newer = Device.Random();
        a.WriteGame(Hotbar, newer);
        a.Engine.Backup(RevisionOrigin.Manual);
        b.WriteGame(Hotbar, newer); // B happens to have A's newer state, with no common state known.

        var item = Item(b.Engine.Plan(a.Settings.DeviceId, older), Hotbar);
        Assert.NotEqual(PlanAction.Apply, item.Action);
        Assert.False(item.Selected);
    }

    [Fact]
    public void Finding14TheApplicationsOwnTemporaryFilesAreNotBackedUp()
    {
        var rules = new RuleSet(new RuleFile());
        Assert.Equal("Exclude_Temp", rules.ExclusionReason(Roots.Game, Device.Chr + "/.HOTBAR.DAT.1a2b3c4d.tmp"));
        Assert.Equal("Exclude_Temp", rules.ExclusionReason(Roots.Plugins, ".Some.json.1a2b3c4d.tmp"));
    }

    [Fact]
    public void Finding15AManifestListingAFileTwiceIsRejected()
    {
        using var temp = new TempFolder();
        var device = SyncStore.NewDeviceId();
        var store = SyncStore.Create(temp.Sub("S"), device);
        store.WriteDeviceInfo(new DeviceInfo { DeviceId = device, DisplayName = "A" });
        var content = Encoding.UTF8.GetBytes("x");
        var hash = FileOps.HashBytes(content);
        store.PutObject(device, content, hash);
        var revision = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(device, DateTimeOffset.UtcNow), DeviceId = device };
        revision.Roots[Roots.Game] = new RootEntry
        {
            Files = [new FileEntry { Path = "a.dat", Hash = hash }, new FileEntry { Path = "A.DAT", Hash = hash }],
        };
        Assert.Throws<InvalidDataException>(() => store.Publish(revision));
    }

    [Fact]
    public void LinksAreNotFollowedButOrdinaryFilesAreScanned()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("root");
        Directory.CreateDirectory(Path.Combine(folder, "real"));
        File.WriteAllText(Path.Combine(folder, "real", "a.json"), "{}");
        var outside = temp.Sub("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.json"), "{}");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "link"), outside);
        }
        catch (IOException)
        {
            // Creating links needs developer mode or rights; the walk is then tested without one.
        }
        catch (UnauthorizedAccessException)
        {
        }

        var files = Scanner.Walk(folder).Select(f => Path.GetRelativePath(folder, f)).ToList();
        Assert.Contains(Path.Combine("real", "a.json"), files);
        Assert.DoesNotContain(files, f => f.Contains("secret", StringComparison.Ordinal));
    }

    private static (Device A, Device B) TwoDevices(TempFolder temp)
    {
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = new Device(temp, "b");
        b.Populate();
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);
        return (a, b);
    }

    private static void Align(Device a, Device b)
    {
        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        foreach (var path in new[] { Hotbar, Keybind, "MACROSYS.dat" })
        {
            Item(plan, path).Selected = true;
        }

        b.Engine.Apply(plan);
    }

    private static PlanItem Item(ApplyPlan plan, string path) => plan.Items.Single(i => i.Root == Roots.Game && i.Path == path);

    private static Dictionary<string, string> State(Device device) =>
        Directory.EnumerateFiles(device.GameFolder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => RelativePath.FromFullPath(device.GameFolder, f), FileOps.HashFile);

    /// <summary>Runs an action before a chosen stage of an apply.</summary>
    private sealed class ActionProbe(string stage, int index, Action action) : IApplyProbe
    {
        public void Before(string stage1, int index1)
        {
            if (stage1 == stage && index1 == index)
            {
                action();
            }
        }
    }
}
