using System.Text;
using XivConfigSync.Core.Discovery;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>ACT and its plugins in the backup (owner's request, 2026-11-06), on generated folders only.</summary>
public sealed class ActTests
{
    private const string Overlay = ActPlugins.ConfigFolder + "/RainbowMage.OverlayPlugin.config.json";
    private const string Parser = ActPlugins.ConfigFolder + "/FFXIV_ACT_Plugin.config.xml";
    private const string CactbotUser = ActPlugins.ActFolder + "/Plugins/cactbot/cactbot/user";
    private static readonly Dictionary<string, List<string>> NoCustom = [];

    [Fact]
    public void ThePluginsAndTheirSettingsAreFoundFromActsOwnList()
    {
        using var temp = new TempFolder();
        var roaming = temp.Sub("roaming");
        MakeAct(roaming);

        var plugins = ActPlugins.Discover(roaming, NoCustom).ToDictionary(p => p.Key);

        Assert.Equal([ActPlugins.ActKey, "FFXIV_ACT_Plugin", "OverlayPlugin", "CactbotOverlay", "ACT.SpecialSpellTimer"], plugins.Keys);
        Assert.Equal([ActPlugins.MainConfig, ActPlugins.ConfigFolder + "/Unknown.config.xml"], plugins[ActPlugins.ActKey].Proposal);
        Assert.Equal([Parser], plugins["FFXIV_ACT_Plugin"].Proposal);
        Assert.Equal([Overlay], plugins["OverlayPlugin"].Proposal);

        // cactbot's user folder is named in OverlayPlugin's settings and lies in cactbot's folder.
        Assert.Equal([CactbotUser + "/**"], plugins["CactbotOverlay"].Proposal);

        // Hojoring keeps its settings where no path points to: the table of known places.
        var timers = plugins["ACT.SpecialSpellTimer"];
        Assert.False(timers.Enabled);
        Assert.Equal(["anoyetta/ACT/*.xml", "anoyetta/ACT/*.config"], timers.Proposal);

        // A folder outside the roaming folder is shown, never backed up; a log folder is not followed.
        Assert.Contains(@"D:\Elsewhere\Overlays", plugins["OverlayPlugin"].Outside);
        Assert.DoesNotContain(plugins.Values.SelectMany(p => p.Proposal.Concat(p.Outside)), p => p.Contains("FFXIVLogs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithoutActNothingIsFound()
    {
        using var temp = new TempFolder();
        Assert.Empty(ActPlugins.Discover(temp.Sub("roaming"), NoCustom));
        Assert.Empty(ActPlugins.Discover(null, NoCustom));
    }

    [Fact]
    public void TheUsersOwnPathsReplaceTheProposalUntilReset()
    {
        using var temp = new TempFolder();
        var roaming = temp.Sub("roaming");
        MakeAct(roaming);
        var rules = new RuleSet(new RuleFile()).WithActPaths("CactbotOverlay", [CactbotUser + "/raidboss.js"]);
        var cactbot = ActPlugins.Discover(roaming, rules.File.ActPaths).Single(p => p.Key == "CactbotOverlay");
        Assert.True(cactbot.IsCustom);
        Assert.Equal([CactbotUser + "/raidboss.js"], cactbot.Paths);
        Assert.Equal([CactbotUser + "/**"], cactbot.Proposal);

        var path = Path.Combine(temp.Sub("rules"), "rules.json");
        rules.WithArea(Areas.Chat, false).Save(path);
        Assert.Equal([CactbotUser + "/raidboss.js"], RuleSet.Load(path).File.ActPaths["CactbotOverlay"]);
        Assert.False(ActPlugins.Discover(roaming, rules.WithActPaths("CactbotOverlay", null).File.ActPaths).Single(p => p.Key == "CactbotOverlay").IsCustom);
    }

    [Theory]
    [InlineData(@"anoyetta\ACT\*.xml", "anoyetta/ACT/*.xml")]
    [InlineData("anoyetta/ACT/**", "anoyetta/ACT/**")]
    [InlineData(@"""{roaming}\Advanced Combat Tracker\Config\x.json""", "Advanced Combat Tracker/Config/x.json")]
    [InlineData(@"D:\Elsewhere\x.json", null)]
    [InlineData("../Local/x.json", null)]
    [InlineData("  ", null)]
    public void ATypedPathMustLieBelowTheRoamingFolder(string line, string? expected)
    {
        var roaming = @"C:\Users\Someone\AppData\Roaming";
        Assert.Equal(expected, ActPlugins.ParsePath(roaming, line.Replace("{roaming}", roaming, StringComparison.Ordinal)));
    }

    [Fact]
    public void OnlySettingsAreBackedUpNeverLogsCachesOrPrograms()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        MakeAct(a.RoamingFolder);

        // Even a path the user widened to all of ACT's folder leaves these out.
        a.Engine.UpdateRules(a.Engine.Rules.WithActPaths(ActPlugins.ActKey, [ActPlugins.ActFolder + "/**"]));
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);

        var files = a.Engine.ScanOnly().Files.Where(f => f.Root == Roots.Act).Select(f => f.Path).ToList();
        Assert.Contains(ActPlugins.MainConfig, files);
        Assert.Contains(Overlay, files);
        Assert.Contains(CactbotUser + "/raidboss.js", files);
        Assert.Contains("anoyetta/ACT/ACT.SpecialSpellTimer.Spells.xml", files);
        Assert.DoesNotContain(files, f => f.Contains("FFXIVLogs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.Contains("Cache", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".backup", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.StartsWith("anoyetta/ACT/logs", StringComparison.OrdinalIgnoreCase));

        // Nothing else of the roaming folder is read.
        Assert.DoesNotContain(files, f => f.StartsWith("SomeOtherApp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PluginSettingsFlowToTheOtherDeviceButActsOwnFileNever()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevicesWithAct(temp);

        Write(a.RoamingFolder, Overlay, """{ "Overlays": [ { "Name": "DPS", "X": 900 } ] }""");
        Write(a.RoamingFolder, ActPlugins.MainConfig, MainConfig(a.RoamingFolder, extra: true));
        a.Engine.Backup(RevisionOrigin.GameExit);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Equal(PlanAction.Apply, plan.Items.Single(i => i.Root == Roots.Act && i.Path == Overlay).Action);
        Assert.Equal(ApplyRule.Never, plan.Items.Single(i => i.Root == Roots.Act && i.Path == ActPlugins.MainConfig).Rule);

        var result = b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.Contains(Areas.Act, result.AreaKeys!);
        Assert.Equal(Read(a.RoamingFolder, Overlay), Read(b.RoamingFolder, Overlay));
        Assert.Equal(MainConfig(b.RoamingFolder, extra: false), Encoding.UTF8.GetString(Read(b.RoamingFolder, ActPlugins.MainConfig)));

        // The plugin A has and B lacks is named after a comparison.
        Assert.Equal(["Triggernometry"], b.Engine.MissingActPlugins(a.Settings.DeviceId));
    }

    [Fact]
    public void WhileActRunsItsFilesWaitAndAreTakenAfterwards()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevicesWithAct(temp);
        Write(a.RoamingFolder, Overlay, """{ "Overlays": [] }""");
        a.Engine.Backup(RevisionOrigin.GameExit);
        var before = Read(b.RoamingFolder, Overlay);

        b.ActRunning = true;
        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.Contains("Warn_ActRunning", plan.Warnings);
        foreach (var item in plan.Items)
        {
            item.Selected = item.Root == Roots.Act && item.Action == PlanAction.Apply;
        }

        Assert.Equal("Refuse_ActRunning", Assert.Throws<RefusedException>(() => b.Engine.Apply(plan)).ReasonKey);
        var waiting = b.Engine.AutoApply(a.Settings.DeviceId);
        Assert.Equal(1, waiting.ActWaiting);
        Assert.Equal(before, Read(b.RoamingFolder, Overlay));

        b.ActRunning = false;
        Assert.Equal(0, b.Engine.AutoApply(a.Settings.DeviceId).ActWaiting);
        Assert.Equal(Read(a.RoamingFolder, Overlay), Read(b.RoamingFolder, Overlay));
    }

    [Fact]
    public void ADeviceWithoutActIsToldAndNothingOfActIsOffered()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        MakeAct(a.RoamingFolder);
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = new Device(temp, "b");
        b.Populate();
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        Assert.DoesNotContain(plan.Items, i => i.Root == Roots.Act);
        Assert.Contains("Warn_ActOff", plan.Warnings);
        Assert.DoesNotContain("Warn_PluginsOff", plan.Warnings);
        Assert.False(Directory.Exists(Path.Combine(b.RoamingFolder, ActPlugins.ActFolder)));
    }

    [Fact]
    public void ActsBackupCanBeSwitchedOff()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        MakeAct(a.RoamingFolder);
        a.Settings.BackUpAct = false;
        Assert.True(a.Engine.HasAct);
        Assert.Null(a.Engine.Roots[Roots.Act]);
        Assert.DoesNotContain(a.Engine.ScanOnly().Files, f => f.Root == Roots.Act);
    }

    private static (Device A, Device B) TwoDevicesWithAct(TempFolder temp)
    {
        var a = new Device(temp, "a");
        a.Populate();
        MakeAct(a.RoamingFolder);
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = new Device(temp, "b");
        b.Populate();
        MakeAct(b.RoamingFolder);
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);

        // The first transfer takes A's settings, so both share a state from then on.
        b.Engine.AutoApply(a.Settings.DeviceId);
        b.Engine.ResolveFirstContact(a.Settings.DeviceId, takeSource: true);
        return (a, b);
    }

    /// <summary>A generated ACT below a roaming folder, shaped like a real one (names only, no real data).</summary>
    private static void MakeAct(string roaming)
    {
        Write(roaming, ActPlugins.MainConfig, MainConfig(roaming, extra: false));
        Write(roaming, ActPlugins.ConfigFolder + "/Backup/Advanced Combat Tracker.config.20261101.xml", "<old />");
        Write(roaming, Parser, $"""<Config><SettingsSerializer><TextBox Name="txtLogFileDirectory" Value="{roaming}\Advanced Combat Tracker\FFXIVLogs" /></SettingsSerializer></Config>""");
        var user = Path.Combine(roaming, "Advanced Combat Tracker", "Plugins", "cactbot", "cactbot", "user").Replace(@"\", @"\\", StringComparison.Ordinal);
        Write(roaming, Overlay, "{ \"CactbotUserDirectory\": \"" + user + "\\\\\", \"Other\": \"D:\\\\Elsewhere\\\\Overlays\" }");
        Write(roaming, Overlay + ".backup", "{ }");
        Write(roaming, ActPlugins.ConfigFolder + "/Unknown.config.xml", "<x />");
        Write(roaming, ActPlugins.ActFolder + "/Plugins/FFXIV_ACT_Plugin.dll", "MZ");
        Write(roaming, ActPlugins.ActFolder + "/Plugins/OverlayPlugin/OverlayPlugin.dll", "MZ");
        Write(roaming, ActPlugins.ActFolder + "/Plugins/OverlayPlugin/OverlayPlugin.dll.config", "<configuration />");
        Write(roaming, ActPlugins.ActFolder + "/Plugins/cactbot/cactbot/CactbotOverlay.dll", "MZ");
        Write(roaming, CactbotUser + "/raidboss.js", "Options.Language = 'de';");
        Write(roaming, ActPlugins.ActFolder + "/Plugins/ACT.Hojoring/ACT.SpecialSpellTimer.dll", "MZ");
        Write(roaming, ActPlugins.ActFolder + "/FFXIVLogs/Network_1.log", "log");
        Write(roaming, ActPlugins.ActFolder + "/Advanced Combat Tracker.log", "log");
        Write(roaming, ActPlugins.ActFolder + "/OverlayPluginCache/x.bin", "cache");
        Write(roaming, "anoyetta/ACT/ACT.SpecialSpellTimer.Spells.xml", "<Spells />");
        Write(roaming, "anoyetta/ACT/ACT.SpecialSpellTimer.config", "<config />");
        Write(roaming, "anoyetta/ACT/logs/x.xml", "<log />");
        Write(roaming, "anoyetta/ACT/fflogs.db", "db");
        Write(roaming, "SomeOtherApp/settings.json", "{ }");
    }

    private static string MainConfig(string roaming, bool extra)
    {
        var plugins = Path.Combine(roaming, "Advanced Combat Tracker", "Plugins");
        var list = string.Concat(
            $"""<Plugin Enabled="True" Path="{plugins}\FFXIV_ACT_Plugin.dll" />""",
            $"""<Plugin Enabled="True" Path="{plugins}\OverlayPlugin\OverlayPlugin.dll" />""",
            $"""<Plugin Enabled="True" Path="{plugins}\cactbot\cactbot\CactbotOverlay.dll" />""",
            $"""<Plugin Enabled="False" Path="{plugins}\ACT.Hojoring\ACT.SpecialSpellTimer.dll" />""",
            extra ? $"""<Plugin Enabled="True" Path="{plugins}\Triggernometry.dll" />""" : string.Empty);

        return $"""<?xml version="1.0" encoding="utf-8"?><Config><ActPlugins>{list}</ActPlugins><DirectoryInfo Name="folderPlugins" Value="{plugins}" /></Config>""";
    }

    private static void Write(string roaming, string relative, string text)
    {
        var full = Path.Combine(roaming, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static byte[] Read(string roaming, string relative) => File.ReadAllBytes(Path.Combine(roaming, relative.Replace('/', Path.DirectorySeparatorChar)));
}
