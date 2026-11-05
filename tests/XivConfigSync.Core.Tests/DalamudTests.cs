using System.Text;
using System.Text.Json.Nodes;
using XivConfigSync.Core.Dalamud;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>
/// Dalamud's own folder as a backup area, and merging plugin repositories between devices (owner's
/// requests, 2026-11-05). The settings file has the shape of a real one, with made-up addresses.
/// </summary>
public sealed class DalamudTests
{
    private const string Repo1 = "https://example.invalid/one/pluginmaster.json";
    private const string Repo2 = "https://example.invalid/two/pluginmaster.json";
    private const string Repo3 = "https://example.invalid/three/repo.json";

    [Fact]
    public void FindsTheRepositoriesTheOtherDeviceHasAndThisOneLacks()
    {
        var local = Settings((Repo1, true), (Repo2, true));
        // Same address in another case and with a trailing slash: not missing.
        var source = Settings((Repo2.ToUpperInvariant() + "/", true), (Repo3, false));

        var missing = RepositoryMerge.Missing(local, source);

        Assert.Equal([new PluginRepository(Repo3, false)], missing);
    }

    [Fact]
    public void MergeAppendsOnlyTheMissingAndKeepsEverythingElse()
    {
        var local = Settings((Repo1, true), (Repo2, false));
        var source = Settings((Repo2, true), (Repo3, true));

        var merged = RepositoryMerge.Merge(local, source)!;

        Assert.Equal([new(Repo1, true), new(Repo2, false), new(Repo3, true)], RepositoryMerge.Read(merged));
        var before = JsonNode.Parse(Encoding.UTF8.GetString(local))!.AsObject();
        var after = JsonNode.Parse(Encoding.UTF8.GetString(merged))!.AsObject();
        Assert.Equal(before.Select(p => p.Key), after.Select(p => p.Key));
        foreach (var (key, value) in before.Where(p => p.Key != "ThirdRepoList"))
        {
            Assert.True(JsonNode.DeepEquals(value, after[key]), key);
        }

        // The appended entry is the source's, with its type first, as Dalamud's reader needs it.
        var added = after["ThirdRepoList"]!["$values"]!.AsArray()[2]!.AsObject();
        Assert.Equal("$type", added.First().Key);
        Assert.Null(RepositoryMerge.Merge(merged, source));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void MergeKeepsTheLineEndsAndTheTextOfTheFile(string newline)
    {
        var local = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Settings((Repo1, true))).Replace("\r\n", newline, StringComparison.Ordinal));
        var merged = Encoding.UTF8.GetString(RepositoryMerge.Merge(local, Settings((Repo3, true)))!);

        // Every line of the file is still there, in order: the last entry gains its comma and the
        // new entry's five lines follow it.
        var before = Encoding.UTF8.GetString(local).Split(newline);
        var after = merged.Split(newline);
        Assert.DoesNotContain("\r\r", merged, StringComparison.Ordinal);
        Assert.Equal(before.Length + 5, after.Length);
        Assert.Equal(before.Take(11), after.Take(11));
        Assert.Equal(before[11] + ",", after[11]);
        Assert.Equal(before.Skip(12), after.Skip(17));
    }

    [Theory]
    [InlineData("{ \"ThirdRepoList\": [ { \"Url\": \"x\" } ] }")]
    [InlineData("{ \"ThirdRepoList\": { \"$values\": [ { \"Name\": \"x\" } ] } }")]
    [InlineData("{ \"Other\": 1 }")]
    [InlineData("not json")]
    public void RefusesASettingsFileOfAnotherShape(string text) =>
        Assert.Throws<InvalidDataException>(() => RepositoryMerge.Read(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void BacksUpTheTopLevelOfDalamudsFolderOnlyAndNeverOffersItToAnotherDevice()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp, Settings((Repo1, true)), Settings((Repo2, true)));
        var folder = DalamudFolder(a);
        File.WriteAllText(Path.Combine(folder, "dalamud.log"), "log");
        File.WriteAllText(Path.Combine(folder, "crash.dmp"), "dump");
        File.WriteAllText(Path.Combine(folder, "dalamudVfs.db"), "shadow");
        File.WriteAllText(Path.Combine(folder, "dalamudUI.ini"), "[Window]");
        // A file without a row of its own in the matrix; B does not have it.
        File.WriteAllText(Path.Combine(folder, "otherTool.json"), "{}");
        Directory.CreateDirectory(Path.Combine(folder, "installedPlugins", "SomePlugin"));
        File.WriteAllText(Path.Combine(folder, "installedPlugins", "SomePlugin", "SomePlugin.dll"), "binary");
        a.Engine.Backup(RevisionOrigin.Manual);

        var store = a.Engine.OpenStore();
        var manifest = store.ReadRevision(a.Settings.DeviceId, store.ReadHead(a.Settings.DeviceId)!);
        var dalamud = manifest.AllFiles().Where(f => f.Root == Roots.Dalamud).Select(f => f.File.Path).Order(StringComparer.Ordinal);
        Assert.Equal(["dalamudConfig.json", "dalamudUI.ini", "otherTool.json"], dalamud);
        Assert.Contains(manifest.AllFiles(), f => f.Root == Roots.Plugins && f.File.Path == "Some.json");

        var plan = b.Engine.Plan(a.Settings.DeviceId, null);
        var offered = plan.Items.Where(i => i.Root == Roots.Dalamud).ToList();
        Assert.Equal(3, offered.Count);
        Assert.All(offered, i => Assert.Equal(PlanAction.Skip, i.Action));
        Assert.All(offered, i => Assert.Equal(Rules.ApplyRule.Never, i.Rule));
    }

    [Fact]
    public void MergesRepositoriesAsAnApplyThatCanBeUndone()
    {
        using var temp = new TempFolder();
        var original = Settings((Repo1, true));
        var (a, b) = TwoDevices(temp, original, Settings((Repo1, true), (Repo3, true)));
        var path = Path.Combine(DalamudFolder(a), RepositoryMerge.FileName);

        Assert.Equal([new PluginRepository(Repo3, true)], a.Engine.MissingRepositories(b.Settings.DeviceId));
        var added = a.Engine.MergeRepositories(b.Settings.DeviceId);

        Assert.Equal([new PluginRepository(Repo3, true)], added);
        Assert.Equal([Repo1, Repo3], RepositoryMerge.Read(File.ReadAllBytes(path)).Select(r => r.Url));
        Assert.Empty(a.Engine.MissingRepositories(b.Settings.DeviceId));

        a.Engine.Undo(a.Engine.ListSnapshots()[0].SnapshotId);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void MergeIsRefusedWhileTheGameRunsAndForAnUnknownFile()
    {
        using var temp = new TempFolder();
        var (a, b) = TwoDevices(temp, Settings((Repo1, true)), Settings((Repo3, true)));
        var path = Path.Combine(DalamudFolder(a), RepositoryMerge.FileName);

        a.Game.IsRunning = true;
        Assert.Equal("Refuse_GameRunning", Assert.Throws<RefusedException>(() => a.Engine.MergeRepositories(b.Settings.DeviceId)).ReasonKey);
        a.Game.IsRunning = false;

        var unknown = Encoding.UTF8.GetBytes("{ \"ThirdRepoList\": [] }");
        File.WriteAllBytes(path, unknown);
        Assert.Equal("Refuse_DalamudSettingsUnknown", Assert.Throws<RefusedException>(() => a.Engine.MergeRepositories(b.Settings.DeviceId)).ReasonKey);
        Assert.Equal(unknown, File.ReadAllBytes(path));
    }

    /// <summary>A settings file in Dalamud's form: type names, two-space indentation, CRLF.</summary>
    private static byte[] Settings(params (string Url, bool Enabled)[] repositories)
    {
        var entries = string.Join(
            ",\r\n",
            repositories.Select(r => $"      {{\r\n        \"$type\": \"Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud\",\r\n        \"Url\": \"{r.Url}\",\r\n        \"IsEnabled\": {(r.Enabled ? "true" : "false")}\r\n      }}"));
        var text = "{\r\n  \"$type\": \"Dalamud.Configuration.Internal.DalamudConfiguration, Dalamud\",\r\n  \"BadWords\": null,\r\n  \"DutyFinderTaskbarFlash\": true,\r\n"
            + "  \"ThirdRepoList\": {\r\n    \"$type\": \"System.Collections.Generic.List`1[[Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud]], System.Private.CoreLib\",\r\n    \"$values\": [\r\n"
            + entries + "\r\n    ]\r\n  },\r\n  \"GlobalUiScale\": 1.25,\r\n  \"LanguageOverride\": \"de\",\r\n  \"ThirdRepoSpeedbumpDismissed\": true\r\n}";
        return Encoding.UTF8.GetBytes(text);
    }

    private static string DalamudFolder(Device device) => Path.GetDirectoryName(device.Settings.PluginConfigFolder!)!;

    /// <summary>Two devices whose plugin folder is Dalamud's <c>pluginConfigs</c>, each with its own settings file.</summary>
    private static (Device A, Device B) TwoDevices(TempFolder temp, byte[] settingsA, byte[] settingsB)
    {
        var a = WithDalamud(new Device(temp, "a"), temp.Sub("a-launcher"), settingsA);
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = WithDalamud(new Device(temp, "b"), temp.Sub("b-launcher"), settingsB);
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);
        return (a, b);
    }

    private static Device WithDalamud(Device device, string launcher, byte[] settings)
    {
        device.Populate();
        var plugins = Path.Combine(launcher, "pluginConfigs");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "Some.json"), "{}");
        File.WriteAllBytes(Path.Combine(launcher, RepositoryMerge.FileName), settings);
        device.Settings.PluginConfigFolder = plugins;
        return device;
    }
}
