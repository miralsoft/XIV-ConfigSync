using System.Text;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>Showing which settings differ (owner's request, 2026-11-06).</summary>
public sealed class ComparisonTests
{
    private const string Common = Device.Chr + "/COMMON.DAT";

    [Fact]
    public void TheGamesTextFilesAreComparedSettingBySettingWithTheirPart()
    {
        var here = Latin1("<UI Settings>\r\nLogFontSize\t12\r\nColorTell\t1\r\nWeaponAutoPutAway\t1\r\n\0\r\n");
        var there = Latin1("<UI Settings>\r\nLogFontSize\t18\r\nColorTell\t1\r\nWeaponAutoPutAway\t0\r\nNewSetting\t5\r\n\0\r\n");
        var keep = new RuleSet(new RuleFile()).WithPart("chat.font", false).KeepLocalKeys(Roots.Game, Common);

        var result = SettingsComparison.Compare(Roots.Game, Common, here, there, keep);

        Assert.True(result.Readable);
        Assert.False(result.Truncated);
        Assert.Equal(["LogFontSize", "WeaponAutoPutAway", "NewSetting"], result.Differences.Select(d => d.Setting));
        var font = result.Differences[0];
        Assert.Equal(("12", "18", true, "Part_chat_font"), (font.Here, font.There, font.StaysHere, font.GroupKey));
        Assert.False(result.Differences[1].StaysHere);
        Assert.Null(result.Differences[2].Here);
        Assert.Equal("5", result.Differences[2].There);
    }

    [Fact]
    public void FilesOutsideTheSplitAreGroupedByTheirSection()
    {
        var here = Latin1("<Display Settings>\r\nScreenWidth\t1920\r\n<SoundPlay Settings>\r\nSoundMaster\t50\r\n");
        var there = Latin1("<Display Settings>\r\nScreenWidth\t2560\r\n<SoundPlay Settings>\r\nSoundMaster\t50\r\n");
        var result = SettingsComparison.Compare(Roots.Game, "FFXIV.cfg", here, there, null);
        var only = Assert.Single(result.Differences);
        Assert.Equal(("Display Settings", "ScreenWidth", "1920", "2560"), (only.GroupText, only.Setting, only.Here, only.There));
        Assert.Null(only.GroupKey);
    }

    [Fact]
    public void ASettingThatOccursTwiceIsComparedByOccurrence()
    {
        var here = Latin1("LockonDefaultZoom\t1.5\r\nLockonDefaultZoom\t3.0\r\n");
        var there = Latin1("LockonDefaultZoom\t1.5\r\nLockonDefaultZoom\t4.0\r\n");
        var only = Assert.Single(SettingsComparison.Compare(Roots.Game, Common, here, there, null).Differences);
        Assert.Equal(("3.0", "4.0"), (only.Here, only.There));
    }

    [Fact]
    public void PluginSettingsAreComparedFieldByFieldWithTheTopLevelAsGroup()
    {
        var here = Encoding.UTF8.GetBytes("""{ "Window": { "Position": { "X": 120, "Y": 40 } }, "Enabled": true, "Names": ["a", "b"] }""");
        var there = Encoding.UTF8.GetBytes("""{ "Window": { "Position": { "X": 860, "Y": 40 } }, "Enabled": true, "Names": ["a"], "Extra": "x" }""");
        var result = SettingsComparison.Compare(Roots.Plugins, "SomePlugin.json", here, there, null);
        Assert.True(result.Readable);
        Assert.Equal(["Window.Position.X", "Extra", "Names[1]"], result.Differences.Select(d => d.Setting));
        Assert.Equal(("Window", "120", "860"), (result.Differences[0].GroupText, result.Differences[0].Here, result.Differences[0].There));
        Assert.Equal(string.Empty, result.Differences[1].GroupText);
        Assert.Null(result.Differences[1].Here);
        Assert.Null(result.Differences[2].There);
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("Discord.WebhookUrl")]
    [InlineData("DktSessionId")]
    [InlineData("Auth.Token")]
    [InlineData("LastLogin0")]
    public void CredentialsAreNeverShown(string setting)
    {
        Assert.True(SettingsComparison.IsSensitive(setting));
        var here = Encoding.UTF8.GetBytes("{ \"" + setting + "\": \"mine-secret\" }");
        var there = Encoding.UTF8.GetBytes("{ \"" + setting + "\": \"their-secret\" }");
        var only = Assert.Single(SettingsComparison.Compare(Roots.Plugins, "P.json", here, there, null).Differences);
        Assert.DoesNotContain("secret", only.Here, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", only.There, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Roots.Game, Device.Chr + "/HOTBAR.DAT")]
    [InlineData(Roots.Game, Device.Chr + "/ADDON.DAT")]
    [InlineData(Roots.Plugins, "SomePlugin/data.db")]
    [InlineData(Roots.Plugins, "Broken.json")]
    public void BinaryOrUnknownFilesAreOnlySaidToDiffer(string root, string path)
    {
        var result = SettingsComparison.Compare(root, path, [0, 1, 2, 9, 9], [0, 1, 3, 9, 9], null);
        Assert.False(result.Readable);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public void LongListsAreCutAndLongValuesShortened()
    {
        var many = new StringBuilder("{");
        var others = new StringBuilder("{");
        for (var i = 0; i < SettingsComparison.MaxDifferences + 10; i++)
        {
            many.Append("\"K").Append(i).Append("\": 1,");
            others.Append("\"K").Append(i).Append("\": 2,");
        }

        many.Append("\"Long\": \"").Append('a', 500).Append("\"}");
        others.Append("\"Long\": \"b\"}");
        var result = SettingsComparison.Compare(Roots.Plugins, "P.json", Encoding.UTF8.GetBytes(many.ToString()), Encoding.UTF8.GetBytes(others.ToString()), null);
        Assert.True(result.Truncated);
        Assert.Equal(SettingsComparison.MaxDifferences, result.Differences.Count);

        var shortened = SettingsComparison.Compare(Roots.Plugins, "P.json", Encoding.UTF8.GetBytes("{\"Long\": \"" + new string('a', 500) + "\"}"), Encoding.UTF8.GetBytes("{\"Long\": \"b\"}"), null);
        Assert.True(Assert.Single(shortened.Differences).Here!.Length <= 121);
    }

    [Fact]
    public void TheEngineComparesAFileOfAPlanWithoutWritingAnything()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.WriteGame(Common, Latin1("<UI Settings>\r\nLogFontSize\t12\r\n"));
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = new Device(temp, "b");
        b.Populate();
        b.WriteGame(Common, Latin1("<UI Settings>\r\nLogFontSize\t18\r\n"));
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);
        var before = b.ReadGame(Common);

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        var item = plan.Items.Single(i => i.Path == Common);
        var only = Assert.Single(b.Engine.CompareSettings(plan, item).Differences);
        Assert.Equal(("18", "12"), (only.Here, only.There));
        Assert.False(b.Engine.CompareSettings(plan, plan.Items.Single(i => i.Path == Device.Chr + "/HOTBAR.DAT")).Readable);
        Assert.Equal(before, b.ReadGame(Common));
    }

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
