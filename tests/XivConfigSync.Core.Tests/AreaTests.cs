using System.Text;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>Areas instead of files (owner's request, 2026-11-06).</summary>
public sealed class AreaTests
{
    private const string Hotbar = Device.Chr + "/HOTBAR.DAT";
    private const string Keybind = Device.Chr + "/KEYBIND.DAT";
    private const string Addon = Device.Chr + "/ADDON.DAT";
    private const string Common = Device.Chr + "/COMMON.DAT";

    private static readonly RuleSet Defaults = new(new RuleFile());

    [Theory]
    [InlineData(Roots.Game, Hotbar, Areas.Hotbars)]
    [InlineData(Roots.Game, "MACROSYS.dat", Areas.Hotbars)]
    [InlineData(Roots.Game, Keybind, Areas.Controls)]
    [InlineData(Roots.Game, Device.Chr + "/GEARSET.DAT", Areas.Gearsets)]
    [InlineData(Roots.Game, Device.Chr + "/LOGFLTR.DAT", Areas.Chat)]
    [InlineData(Roots.Game, Addon, Areas.Hud)]
    [InlineData(Roots.Game, Device.Chr + "/ITEMODR.DAT", Areas.Inventory)]
    [InlineData(Roots.Plugins, "Some/Deep/file.json", Areas.Plugins)]
    public void EveryKnownFileBelongsToItsArea(string root, string path, string area) =>
        Assert.Equal([area], Areas.Of(root, path).Select(a => a.Key));

    [Theory]
    [InlineData(Roots.Game, "FFXIV.cfg")]
    [InlineData(Roots.Game, "FFXIV_BOOT.cfg")]
    [InlineData(Roots.Game, "cfgcopy/FFXIV_CFGCPY0000000003D8CE4F.dat")]
    [InlineData(Roots.Game, Device.Chr + "/UISAVE.DAT")]
    [InlineData(Roots.Dalamud, "dalamudConfig.json")]
    public void DeviceFilesBelongToNoArea(string root, string path) => Assert.Empty(Areas.Of(root, path));

    [Fact]
    public void TheCharacterSettingsFileBelongsToTwoAreas() =>
        Assert.Equal([Areas.Character, Areas.Chat], Areas.Of(Roots.Game, Common).Select(a => a.Key));

    [Fact]
    public void AnAreaSwitchedOffIsNeverTaken()
    {
        var rules = Defaults.WithArea(Areas.Hud, false);
        Assert.Equal(ApplyRule.Never, rules.RuleFor(Roots.Game, Addon));
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, Hotbar));
        Assert.False(rules.IsAreaOn(Areas.Hud));
        Assert.True(rules.WithArea(Areas.Hud, true).IsAreaOn(Areas.Hud));
    }

    [Fact]
    public void AnOverrideOnASingleFileStillWins()
    {
        var rules = Defaults.WithArea(Areas.Hotbars, false).WithOverride(Roots.Game, "MACROSYS.dat", ApplyRule.Always);
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, "MACROSYS.dat"));
        Assert.Equal(ApplyRule.Never, rules.RuleFor(Roots.Game, Hotbar));
    }

    [Fact]
    public void SwitchingAnAreaRemovesAnOverrideOnExactlyItsPattern()
    {
        var rules = Defaults.WithOverride(Roots.Game, "FFXIV_CHR*/ADDON.DAT", ApplyRule.OnRequest).WithArea(Areas.Hud, true);
        Assert.Empty(rules.File.Overrides);
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, Addon));
    }

    [Fact]
    public void TheDefaultOfAMatrixRowFollowsItsArea()
    {
        Assert.Equal(ApplyRule.Always, Defaults.DefaultFor(Roots.Game, "FFXIV_CHR*/ADDON.DAT"));
        Assert.Equal(ApplyRule.Never, Defaults.WithArea(Areas.Hud, false).DefaultFor(Roots.Game, "FFXIV_CHR*/ADDON.DAT"));
        Assert.Equal(ApplyRule.Never, Defaults.DefaultFor(Roots.Game, "FFXIV.cfg"));
    }

    [Fact]
    public void TheCharacterSettingsFileIsSplitOnlyWhenOneOfItsAreasIsOff()
    {
        Assert.Null(Defaults.KeepLocalKeys(Roots.Game, Common));
        var keepChat = Defaults.WithArea(Areas.Chat, false).KeepLocalKeys(Roots.Game, Common);
        Assert.NotNull(keepChat);
        Assert.True(keepChat("LogFontSize"));
        Assert.False(keepChat("WeaponAutoPutAway"));
        var keepRest = Defaults.WithArea(Areas.Character, false).KeepLocalKeys(Roots.Game, Common);
        Assert.NotNull(keepRest);
        Assert.False(keepRest("LogFontSize"));
        Assert.True(keepRest("WeaponAutoPutAway"));

        var neither = Defaults.WithArea(Areas.Chat, false).WithArea(Areas.Character, false);
        Assert.Null(neither.KeepLocalKeys(Roots.Game, Common));
        Assert.Equal(ApplyRule.Never, neither.RuleFor(Roots.Game, Common));
    }

    [Theory]
    [InlineData("LogFontSize", true)]
    [InlineData("LogTabFilter2", true)]
    [InlineData("IsLogTell", true)]
    [InlineData("ChatType", true)]
    [InlineData("PlayGuideLoginDisp", false)]
    [InlineData("AchievementAppealLoginDisp", false)]
    [InlineData("BattleEffectSelf", false)]
    public void ChatSettingsAreFoundByTheirName(string key, bool chat) => Assert.Equal(chat, Areas.IsChatKey(key));

    [Fact]
    public void MergingKeepsTheChosenValuesAndEverythingElseOfTheSource()
    {
        var source = CommonFile(font: "12", effect: "1");
        var local = CommonFile(font: "18", effect: "0");
        var merged = CommonSettings.Merge(source, local, Areas.IsChatKey);
        Assert.Equal(CommonFile(font: "18", effect: "1"), merged);

        // Keeping nothing gives the source back byte for byte.
        Assert.Equal(source, CommonSettings.Merge(source, local, _ => false));
    }

    [Fact]
    public void TheComparablePartLeavesOutTheKeptSettings()
    {
        var one = CommonSettings.Comparable(CommonFile(font: "12", effect: "1"), Areas.IsChatKey);
        var two = CommonSettings.Comparable(CommonFile(font: "18", effect: "1"), Areas.IsChatKey);
        var three = CommonSettings.Comparable(CommonFile(font: "12", effect: "0"), Areas.IsChatKey);
        Assert.Equal(one, two);
        Assert.NotEqual(one, three);
    }

    [Theory]
    [InlineData("Key\tValue\tExtra\r\n")]
    [InlineData("no tab here\r\n")]
    [InlineData("Bad Key\t1\r\n")]
    public void AFileOfAnotherShapeIsNeverMerged(string line)
    {
        var odd = Encoding.Latin1.GetBytes("<UI Settings>\r\n" + line);
        Assert.Null(CommonSettings.Merge(odd, CommonFile("12", "1"), Areas.IsChatKey));
        Assert.Null(CommonSettings.Comparable(odd, Areas.IsChatKey));
    }

    [Fact]
    public void WithTheChatOffTheChatKeepsThisDevicesValuesAndTheRestIsTaken()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        a.WriteGame(Common, CommonFile(font: "12", effect: "1"));
        b.WriteGame(Common, CommonFile(font: "18", effect: "1"));
        a.Engine.Backup(RevisionOrigin.Manual);
        b.Engine.UpdateRules(b.Engine.Rules.WithArea(Areas.Chat, false));

        // Only the chat differs: nothing to take.
        Assert.Equal(PlanAction.Skip, Item(b.Engine.Plan(a.Settings.DeviceId, null), Common).Action);

        // A changes a character setting: B takes it and keeps its own font size.
        a.WriteGame(Common, CommonFile(font: "14", effect: "0"));
        a.Engine.Backup(RevisionOrigin.GameExit);
        var first = Item(b.Engine.Plan(a.Settings.DeviceId, null), Common);
        Assert.Equal(PlanAction.Conflict, first.Action);
        Assert.Equal(PlanReason.NoCommonState, first.Reason);
        Assert.True(b.Engine.ResolveFirstContact(a.Settings.DeviceId, takeSource: true) > 0);
        Assert.Equal(CommonFile(font: "18", effect: "0"), b.ReadGame(Common));

        // From now on a change on A flows by itself, still without the chat.
        a.WriteGame(Common, CommonFile(font: "10", effect: "1"));
        a.Engine.Backup(RevisionOrigin.GameExit);
        var next = Item(b.Engine.Plan(a.Settings.DeviceId, null), Common);
        Assert.Equal(PlanAction.Apply, next.Action);
        b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.Equal(CommonFile(font: "18", effect: "1"), b.ReadGame(Common));
        Assert.Equal(PlanAction.Skip, Item(b.Engine.Plan(a.Settings.DeviceId, null), Common).Action);

        // B changes its own chat font: still nothing to take.
        b.WriteGame(Common, CommonFile(font: "20", effect: "1"));
        Assert.Equal(PlanAction.Skip, Item(b.Engine.Plan(a.Settings.DeviceId, null), Common).Action);
    }

    [Fact]
    public void ForTheHudLayoutTheStateBackedUpLastWins()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        b.Engine.ResolveFirstContact(a.Settings.DeviceId, takeSource: true);
        b.Engine.Backup(RevisionOrigin.Manual);
        a.Engine.ResolveFirstContact(b.Settings.DeviceId, takeSource: true);

        a.WriteGame(Addon, Device.Random());
        a.Engine.Backup(RevisionOrigin.GameExit);
        Thread.Sleep(20);
        var later = Device.Random();
        b.WriteGame(Addon, later);
        b.Engine.Backup(RevisionOrigin.GameExit);

        var onB = Item(b.Engine.Plan(a.Settings.DeviceId, null), Addon);
        Assert.Equal(PlanAction.Offer, onB.Action);
        Assert.Equal(PlanReason.LocalNewerState, onB.Reason);

        var onA = Item(a.Engine.Plan(b.Settings.DeviceId, null), Addon);
        Assert.Equal(PlanAction.Apply, onA.Action);
        Assert.Equal(PlanReason.SourceNewerState, onA.Reason);
        a.Engine.AutoApply(b.Settings.DeviceId);
        Assert.Equal(later, a.ReadGame(Addon));
    }

    [Fact]
    public void TheFirstTransferIsAskedOnceInsteadOfAConflictPerFile()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var mine = b.ReadGame(Hotbar);

        var result = b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.True(result.FirstContact > 0);
        Assert.Equal(0, result.Waiting);
        Assert.Equal(0, result.Applied);
        Assert.Equal(mine, b.ReadGame(Hotbar));

        // Keeping this device's files: no question and no conflict any more.
        Assert.Equal(0, b.Engine.ResolveFirstContact(a.Settings.DeviceId, takeSource: false));
        Assert.Equal(mine, b.ReadGame(Hotbar));
        var again = b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.Equal(0, again.FirstContact);
        Assert.Equal(0, again.Waiting);
        Assert.Equal(PlanReason.LocalChangedOnly, Item(b.Engine.Plan(a.Settings.DeviceId, null), Hotbar).Reason);
    }

    [Fact]
    public void TakingTheFirstTransferTakesEveryAreaButNotDeviceFiles()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        var ownCfg = b.ReadGame("FFXIV.cfg");
        b.Engine.UpdateRules(b.Engine.Rules.WithArea(Areas.Hud, false));
        var ownAddon = b.ReadGame(Addon);

        Assert.True(b.Engine.ResolveFirstContact(a.Settings.DeviceId, takeSource: true) > 0);
        Assert.Equal(a.ReadGame(Hotbar), b.ReadGame(Hotbar));
        Assert.Equal(a.ReadGame(Keybind), b.ReadGame(Keybind));
        Assert.Equal(ownCfg, b.ReadGame("FFXIV.cfg"));
        Assert.Equal(ownAddon, b.ReadGame(Addon));
    }

    [Fact]
    public void AppliedFilesAreNamedByTheirAreas() =>
        Assert.Equal(
            [Areas.Hotbars, Areas.Hud],
            SyncEngine.AreaKeysOf([Plan(Addon), Plan(Hotbar), Plan("MACROSYS.dat"), Plan("FFXIV.cfg")]));

    private static PlanItem Plan(string path) => new()
    {
        Root = Roots.Game,
        Path = path,
        SourceHash = "x",
        SourceSize = 1,
        Rule = ApplyRule.Always,
        Matrix = FileMatrix.Lookup(Roots.Game, path),
        Action = PlanAction.Apply,
        Reason = PlanReason.SourceNewer,
    };

    private static byte[] CommonFile(string font, string effect) => Encoding.Latin1.GetBytes(
        "BattleEffectSelf\t" + effect + "\r\nBattleEffectParty\t0\r\n\r\n<UI Settings>\r\n\r\n<Charcter Settings>\r\n"
        + "LogFontSize\t" + font + "\r\nIsLogTell\t1\r\nWeaponAutoPutAway\t1\r\nTelepoTicketGilSetting\t1\r\n\0\r\n\r\n");

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

    private static PlanItem Item(ApplyPlan plan, string path) => plan.Items.Single(i => i.Root == Roots.Game && i.Path == path);
}
