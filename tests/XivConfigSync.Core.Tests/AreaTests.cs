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
    public void TheCharacterSettingsFileBelongsToThreeAreas() =>
        Assert.Equal([Areas.Hotbars, Areas.Character, Areas.Chat], Areas.Of(Roots.Game, Common).Select(a => a.Key));

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

        var neither = Defaults.WithArea(Areas.Chat, false).WithArea(Areas.Character, false).WithArea(Areas.Hotbars, false);
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
    [InlineData("ColorTell", true)]
    [InlineData("LogChatBubbleSayFontColor", true)]
    [InlineData("LogNamePlateDispEnemyCast", false)]
    [InlineData("HotbarLock", false)]
    public void ChatSettingsAreFoundByTheirName(string key, bool chat) => Assert.Equal(chat, IsChat(key));

    [Fact]
    public void MergingKeepsTheChosenValuesAndEverythingElseOfTheSource()
    {
        var source = CommonFile(font: "12", effect: "1");
        var local = CommonFile(font: "18", effect: "0");
        var merged = CommonSettings.Merge(source, local, IsChat);
        Assert.Equal(CommonFile(font: "18", effect: "1"), merged);

        // Keeping nothing gives the source back byte for byte.
        Assert.Equal(source, CommonSettings.Merge(source, local, _ => false));
    }

    [Fact]
    public void ASettingThatOccursTwiceKeepsBothValuesInOrder()
    {
        var file = Encoding.Latin1.GetBytes("<Game Camera Settings>\r\nLockonDefaultZoom\t1.5\r\nLockonDefaultZoom\t3.0\r\n");
        var other = Encoding.Latin1.GetBytes("<Game Camera Settings>\r\nLockonDefaultZoom\t2.0\r\nLockonDefaultZoom\t4.0\r\n");
        Assert.Equal(file, CommonSettings.Merge(file, file, _ => true));
        Assert.Equal(other, CommonSettings.Merge(file, other, _ => true));
    }

    [Fact]
    public void TheComparablePartLeavesOutTheKeptSettings()
    {
        var one = CommonSettings.Comparable(CommonFile(font: "12", effect: "1"), IsChat);
        var two = CommonSettings.Comparable(CommonFile(font: "18", effect: "1"), IsChat);
        var three = CommonSettings.Comparable(CommonFile(font: "12", effect: "0"), IsChat);
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
        Assert.Null(CommonSettings.Merge(odd, CommonFile("12", "1"), IsChat));
        Assert.Null(CommonSettings.Comparable(odd, IsChat));
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

    [Theory]
    [InlineData("LogFontSize", "chat.font")]
    [InlineData("LogPermeationRateLog3", "chat.font")]
    [InlineData("LogTimeDispLog2", "chat.time")]
    [InlineData("LogChatBubbleTellFontColor", "chat.bubbles")]
    [InlineData("ColorSysErr", "chat.colors")]
    [InlineData("LogColorRoleTank", "chat.colors")]
    [InlineData("LogTabFilter1", "chat.tabs")]
    [InlineData("IsLogCwls3", "chat.tabs")]
    [InlineData("ChatType", "chat.tabs")]
    [InlineData("LogNamePlateDispEnemyCast", "character.nameplates")]
    [InlineData("NamePlateColorTank", "character.nameplates")]
    [InlineData("HotbarCrossLock", "hotbars.display")]
    [InlineData("ThirdPersonDefaultZoom", "character.camera")]
    [InlineData("GPoseInitCameraFocus", "character.camera")]
    [InlineData("BattleEffectParty", "character.battle")]
    [InlineData("SolBahamutSize", "character.pets")]
    [InlineData("MapDispSize", "character.sizes")]
    [InlineData("TelepoTicketGilSetting", "character.other")]
    [InlineData("SomeSettingOfALaterGameVersion", "character.other")]
    public void EverySettingOfTheCharacterFileHasItsPart(string setting, string part) =>
        Assert.Equal(part, SettingParts.PartOf(Common, setting).Key);

    [Theory]
    [InlineData("MouseTpsYReverse", "controls.camera")]
    [InlineData("FlyingControlType", "controls.camera")]
    [InlineData("CircleSheathedEnemy", "controls.target")]
    [InlineData("AutoLockOn", "controls.target")]
    [InlineData("NaviMapDisp", "controls.display")]
    [InlineData("HotbarDisp", "controls.display")]
    public void BothControlFilesHaveTheSameParts(string setting, string part)
    {
        Assert.Equal(part, SettingParts.PartOf(Device.Chr + "/CONTROL0.DAT", setting).Key);
        Assert.Equal(part, SettingParts.PartOf(Device.Chr + "/CONTROL1.DAT", setting).Key);
    }

    [Fact]
    public void APartSwitchedOffKeepsOnlyItsOwnSettings()
    {
        var rules = Defaults.WithPart("chat.font", false);
        var keep = rules.KeepLocalKeys(Roots.Game, Common);
        Assert.NotNull(keep);
        Assert.True(keep("LogFontSize"));
        Assert.True(keep("LogPermeationRate"));
        Assert.False(keep("LogTabFilter0"));
        Assert.False(keep("ColorTell"));
        Assert.False(keep("NamePlateColorTank"));
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, Common));

        // The part's own switch stays when its area is turned off and on again.
        var again = rules.WithArea(Areas.Chat, false).WithArea(Areas.Chat, true);
        Assert.False(again.IsPartSwitchOn("chat.font"));
        Assert.True(again.KeepLocalKeys(Roots.Game, Common)!("LogFontSize"));
    }

    [Fact]
    public void TheControlFilesAreSplitByPartToo()
    {
        var control = Device.Chr + "/CONTROL1.DAT";
        Assert.Null(Defaults.KeepLocalKeys(Roots.Game, control));
        var keep = Defaults.WithPart("controls.display", false).KeepLocalKeys(Roots.Game, control);
        Assert.NotNull(keep);
        Assert.True(keep("NaviMapDisp"));
        Assert.False(keep("AutoLockOn"));
        Assert.Null(Defaults.KeepLocalKeys(Roots.Game, Hotbar));
    }

    [Fact]
    public void AControlFileOfTheRealShapeIsMergedByPart()
    {
        var source = Encoding.Latin1.GetBytes("\r\n<Charcter Settings>\r\nMouseTpsYReverse\t1\r\n\r\n<Target Settings>\r\nAutoLockOn\t1\r\nNaviMapDisp\t1\r\n\0\r\n");
        var local = Encoding.Latin1.GetBytes("\r\n<Charcter Settings>\r\nMouseTpsYReverse\t0\r\n\r\n<Target Settings>\r\nAutoLockOn\t0\r\nNaviMapDisp\t0\r\n\0\r\n");
        var keep = Defaults.WithPart("controls.display", false).KeepLocalKeys(Roots.Game, Device.Chr + "/CONTROL0.DAT")!;
        Assert.Equal(
            Encoding.Latin1.GetBytes("\r\n<Charcter Settings>\r\nMouseTpsYReverse\t1\r\n\r\n<Target Settings>\r\nAutoLockOn\t1\r\nNaviMapDisp\t0\r\n\0\r\n"),
            CommonSettings.Merge(source, local, keep));
    }

    [Fact]
    public void EveryAreaPartIsShownOnce() =>
        Assert.Equal(["controls.camera", "controls.target", "controls.display"], SettingParts.OfArea(Areas.Controls).Select(p => p.Key));

    [Fact]
    public void ASingleSettingCanStayHereWhileItsPartIsTaken()
    {
        var rules = Defaults.WithSetting(Common, "LogFontSize", false);
        var keep = rules.KeepLocalKeys(Roots.Game, Common);
        Assert.NotNull(keep);
        Assert.True(keep("LogFontSize"));
        Assert.False(keep("LogPermeationRate"));
        Assert.False(keep("WeaponAutoPutAway"));
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, Common));
        Assert.False(rules.SettingChoice(Device.Chr + "/common.dat", "LogFontSize"));

        // The choice is per file: the control files are not touched by it.
        Assert.Null(rules.KeepLocalKeys(Roots.Game, Device.Chr + "/CONTROL0.DAT"));
    }

    [Fact]
    public void ASingleSettingCanBeTakenWhileItsPartStaysHere()
    {
        var rules = Defaults.WithPart("chat.font", false).WithSetting(Common, "LogFontSize", true);
        var keep = rules.KeepLocalKeys(Roots.Game, Common)!;
        Assert.False(keep("LogFontSize"));
        Assert.True(keep("LogPermeationRate"));

        // Back to following its part.
        var back = rules.WithSetting(Common, "LogFontSize", null);
        Assert.Null(back.SettingChoice(Common, "LogFontSize"));
        Assert.True(back.KeepLocalKeys(Roots.Game, Common)!("LogFontSize"));
    }

    [Fact]
    public void ChoicesThatOnlyTakeChangeNothingWhenEveryPartIsOn() =>
        Assert.Null(Defaults.WithSetting(Common, "LogFontSize", true).KeepLocalKeys(Roots.Game, Common));

    [Fact]
    public void AnAreaWithEveryPartOffKeepsTheWholeFileHere()
    {
        var control = Device.Chr + "/CONTROL0.DAT";
        var rules = Defaults.WithPart("controls.camera", false).WithPart("controls.target", false).WithPart("controls.display", false);
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, control));
        var keep = rules.KeepLocalKeys(Roots.Game, control)!;
        Assert.True(keep("MouseTpsYReverse"));
        Assert.True(keep("AutoLockOn"));
        Assert.True(keep("NaviMapDisp"));

        // Unless one setting of it is chosen.
        Assert.False(rules.WithSetting(control, "AutoLockOn", true).KeepLocalKeys(Roots.Game, control)!("AutoLockOn"));
    }

    [Fact]
    public void SettingChoicesSurviveSavingAndOtherRuleChanges()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Sub("rules"), "rules.json");
        Defaults.WithSetting(Common, "LogFontSize", false).WithArea(Areas.Hud, false).WithPart("chat.tabs", false)
            .WithOverride(Roots.Game, "FFXIV_CHR*/ADDON.DAT", ApplyRule.OnRequest).WithExclusion(Roots.Game, "x", true)
            .Save(path);
        Assert.False(RuleSet.Load(path).SettingChoice(Common, "LogFontSize"));
    }

    [Fact]
    public void ASettingKeptHereStaysThroughTheAutomaticTransfer()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp);
        a.WriteGame(Common, CommonFile(font: "12", effect: "1"));
        b.WriteGame(Common, CommonFile(font: "18", effect: "1"));
        a.Engine.Backup(RevisionOrigin.Manual);
        b.Engine.UpdateRules(b.Engine.Rules.WithSetting(Common, "LogFontSize", false));
        Assert.Equal(PlanAction.Skip, Item(b.Engine.Plan(a.Settings.DeviceId, null), Common).Action);
        Assert.True(Assert.Single(b.Engine.CompareSettings(b.Engine.Plan(a.Settings.DeviceId, null), Item(b.Engine.Plan(a.Settings.DeviceId, null), Common)).Differences).StaysHere);

        a.WriteGame(Common, CommonFile(font: "14", effect: "0"));
        a.Engine.Backup(RevisionOrigin.GameExit);
        Assert.True(b.Engine.ResolveFirstContact(a.Settings.DeviceId, takeSource: true) > 0);
        Assert.Equal(CommonFile(font: "18", effect: "0"), b.ReadGame(Common));

        a.WriteGame(Common, CommonFile(font: "10", effect: "1"));
        a.Engine.Backup(RevisionOrigin.GameExit);
        b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.Equal(CommonFile(font: "18", effect: "1"), b.ReadGame(Common));
    }

    private static bool IsChat(string setting) => SettingParts.PartOf(Common, setting).AreaKey == Areas.Chat;

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
