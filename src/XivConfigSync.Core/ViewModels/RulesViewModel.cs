using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Discovery;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.ViewModels;

/// <summary>
/// ACT or one of its plugins on the rules page, with the paths of its settings: the proposal, or
/// the user's own list (owner's request, 2026-11-06).
/// </summary>
public sealed partial class ActRow : ObservableObject
{
    private readonly RulesViewModel owner;

    public ActRow(RulesViewModel owner, ActPlugin plugin, int files)
    {
        this.owner = owner;
        Key = plugin.Key;
        Name = plugin.Key == ActPlugins.ActKey ? Strings.Act_Self : plugin.Key;
        PathsText = string.Join(Environment.NewLine, plugin.Paths);
        var state = new List<string>
        {
            plugin.Key == ActPlugins.ActKey ? Strings.Act_SelfState : plugin.Enabled ? Strings.Act_Enabled : Strings.Act_Disabled,
            files == 1 ? Strings.Act_FilesOne : Text.Format(Strings.Act_Files, files),
            plugin.IsCustom ? Strings.Act_Custom : Strings.Act_Proposal,
        };
        StateText = string.Join(" · ", state);
        OutsideText = plugin.Outside.Count == 0 ? string.Empty : Text.Format(Strings.Act_Outside, string.Join(", ", plugin.Outside));
        IsCustom = plugin.IsCustom;
    }

    public string Key { get; }

    public string Name { get; }

    /// <summary>Whether ACT loads it, how many files its paths name, and whether they are the proposal.</summary>
    public string StateText { get; }

    /// <summary>Paths its settings name that are not backed up: outside the roaming folder or too large.</summary>
    public string OutsideText { get; }

    public bool IsCustom { get; }

    /// <summary>The paths, one per line, relative to the roaming folder; editable.</summary>
    [ObservableProperty]
    public partial string PathsText { get; set; }

    [RelayCommand]
    public void Save() => owner.SaveActPaths(this);

    [RelayCommand]
    public void UseProposal() => owner.ResetActPaths(Key);

    public override string ToString() => Name;
}

/// <summary>A rule as a choice in a list.</summary>
public sealed record RuleChoice(ApplyRule Rule, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One line of the rules page: a matrix row with its effective rule.</summary>
public sealed partial class RuleRow : ObservableObject
{
    private readonly RulesViewModel owner;
    private bool loading = true;

    public RuleRow(RulesViewModel owner, MatrixEntry entry, ApplyRule defaultRule, ApplyRule effective)
    {
        this.owner = owner;
        Entry = entry;
        DefaultRule = defaultRule;
        Choice = RulesViewModel.Choices.First(c => c.Rule == effective);
        loading = false;
    }

    public MatrixEntry Entry { get; }

    /// <summary>The rule without an override on this row: its area's switch, or the matrix default.</summary>
    public ApplyRule DefaultRule { get; }

    public string Root => Text.RootName(Entry.Root);

    public string Pattern => Entry.Pattern == "**"
        ? Strings.Rules_AllFiles
        : Entry.Pattern.Replace("FFXIV_CHR*/", Strings.Character_Any + "/", StringComparison.Ordinal).Replace("/**", "/...", StringComparison.Ordinal);

    public string Note => Text.Get(Entry.NoteKey);

    public string DefaultText => RulesViewModel.RuleName(DefaultRule);

    public string EvidenceText => Text.Get("Evidence_" + Entry.Evidence);

    public bool IsChanged => Choice.Rule != DefaultRule;

    [ObservableProperty]
    public partial RuleChoice Choice { get; set; }

    partial void OnChoiceChanged(RuleChoice value)
    {
        if (!loading)
        {
            owner.Change(this, value.Rule);
            OnPropertyChanged(nameof(IsChanged));
        }
    }
}

/// <summary>A rule choice for a plugin; a null rule follows the rule for all plugin files.</summary>
public sealed record PluginRuleChoice(ApplyRule? Rule, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One plugin on the rules page, with its own rule and whether it is backed up.</summary>
public sealed partial class PluginRuleRow : ObservableObject
{
    private readonly RulesViewModel owner;
    private bool loading = true;

    public PluginRuleRow(RulesViewModel owner, PluginInfo plugin, ApplyRule? rule, bool excluded)
    {
        this.owner = owner;
        Name = plugin.Name;
        FilesText = Text.Format(Strings.Rules_PluginFiles, plugin.Files, Math.Max(1, (plugin.Size + 1023) / 1024));
        Choice = RulesViewModel.PluginChoices.First(c => c.Rule == rule);
        NotBackedUp = excluded;
        loading = false;
    }

    public string Name { get; }

    public string FilesText { get; }

    [ObservableProperty]
    public partial PluginRuleChoice Choice { get; set; }

    [ObservableProperty]
    public partial bool NotBackedUp { get; set; }

    partial void OnChoiceChanged(PluginRuleChoice value)
    {
        if (!loading)
        {
            owner.ChangePlugin(Name, value.Rule);
        }
    }

    partial void OnNotBackedUpChanged(bool value)
    {
        if (!loading)
        {
            owner.ExcludePlugin(Name, value);
        }
    }
}

/// <summary>One area on the rules page: what a player recognises, switched on or off for this device.</summary>
public sealed partial class AreaRow : ObservableObject
{
    private readonly RulesViewModel owner;
    private bool loading = true;

    public AreaRow(RulesViewModel owner, string key, bool on, IEnumerable<PartRow> parts)
    {
        this.owner = owner;
        Key = key;
        IsOn = on;
        Parts = [.. parts];
        loading = false;
    }

    /// <summary>The groups of settings that can be switched on their own; empty for most areas.</summary>
    public IReadOnlyList<PartRow> Parts { get; }

    public bool HasParts => Parts.Count > 0;

    public string Key { get; }

    public string Name => Text.Get("Area_" + Key);

    public string Hint => Text.Get("Area_" + Key + "_Hint");

    /// <summary>Whether the area is taken from other devices.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (!loading)
        {
            owner.ChangeArea(Key, value);
        }
    }

    public override string ToString() => Name;
}

/// <summary>A group of settings under an area, such as the chat's font size.</summary>
public sealed partial class PartRow : ObservableObject
{
    private readonly RulesViewModel owner;
    private bool loading = true;

    public PartRow(RulesViewModel owner, SettingPart part, bool on)
    {
        this.owner = owner;
        Key = part.Key;
        Name = Text.Get(part.NameKey);
        IsOn = on;
        loading = false;
    }

    public string Key { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (!loading)
        {
            owner.ChangePart(Key, value);
        }
    }

    public override string ToString() => Name;
}

/// <summary>A user exclusion in the list.</summary>
public sealed record ExclusionRow(string Root, string Pattern, string RootText);

/// <summary>The rules page: the matrix with the user's overrides, and exclusions from the backup.</summary>
public sealed partial class RulesViewModel : ObservableObject
{
    private readonly MainViewModel main;
    private readonly IUiServices ui;

    public RulesViewModel(MainViewModel main, IUiServices ui)
    {
        this.main = main;
        this.ui = ui;
        Reload();
    }

    /// <summary>The three rules with their names.</summary>
    public static IReadOnlyList<RuleChoice> Choices { get; } =
    [
        new(ApplyRule.Always, Strings.Rule_Always),
        new(ApplyRule.OnRequest, Strings.Rule_OnRequest),
        new(ApplyRule.Never, Strings.Rule_Never),
    ];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound from XAML, which needs an instance member.")]
    public IReadOnlyList<RuleChoice> RuleChoices => Choices;

    /// <summary>The areas, the first thing on the page.</summary>
    public ObservableCollection<AreaRow> AreaRows { get; } = [];

    public ObservableCollection<RuleRow> Rows { get; } = [];

    public ObservableCollection<ExclusionRow> Exclusions { get; } = [];

    /// <summary>The plugins found in the plugin settings folder, each with its own rule.</summary>
    public ObservableCollection<PluginRuleRow> Plugins { get; } = [];

    /// <summary>The choices for a plugin: its own rule, or the one for all plugin files.</summary>
    public static IReadOnlyList<PluginRuleChoice> PluginChoices { get; } =
    [
        new(null, Strings.Rules_PluginDefault),
        new(ApplyRule.Always, Strings.Rule_Always),
        new(ApplyRule.OnRequest, Strings.Rule_OnRequest),
        new(ApplyRule.Never, Strings.Rule_Never),
    ];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound from XAML, which needs an instance member.")]
    public IReadOnlyList<PluginRuleChoice> PluginRuleChoices => PluginChoices;

    public IReadOnlyList<string> RootChoices { get; } = [.. Roots.All.Select(Text.RootName)];

    [ObservableProperty]
    public partial string NewExclusionPattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int NewExclusionRoot { get; set; } = 1;

    /// <summary>The name of a rule.</summary>
    public static string RuleName(ApplyRule rule) => Choices.First(c => c.Rule == rule).Name;

    /// <summary>Applies a change from a row.</summary>
    public void Change(RuleRow row, ApplyRule rule)
    {
        if (rule == ApplyRule.Always && row.Entry.Class is FileClass.Device or FileClass.Mixed or FileClass.Sensitive)
        {
            main.StatusText = Strings.Rules_NeverWarning;
        }

        // Through the operation queue, so a running backup or plan never sees rules change halfway
        // (review 3, finding 8).
        var root = row.Entry.Root;
        var pattern = row.Entry.Pattern;
        var value = rule == row.DefaultRule ? (ApplyRule?)null : rule;
        _ = main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithOverride(root, pattern, value)));
    }

    /// <summary>Switches an area, through the operation queue like every rule change; the file rows follow it.</summary>
    public void ChangeArea(string key, bool on) => _ = ChangeAreaAsync(key, on);

    private async Task ChangeAreaAsync(string key, bool on)
    {
        await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithArea(key, on))).ConfigureAwait(true);
        ReloadFiles();
    }

    /// <summary>Switches a part of a settings file.</summary>
    public void ChangePart(string key, bool on) =>
        _ = main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithPart(key, on)));

    /// <summary>Sets a plugin's own rule, through the operation queue like every rule change.</summary>
    public void ChangePlugin(string name, ApplyRule? rule) =>
        _ = main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(PluginRules.WithRule(main.Engine.Rules, name, rule)));

    /// <summary>Leaves a plugin out of the backup, or takes it in again.</summary>
    public void ExcludePlugin(string name, bool excluded) => _ = ExcludePluginAsync(name, excluded);

    private async Task ExcludePluginAsync(string name, bool excluded)
    {
        await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(PluginRules.WithExcluded(main.Engine.Rules, name, excluded))).ConfigureAwait(true);

        // The two patterns show in the exclusion list too, where they can also be removed.
        ReloadExclusions();
    }

    [RelayCommand]
    public async Task ResetAllAsync()
    {
        if (!ui.Confirm(Strings.Rules_Title, Strings.Rules_ResetConfirm))
        {
            return;
        }

        await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(new RuleSet(new RuleFile { Exclusions = [.. main.Engine.Rules.File.Exclusions] }))).ConfigureAwait(true);
        Reload();
    }

    [RelayCommand]
    public async Task AddExclusionAsync()
    {
        var pattern = NewExclusionPattern.Trim().Replace('\\', '/');
        if (pattern.Length == 0 || pattern.Contains("..", StringComparison.Ordinal))
        {
            return;
        }

        var root = Roots.All[Math.Clamp(NewExclusionRoot, 0, Roots.All.Count - 1)];
        if (await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithExclusion(root, pattern, true))).ConfigureAwait(true))
        {
            NewExclusionPattern = string.Empty;
        }

        Reload();
    }

    [RelayCommand]
    public async Task RemoveExclusionAsync(ExclusionRow? row)
    {
        if (row is null)
        {
            return;
        }

        await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithExclusion(row.Root, row.Pattern, false))).ConfigureAwait(true);
        Reload();
    }

    /// <summary>ACT and its plugins, each with the paths of its settings.</summary>
    public ObservableCollection<ActRow> ActRows { get; } = [];

    /// <summary>Whether ACT is found here, and whether its settings are backed up.</summary>
    [ObservableProperty]
    public partial string ActState { get; set; } = string.Empty;

    /// <summary>Saves the paths a row's text names; lines that are not usable are reported and nothing is saved.</summary>
    public void SaveActPaths(ActRow row)
    {
        var roaming = main.Engine.RoamingFolder;
        var lines = row.PathsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bad = lines.Where(l => ActPlugins.ParsePath(roaming, l) is null).ToList();
        if (bad.Count > 0)
        {
            ui.Inform(Strings.Rules_Title, Text.Format(Strings.Act_BadPaths, Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, bad)));
            return;
        }

        var paths = lines.Select(l => ActPlugins.ParsePath(roaming, l)!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var key = row.Key;
        _ = SaveActAsync(rules => rules.WithActPaths(key, paths));
    }

    /// <summary>Goes back to the proposal for a plugin.</summary>
    public void ResetActPaths(string key) => _ = SaveActAsync(rules => rules.WithActPaths(key, null));

    private async Task SaveActAsync(Func<RuleSet, RuleSet> change)
    {
        await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(change(main.Engine.Rules))).ConfigureAwait(true);
        ReloadAct();
    }

    private void ReloadAct()
    {
        ActRows.Clear();
        var engine = main.Engine;
        if (!engine.HasAct)
        {
            ActState = Strings.Act_NotFound;
            return;
        }

        ActState = engine.BacksUpAct ? Strings.Act_Intro : Strings.Act_Off;
        foreach (var plugin in engine.ActPluginList())
        {
            var files = ActPlugins.Expand(engine.RoamingFolder, plugin.Paths).Count(p => engine.Rules.ExclusionReason(Roots.Act, p) is null);
            ActRows.Add(new ActRow(this, plugin, files));
        }
    }

    /// <summary>How many single settings have their own choice from the transfer page; empty when none.</summary>
    [ObservableProperty]
    public partial string SettingChoicesText { get; set; } = string.Empty;

    /// <summary>Lets every single setting follow its group again.</summary>
    [RelayCommand]
    public async Task ResetSettingChoicesAsync()
    {
        await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithoutSettingChoices())).ConfigureAwait(true);
        Reload();
    }

    /// <summary>Rebuilds the lists from the effective rules.</summary>
    public void Reload()
    {
        var choices = main.Engine.Rules.File.Settings.Count;
        SettingChoicesText = choices switch
        {
            0 => string.Empty,
            1 => Strings.Rules_SettingChoicesOne,
            _ => Text.Format(Strings.Rules_SettingChoices, choices),
        };
        AreaRows.Clear();
        foreach (var key in Areas.Keys)
        {
            AreaRows.Add(new AreaRow(
                this,
                key,
                main.Engine.Rules.IsAreaOn(key),
                SettingParts.OfArea(key).Select(p => new PartRow(this, p, main.Engine.Rules.IsPartSwitchOn(p.Key)))));
        }

        ReloadFiles();
        ReloadExclusions();
        ReloadAct();

        Plugins.Clear();
        foreach (var plugin in PluginRules.List(main.Engine.PluginFolder))
        {
            Plugins.Add(new PluginRuleRow(this, plugin, PluginRules.RuleOf(main.Engine.Rules, plugin.Name), PluginRules.IsExcluded(main.Engine.Rules, plugin.Name)));
        }
    }

    private void ReloadFiles()
    {
        Rows.Clear();
        foreach (var entry in FileMatrix.Entries)
        {
            var defaultRule = main.Engine.Rules.DefaultFor(entry.Root, entry.Pattern);
            var effective = main.Engine.Rules.File.Overrides
                .FirstOrDefault(o => o.Root == entry.Root && string.Equals(o.Pattern, entry.Pattern, StringComparison.OrdinalIgnoreCase))?.Rule
                ?? defaultRule;
            Rows.Add(new RuleRow(this, entry, defaultRule, effective));
        }
    }

    private void ReloadExclusions()
    {
        Exclusions.Clear();
        foreach (var exclusion in main.Engine.Rules.File.Exclusions)
        {
            Exclusions.Add(new ExclusionRow(exclusion.Root, exclusion.Pattern, Text.RootName(exclusion.Root)));
        }
    }
}
