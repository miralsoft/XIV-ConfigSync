using System.Globalization;
using XivConfigSync.Core.Discovery;
using XivConfigSync.Core.Logging;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Settings;
using XivConfigSync.Core.ViewModels;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>
/// The owner's requests of 2026-11-05: files only on this device shown in the transfer list, the
/// launcher folder as the setting, character names from plugin data, and rules per plugin. All
/// names and IDs here are made up.
/// </summary>
public sealed class RequestTests
{
    private const ulong IdOne = 0x00400011223344AA;
    private const ulong IdTwo = 0x00400011223344BB;
    private const ulong IdThree = 0x00400011223344CC;

    private static string Folder(ulong id) => "FFXIV_CHR" + id.ToString("X16", CultureInfo.InvariantCulture);

    [Fact]
    public async Task TheTransferListShowsTheFilesOnlyThisDeviceHasWhenAskedAndNeverSelectsThem()
    {
        using var temp = new TempFolder();
        var a = new Device(temp, "a");
        a.Populate();
        a.Engine.CreateSyncFolder(temp.Sub("sync"), "A");
        a.Engine.Backup(RevisionOrigin.Manual);
        var b = new Device(temp, "b");
        b.Populate();
        File.WriteAllText(Path.Combine(b.PluginFolder, "OnlyOnB.json"), "{}");
        b.Engine.JoinSyncFolder(temp.Sub("sync"), "B", null);
        b.Engine.Backup(RevisionOrigin.Manual);

        using var main = new MainViewModel(b.Engine, new QuietUi(), Log.None, b.Game, new NoShell(), new NoLocator(), new FakeRecycleBin(temp.Sub("bin")));
        await main.RefreshAsync();
        main.Transfer.SelectSource(a.Settings.DeviceId);
        await main.Transfer.CompareAsync();

        Assert.Contains("1", main.Transfer.Messages, StringComparison.Ordinal);
        Assert.DoesNotContain(main.Transfer.Rows, r => r.DisplayPath == "OnlyOnB.json");

        main.Transfer.ShowSkipped = true;
        var row = Assert.Single(main.Transfer.Rows, r => r.DisplayPath == "OnlyOnB.json");
        Assert.Equal(Strings.Action_OnlyHere, row.ActionText);
        Assert.False(row.Selectable);
        row.IsSelected = true;
        Assert.False(row.Item.Selected);
    }

    [Fact]
    public void TheLauncherFolderGivesThePluginAndDalamudFoldersAlsoInSandboxMode()
    {
        using var temp = new TempFolder();
        var classic = temp.Sub("XIVLauncher");
        Directory.CreateDirectory(Path.Combine(classic, "pluginConfigs"));
        Assert.Equal(classic, GameFolders.DalamudDataFolder(classic));
        Assert.Equal(Path.Combine(classic, "pluginConfigs"), GameFolders.PluginConfigFolderOf(classic));
        Assert.True(GameFolders.LooksLikeLauncherFolder(classic));

        var sandbox = temp.Sub("Sandboxed");
        Directory.CreateDirectory(Path.Combine(sandbox, "dalamudUserData", "pluginConfigs"));
        Assert.Equal(Path.Combine(sandbox, "dalamudUserData"), GameFolders.DalamudDataFolder(sandbox));
        Assert.Equal(sandbox, GameFolders.LauncherFolderOf(Path.Combine(sandbox, "dalamudUserData", "pluginConfigs")));

        Assert.False(GameFolders.LooksLikeLauncherFolder(temp.Sub("nothing")));
        Assert.Null(GameFolders.LauncherFolderOf(temp.Sub("my-own-plugins")));
    }

    [Fact]
    public void SettingsThatKnewOnlyThePluginFolderAreMovedToTheLauncherFolder()
    {
        using var temp = new TempFolder();
        var launcher = temp.Sub("XIVLauncher");
        var plugins = Path.Combine(launcher, "pluginConfigs");
        Directory.CreateDirectory(plugins);
        var path = temp.Sub("settings.json");
        new AppSettings { PluginConfigFolder = plugins }.Save(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal(launcher, loaded.DalamudFolder);
        var engine = new Engine.SyncEngine(new LocalData(temp.Sub("local")), loaded, new RuleSet(new RuleFile()), new FakeGame(), new FakeCloud(Platform.CloudFileState.InSync), Log.None);
        Assert.Equal(plugins, engine.Roots[Roots.Plugins]);
        Assert.Equal(launcher, engine.Roots[Roots.Dalamud]);

        // A plugin folder chosen by hand elsewhere stays in use as it is.
        var own = temp.Sub("own-plugins");
        new AppSettings { PluginConfigFolder = own }.Save(path);
        var kept = AppSettings.Load(path);
        Assert.Null(kept.DalamudFolder);
        Assert.Equal(own, new Engine.SyncEngine(new LocalData(temp.Sub("local2")), kept, new RuleSet(new RuleFile()), new FakeGame(), new FakeCloud(Platform.CloudFileState.InSync), Log.None).Roots[Roots.Plugins]);
    }

    [Fact]
    public void SuggestsNamesAndWorldsFromThePluginsByCharacterId()
    {
        using var temp = new TempFolder();
        var plugins = PluginsWithCharacters(temp);

        var found = CharacterIdentities.FromPluginData(plugins, [Folder(IdOne), Folder(IdTwo), Folder(IdThree), "FFXIV_CHR0040001122334499"]);

        // AutoRetainer first; the world from Lifestream where AutoRetainer does not know the character.
        Assert.Equal("Aster Vale (Testworld)", found[Folder(IdOne)].Display);
        Assert.Equal("Brin Hollow (Otherworld)", found[Folder(IdTwo)].Display);
        // Known only to Allagan Market, which gives the world as a number: name only.
        Assert.Equal("Cora Fen", found[Folder(IdThree)].Display);
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void ASourceOfAnotherShapeIsSkipped()
    {
        using var temp = new TempFolder();
        var plugins = temp.Sub("pluginConfigs");
        Directory.CreateDirectory(Path.Combine(plugins, "AutoRetainer"));
        File.WriteAllText(Path.Combine(plugins, "AutoRetainer", "DefaultConfig.json"), "{ \"OfflineData\": \"not a list\" }");
        Directory.CreateDirectory(Path.Combine(plugins, "Lifestream"));
        File.WriteAllText(Path.Combine(plugins, "Lifestream", "DefaultConfig.json"), "not json");

        Assert.Empty(CharacterIdentities.FromPluginData(plugins, [Folder(IdOne)]));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Character 2", true)]
    [InlineData("Charakter 12", true)]
    [InlineData("My main", false)]
    [InlineData("Charakter Zwei", false)]
    public void OnlyEmptyAndNumberedNamesMayBeReplaced(string name, bool placeholder) =>
        Assert.Equal(placeholder, CharacterIdentities.IsPlaceholder(name));

    [Fact]
    public void TakingSuggestionsKeepsTheNamesTheUserChose()
    {
        using var temp = new TempFolder();
        var device = new Device(temp, "a");
        Directory.CreateDirectory(Path.Combine(device.GameFolder, Folder(IdOne)));
        Directory.CreateDirectory(Path.Combine(device.GameFolder, Folder(IdTwo)));
        device.Settings.PluginConfigFolder = PluginsWithCharacters(temp);
        device.Settings.CharacterNames = new() { [Folder(IdOne)] = "Charakter 1", [Folder(IdTwo)] = "My own name" };

        var taken = device.Engine.TakeCharacterSuggestions();

        Assert.Equal(1, taken);
        Assert.Equal("Aster Vale (Testworld)", device.Settings.CharacterNames[Folder(IdOne)]);
        Assert.Equal("My own name", device.Settings.CharacterNames[Folder(IdTwo)]);
        Assert.Equal(0, device.Engine.TakeCharacterSuggestions());
    }

    [Fact]
    public void ListsPluginsByFileAndFolderName()
    {
        using var temp = new TempFolder();
        var plugins = temp.Sub("pluginConfigs");
        Directory.CreateDirectory(Path.Combine(plugins, "Alpha", "sub"));
        File.WriteAllText(Path.Combine(plugins, "Alpha.json"), "{}");
        File.WriteAllText(Path.Combine(plugins, "Alpha", "sub", "data.json"), "{}");
        File.WriteAllText(Path.Combine(plugins, "Beta.json"), "{}");
        File.WriteAllText(Path.Combine(plugins, "Beta.json.bak"), "{}");
        File.WriteAllText(Path.Combine(plugins, ".hidden"), "x");

        var list = PluginRules.List(plugins);

        Assert.Equal(["Alpha", "Beta"], list.Select(p => p.Name));
        Assert.Equal(2, list[0].Files);
        Assert.Equal(2, list[1].Files);
    }

    [Fact]
    public void APluginRuleAppliesToItsFilesOnlyAndWinsOverTheRuleForAllPluginFiles()
    {
        var rules = new RuleSet(new RuleFile()).WithOverride(Roots.Plugins, "**", ApplyRule.Always);
        rules = PluginRules.WithRule(rules, "Alpha", ApplyRule.Never);

        Assert.Equal(ApplyRule.Never, rules.RuleFor(Roots.Plugins, "Alpha.json"));
        Assert.Equal(ApplyRule.Never, rules.RuleFor(Roots.Plugins, "Alpha/sub/data.json"));
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Plugins, "AlphaBeta.json"));
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Plugins, "Beta/x.json"));
        Assert.Equal(ApplyRule.Never, PluginRules.RuleOf(rules, "Alpha"));

        rules = PluginRules.WithRule(rules, "Alpha", null);
        Assert.Null(PluginRules.RuleOf(rules, "Alpha"));
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Plugins, "Alpha.json"));
    }

    [Fact]
    public void APluginLeftOutIsNotBackedUpAndCanBeTakenInAgain()
    {
        var rules = PluginRules.WithExcluded(new RuleSet(new RuleFile()), "Alpha", true);

        Assert.True(PluginRules.IsExcluded(rules, "Alpha"));
        Assert.Equal("Exclude_User", rules.ExclusionReason(Roots.Plugins, "Alpha.json"));
        Assert.Equal("Exclude_User", rules.ExclusionReason(Roots.Plugins, "Alpha/sub/data.json"));
        Assert.Null(rules.ExclusionReason(Roots.Plugins, "Beta.json"));

        rules = PluginRules.WithExcluded(rules, "Alpha", false);
        Assert.False(PluginRules.IsExcluded(rules, "Alpha"));
        Assert.Empty(rules.File.Exclusions);
    }

    /// <summary>Plugin files in the shapes read from real ones, with made-up characters.</summary>
    private static string PluginsWithCharacters(TempFolder temp)
    {
        var plugins = temp.Sub("pluginConfigs");
        Directory.CreateDirectory(Path.Combine(plugins, "AutoRetainer"));
        Directory.CreateDirectory(Path.Combine(plugins, "Lifestream"));
        File.WriteAllText(
            Path.Combine(plugins, "AutoRetainer", "DefaultConfig.json"),
            $"{{ \"OfflineData\": [ {{ \"CID\": {IdOne}, \"Name\": \"Aster Vale\", \"World\": \"Testworld\", \"Gil\": 5 }} ] }}");
        File.WriteAllText(
            Path.Combine(plugins, "Lifestream", "DefaultConfig.json"),
            $"{{ \"CharaMap\": {{ \"{IdOne}\": \"Ignored Name@Ignoredworld\", \"{IdTwo}\": \"Brin Hollow@Otherworld\" }} }}");
        File.WriteAllText(
            Path.Combine(plugins, "AllaganMarket.json"),
            $"{{ \"$type\": \"x\", \"Characters\": {{ \"$type\": \"y\", \"{IdThree}\": {{ \"Name\": \"Cora Fen\", \"WorldId\": 33 }} }} }}");
        return plugins;
    }
}
