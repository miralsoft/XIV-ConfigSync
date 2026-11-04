using XivConfigSync.Core.Engine;
using XivConfigSync.Core.IO;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Store;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>Whole flows with two or three simulated devices sharing one sync folder.</summary>
public sealed class FlowTests
{
    private const string Hotbar = Device.Chr + "/HOTBAR.DAT";
    private const string Keybind = Device.Chr + "/KEYBIND.DAT";
    private const string Addon = Device.Chr + "/ADDON.DAT";

    [Fact]
    public void FirstBackupThenUnchangedBackupMakesNoNewRevision()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");

        var first = a.Engine.Backup(RevisionOrigin.Manual);
        Assert.False(first.Unchanged);
        Assert.Equal(8, first.Files); // 7 game files without the .old, 1 plugin file.

        var second = a.Engine.Backup(RevisionOrigin.Manual);
        Assert.True(second.Unchanged);
        Assert.Equal(first.RevisionId, second.RevisionId);

        var manifest = a.Engine.OpenStore().ReadRevision(a.Settings.DeviceId, first.RevisionId!);
        Assert.DoesNotContain(manifest.AllFiles(), f => f.File.Path.EndsWith(".old", StringComparison.Ordinal));
        Assert.Contains(manifest.AllFiles(), f => f.File.Path == "FFXIV.cfg" && f.File.Flags.Contains(FileFlags.Credential));
    }

    [Fact]
    public void SecondDeviceJoinsAndTakesSharedFilesButNeverTheDeviceFiles()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Empty(plan.Refusals);

        // First contact: both sides have different files and no common state, so the shared ones are conflicts.
        Assert.Equal(PlanAction.Conflict, Item(plan, Hotbar).Action);
        Assert.Equal(PlanAction.Skip, Item(plan, "FFXIV.cfg").Action);
        Assert.Equal(PlanReason.NeverRule, Item(plan, "FFXIV.cfg").Reason);

        // The user takes the shared files from A.
        Item(plan, Hotbar).Selected = true;
        Item(plan, Keybind).Selected = true;
        Item(plan, "MACROSYS.dat").Selected = true;
        var cfgBefore = b.ReadGame("FFXIV.cfg");
        b.Engine.Apply(plan);

        Assert.Equal(a.ReadGame(Hotbar), b.ReadGame(Hotbar));
        Assert.Equal(a.ReadGame(Keybind), b.ReadGame(Keybind));
        Assert.Equal(cfgBefore, b.ReadGame("FFXIV.cfg"));
        Assert.NotEqual(a.ReadGame(Addon), b.ReadGame(Addon));
    }

    [Fact]
    public void ALaterChangeOnOneDeviceIsAppliedOnTheOtherWithoutAsking()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        AlignShared(a, b);

        // A changes its hotbar and backs up.
        var changed = Device.Random();
        a.WriteGame(Hotbar, changed);
        a.Engine.Backup(RevisionOrigin.GameExit);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Equal(PlanAction.Apply, Item(plan, Hotbar).Action);
        Assert.True(Item(plan, Hotbar).Selected);
        Assert.Equal(PlanAction.Skip, Item(plan, Keybind).Action);
        Assert.Equal(PlanReason.UpToDate, Item(plan, Keybind).Reason);
        b.Engine.Apply(plan);
        Assert.Equal(changed, b.ReadGame(Hotbar));

        // And back: B's next change reaches A the same way, without a conflict.
        var fromB = Device.Random();
        b.WriteGame(Hotbar, fromB);
        b.Engine.Backup(RevisionOrigin.GameExit);
        var back = a.Engine.Plan(b.Settings.DeviceId, null);
        Assert.Equal(PlanAction.Apply, Item(back, Hotbar).Action);
        a.Engine.Apply(back);
        Assert.Equal(fromB, a.ReadGame(Hotbar));
    }

    [Fact]
    public void AnOnRequestFileIsOfferedNotApplied()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        AlignShared(a, b, Addon);

        a.WriteGame(Addon, Device.Random());
        a.Engine.Backup(RevisionOrigin.GameExit);
        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Equal(PlanAction.Offer, Item(plan, Addon).Action);
        Assert.False(Item(plan, Addon).Selected);
    }

    [Fact]
    public void ChangesOnBothDevicesAreAConflictAndBothVersionsAreKept()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        AlignShared(a, b);

        var onA = Device.Random();
        var onB = Device.Random();
        a.WriteGame(Hotbar, onA);
        a.Engine.Backup(RevisionOrigin.GameExit);
        b.WriteGame(Hotbar, onB);
        b.Engine.Backup(RevisionOrigin.GameExit);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        var item = Item(plan, Hotbar);
        Assert.Equal(PlanAction.Conflict, item.Action);
        Assert.Equal(PlanReason.BothChanged, item.Reason);
        Assert.False(item.Selected);

        // Nothing is lost: B keeps its file, A's version stays in the store.
        Assert.Equal(onB, b.ReadGame(Hotbar));
        var store = b.Engine.OpenStore();
        Assert.Equal(onA, store.ReadObject(a.Settings.DeviceId, item.SourceHash));
    }

    [Fact]
    public void AnOutdatedSourceDoesNotOverwriteANewerLocalFile()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        AlignShared(a, b);

        // B changes the hotbar; A has not changed anything since.
        b.WriteGame(Hotbar, Device.Random());
        b.Engine.Backup(RevisionOrigin.GameExit);
        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Equal(PlanAction.Offer, Item(plan, Hotbar).Action);
        Assert.Equal(PlanReason.LocalChangedOnly, Item(plan, Hotbar).Reason);
        Assert.False(Item(plan, Hotbar).Selected);
    }

    [Fact]
    public void NothingIsWrittenWhileTheGameRuns()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Item(plan, Hotbar).Selected = true;
        var before = b.ReadGame(Hotbar);

        b.Game.IsRunning = true;
        Assert.Equal("Refuse_GameRunning", Assert.Throws<RefusedException>(() => b.Engine.Apply(plan)).ReasonKey);
        Assert.Equal("Refuse_GameRunning", Assert.Throws<RefusedException>(() => b.Engine.Backup(RevisionOrigin.Manual)).ReasonKey);
        Assert.Equal(before, b.ReadGame(Hotbar));
    }

    [Fact]
    public void ADifferentGameVersionRefusesTheApply()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        WriteVersion(temp.Sub("a-install"), "2026.10.16.0000.0000");
        WriteVersion(temp.Sub("b-install"), "2026.11.01.0000.0000");
        a.Settings.GameInstallFolder = temp.Sub("a-install");
        b.Settings.GameInstallFolder = temp.Sub("b-install");
        a.WriteGame(Hotbar, Device.Random());
        a.Engine.Backup(RevisionOrigin.Manual);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Contains("Refuse_GameVersion", plan.Refusals);
        Assert.Throws<RefusedException>(() => b.Engine.Apply(plan));
    }

    [Fact]
    public void ADamagedSourceIsRefused()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var store = a.Engine.OpenStore();
        var head = store.ReadRevision(a.Settings.DeviceId, store.ReadHead(a.Settings.DeviceId)!);
        var hotbar = head.AllFiles().Single(f => f.File.Path == Hotbar).File;
        File.WriteAllText(store.ObjectPath(a.Settings.DeviceId, hotbar.Hash), "damaged");

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Contains("Refuse_Damaged", plan.Refusals);
    }

    [Fact]
    public void UndoRestoresTheExactStateBeforeTheApply()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        a.WriteGame(Device.Chr + "/NEW.DAT", Device.Random());
        a.Engine.Backup(RevisionOrigin.Manual);
        var before = Snapshot(b);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        foreach (var item in plan.Items.Where(i => i.Selectable))
        {
            item.Selected = true;
        }

        var result = b.Engine.Apply(plan);
        Assert.NotEqual(before, Snapshot(b));
        b.Engine.Undo(result.SnapshotId);
        Assert.Equal(before, Snapshot(b));
        Assert.False(b.ExistsGame(Device.Chr + "/NEW.DAT"));

        // The undone apply is gone from the list, and its files are offered again for a decision.
        Assert.DoesNotContain(b.Engine.ListSnapshots(), s => s.SnapshotId == result.SnapshotId);
        var again = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Equal(PlanAction.Offer, Item(again, Hotbar).Action);
        Assert.False(Item(again, Hotbar).Selected);
        Assert.Throws<RefusedException>(() => b.Engine.Undo(result.SnapshotId));
    }

    [Fact]
    public void RestoringAnOlderOwnRevisionOffersEveryChangedFileIncludingDeviceFiles()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        var old = a.Engine.Backup(RevisionOrigin.Manual).RevisionId!;
        var oldCfg = a.ReadGame("FFXIV.cfg");
        Thread.Sleep(5);
        a.WriteGame("FFXIV.cfg", Device.Random());
        a.Engine.Backup(RevisionOrigin.Manual);

        var plan = a.Engine.Plan(a.Settings.DeviceId, old);
        Assert.True(plan.IsOwnDevice);
        Assert.Equal(PlanAction.Apply, Item(plan, "FFXIV.cfg").Action);
        a.Engine.Apply(plan);
        Assert.Equal(oldCfg, a.ReadGame("FFXIV.cfg"));
    }

    [Theory]
    [InlineData("verify", -1)]
    [InlineData("snapshot", -1)]
    [InlineData("journal", -1)]
    [InlineData("write", 0)]
    [InlineData("write", 1)]
    [InlineData("write", 2)]
    [InlineData("check", -1)]
    public void ACrashAnywhereRecoversToTheOldOrTheNewStateNeverAMix(string stage, int index)
    {
        foreach (var rollBack in new[] { true, false })
        {
            using var temp = new TempFolder();
            var a = new Device(temp, "a");
            a.Populate();
            a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
            a.Engine.Backup(RevisionOrigin.Manual);

            var crashing = new Device(temp, "b", new CrashProbe(stage, index));
            crashing.Populate();
            crashing.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
            crashing.Engine.Backup(RevisionOrigin.Manual);
            var before = Snapshot(crashing);

            var plan = crashing.Engine.Plan(a.Settings.DeviceId, null);
            foreach (var path in new[] { Hotbar, Keybind, "MACROSYS.dat" })
            {
                Item(plan, path).Selected = true;
            }

            Assert.Throws<SimulatedCrashException>(() => crashing.Engine.Apply(plan));

            // The next start: the same device without the crash.
            var restarted = new Core.Engine.SyncEngine(crashing.Local, crashing.Settings, new RuleSet(new RuleFile()), crashing.Game, new FakeCloud(Platform.CloudFileState.InSync), Logging.Log.None);
            if (restarted.HasInterruptedApply)
            {
                restarted.ResolveInterruptedApply(rollBack);
            }

            var after = Snapshot(crashing);
            var expectedNew = new Dictionary<string, string>(before)
            {
                [Hotbar] = Hash(a, Hotbar),
                [Keybind] = Hash(a, Keybind),
                ["MACROSYS.dat"] = Hash(a, "MACROSYS.dat"),
            };
            Assert.True(
                Same(after, before) || Same(after, expectedNew),
                $"Stage {stage}/{index}, rollBack={rollBack}: the state is a mix.");
            if (restarted.HasInterruptedApply)
            {
                Assert.Fail("The journal must be gone after recovery.");
            }
        }
    }

    [Fact]
    public void ReceiptIsConfirmedOnlyForIntactRevisions()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        Assert.Equal(1, b.Engine.ConfirmReceived());
        var statusA = a.Engine.GetDevices().Single(d => d.IsThisDevice);
        Assert.Contains("B", statusA.ReceivedBy);

        a.WriteGame(Hotbar, Device.Random());
        var newHead = a.Engine.Backup(RevisionOrigin.Manual).RevisionId!;
        var store = a.Engine.OpenStore();
        var hotbar = store.ReadRevision(a.Settings.DeviceId, newHead).AllFiles().Single(f => f.File.Path == Hotbar).File;
        File.Delete(store.ObjectPath(a.Settings.DeviceId, hotbar.Hash));
        Assert.Equal(0, b.Engine.ConfirmReceived());
        Assert.Empty(a.Engine.GetDevices().Single(d => d.IsThisDevice).ReceivedBy);
    }

    [Fact]
    public void ADeviceSetUpAgainKeepsItsIdentity()
    {
        using var temp = new TempFolder();
        var (a, _) = TwoDevices(temp);
        var again = new Device(temp, "a2");
        again.Populate();
        again.Engine.JoinSyncFolder(temp.Sub("sync"), "A", a.Settings.DeviceId);
        Assert.Equal(a.Settings.DeviceId, again.Settings.DeviceId);
        Assert.Equal(2, again.Engine.GetDevices().Count);
        Assert.Throws<RefusedException>(() => again.Engine.JoinSyncFolder(temp.Sub("sync"), "X", SyncStore.NewDeviceId()));
    }

    [Fact]
    public void ThreeDevicesStayConsistent()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var c = new Device(temp, "c");
        c.Populate();
        c.Engine.JoinSyncFolder(temp.Sub("sync"), "C", null);
        c.Engine.Backup(RevisionOrigin.Manual);
        AlignShared(a, b);
        TakeShared(c, a);
        c.Engine.Backup(RevisionOrigin.Manual);

        var change = Device.Random();
        b.WriteGame(Keybind, change);
        b.Engine.Backup(RevisionOrigin.GameExit);
        foreach (var target in new[] { a, c })
        {
            var plan = target.Engine.Plan(b.Settings.DeviceId, null);
            Assert.Equal(PlanAction.Apply, Item(plan, Keybind).Action);
            target.Engine.Apply(plan);
            Assert.Equal(change, target.ReadGame(Keybind));
        }
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

    /// <summary>B takes A's shared files (as at onboarding), then both have a common state.</summary>
    private static void AlignShared(Device a, Device b, params string[] extra)
    {
        TakeShared(b, a, extra);
        b.Engine.Backup(RevisionOrigin.Manual);
    }

    private static void TakeShared(Device target, Device source, params string[] extra)
    {
        var plan = target.Engine.Plan(source.Settings.DeviceId, null);
        foreach (var path in new[] { Hotbar, Keybind, "MACROSYS.dat" }.Concat(extra))
        {
            Item(plan, path).Selected = true;
        }

        target.Engine.Apply(plan);
    }

    private static PlanItem Item(ApplyPlan plan, string path) => plan.Items.Single(i => i.Root == Roots.Game && i.Path == path);

    private static Dictionary<string, string> Snapshot(Device device) =>
        Directory.EnumerateFiles(device.GameFolder, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .ToDictionary(f => RelativePath.FromFullPath(device.GameFolder, f), FileOps.HashFile);

    private static string Hash(Device device, string relative) => FileOps.HashFile(device.GamePath(relative));

    private static bool Same(Dictionary<string, string> left, Dictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void WriteVersion(string install, string version)
    {
        Directory.CreateDirectory(Path.Combine(install, "game"));
        File.WriteAllText(Path.Combine(install, "game", "ffxivgame.ver"), version);
    }
}
