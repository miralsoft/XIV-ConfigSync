using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Inventory;
using XivConfigSync.Core.Logging;
using XivConfigSync.Core.ViewModels;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>
/// Removing conflict copies from the game folder (owner's request, 2026-11-05): only the files the
/// user ticks, only after confirming, only to the Recycle Bin, never a file that is not a conflict
/// copy, never one whose original is gone, never while the game runs.
/// </summary>
public sealed class CleanupTests
{
    private const string Cfg = "FFXIV.cfg";
    private const string CfgCopy = "FFXIV-PC2.cfg";
    private const string Hotbar = Device.Chr + "/HOTBAR.DAT";
    private const string HotbarCopy = Device.Chr + "/HOTBAR-PC2-1.DAT";
    private const string Orphan = Device.Chr + "/MACRO-PC2.DAT";

    [Theory]
    [InlineData("FFXIV-PC2.cfg", "FFXIV.cfg")]
    [InlineData("FFXIV-PC2-1.cfg", "FFXIV.cfg")]
    [InlineData("FFXIV_CHR01/HOTBAR-NB-MICHI.DAT", "FFXIV_CHR01/HOTBAR.DAT")]
    public void TheOriginalIsTheNameBeforeTheFirstDash(string copy, string original) =>
        Assert.Equal(original, ConflictCopies.OriginalOf(copy));

    [Fact]
    public void FindsConflictCopiesAndWhetherTheirOriginalIsThere()
    {
        using var temp = new TempFolder();
        var device = Prepare(temp);

        var found = device.Engine.ListConflictCopies();

        Assert.Equal([CfgCopy, HotbarCopy, Orphan], found.Select(c => c.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(found.Single(c => c.Path == CfgCopy).Removable);
        Assert.Equal(Hotbar, found.Single(c => c.Path == HotbarCopy).Original);
        Assert.False(found.Single(c => c.Path == Orphan).Removable);
    }

    [Fact]
    public void MovesOnlyTheChosenCopiesAndNeverAnOriginal()
    {
        using var temp = new TempFolder();
        var device = Prepare(temp);
        var bin = new FakeRecycleBin(temp.Sub("bin"));
        var all = device.Engine.ListConflictCopies();
        var original = all.Single(c => c.Path == CfgCopy) with { Path = Cfg, FullPath = device.GamePath(Cfg) };

        var (moved, left) = device.Engine.RemoveConflictCopies([all.Single(c => c.Path == CfgCopy), original, all.Single(c => c.Path == Orphan)], bin);

        Assert.Equal([CfgCopy], moved);
        Assert.False(File.Exists(device.GamePath(CfgCopy)));
        // Not chosen, not a conflict copy, or without its original: all still there.
        Assert.True(File.Exists(device.GamePath(HotbarCopy)));
        Assert.True(File.Exists(device.GamePath(Cfg)));
        Assert.True(File.Exists(device.GamePath(Orphan)));
        Assert.Equal([Cfg, Orphan], left.Select(l => l.Path));
        Assert.Single(bin.Recycled);
    }

    [Fact]
    public void LeavesACopyThatChangedSinceItWasListed()
    {
        using var temp = new TempFolder();
        var device = Prepare(temp);
        var bin = new FakeRecycleBin(temp.Sub("bin"));
        var listed = device.Engine.ListConflictCopies().Single(c => c.Path == CfgCopy);
        device.WriteGame(CfgCopy, Device.Random(900));

        var (moved, left) = device.Engine.RemoveConflictCopies([listed], bin);

        Assert.Empty(moved);
        Assert.Equal(Strings.Cleanup_Changed, left.Single().Reason);
        Assert.True(File.Exists(device.GamePath(CfgCopy)));
    }

    [Fact]
    public void IsRefusedWhileTheGameRuns()
    {
        using var temp = new TempFolder();
        var device = Prepare(temp);
        var bin = new FakeRecycleBin(temp.Sub("bin"));
        var all = device.Engine.ListConflictCopies();
        device.Game.IsRunning = true;

        var refused = Assert.Throws<RefusedException>(() => device.Engine.RemoveConflictCopies(all, bin));
        Assert.Equal("Refuse_GameRunning", refused.ReasonKey);
        Assert.Empty(bin.Recycled);
    }

    [Fact]
    public async Task ThePageTicksNothingByItselfAndMovesOnlyTheTickedAfterConfirmation()
    {
        using var temp = new TempFolder();
        var device = Prepare(temp);
        var bin = new FakeRecycleBin(temp.Sub("bin"));
        var ui = new QuietUi { ConfirmAnswer = false };
        using var main = new MainViewModel(device.Engine, ui, Log.None, device.Game, new NoShell(), new NoLocator(), bin);
        var page = main.Cleanup;

        await page.LoadAsync();
        Assert.Equal(3, page.Rows.Count);
        Assert.DoesNotContain(page.Rows, r => r.IsSelected);

        // A copy without its original cannot be ticked, not even by "tick all".
        page.SelectAll();
        Assert.False(page.Rows.Single(r => r.DisplayPath == Orphan).IsSelected);
        page.Rows.Single(r => r.DisplayPath == HotbarCopy).IsSelected = false;

        // Declined: nothing moves.
        await page.RemoveSelectedAsync();
        Assert.Equal(1, ui.Confirmations);
        Assert.Empty(bin.Recycled);

        ui.ConfirmAnswer = true;
        await page.RemoveSelectedAsync();
        Assert.Equal([device.GamePath(CfgCopy)], bin.Recycled);
        Assert.True(File.Exists(device.GamePath(HotbarCopy)));
        Assert.Equal(2, page.Rows.Count);
    }

    private static Device Prepare(TempFolder temp)
    {
        var device = new Device(temp, "a");
        device.WriteGame(Cfg, Device.Random());
        device.WriteGame(CfgCopy, Device.Random());
        device.WriteGame(Hotbar, Device.Random());
        device.WriteGame(HotbarCopy, Device.Random());
        device.WriteGame(Orphan, Device.Random());
        return device;
    }
}
