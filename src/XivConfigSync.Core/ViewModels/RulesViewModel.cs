using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.ViewModels;

/// <summary>A rule as a choice in a list.</summary>
public sealed record RuleChoice(ApplyRule Rule, string Name);

/// <summary>One line of the rules page: a matrix row with its effective rule.</summary>
public sealed partial class RuleRow : ObservableObject
{
    private readonly RulesViewModel owner;
    private bool loading = true;

    public RuleRow(RulesViewModel owner, MatrixEntry entry, ApplyRule effective)
    {
        this.owner = owner;
        Entry = entry;
        Choice = RulesViewModel.Choices.First(c => c.Rule == effective);
        loading = false;
    }

    public MatrixEntry Entry { get; }

    public string Root => Entry.Root == Roots.Game ? Strings.Root_Game : Strings.Root_Plugins;

    public string Pattern => Entry.Pattern == "**"
        ? Strings.Rules_AllFiles
        : Entry.Pattern.Replace("FFXIV_CHR*/", Strings.Character_Any + "/", StringComparison.Ordinal).Replace("/**", "/...", StringComparison.Ordinal);

    public string Note => Text.Get(Entry.NoteKey);

    public string DefaultText => RulesViewModel.RuleName(Entry.DefaultRule);

    public string EvidenceText => Text.Get("Evidence_" + Entry.Evidence);

    public bool IsChanged => Choice.Rule != Entry.DefaultRule;

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

/// <summary>A user exclusion in the list.</summary>
public sealed record ExclusionRow(string Root, string Pattern, string RootText);

/// <summary>The rules page: the matrix with the user's overrides, and exclusions from the backup.</summary>
public sealed partial class RulesViewModel : ObservableObject
{
    private readonly MainViewModel main;

    public RulesViewModel(MainViewModel main)
    {
        this.main = main;
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

    public ObservableCollection<RuleRow> Rows { get; } = [];

    public ObservableCollection<ExclusionRow> Exclusions { get; } = [];

    public IReadOnlyList<string> RootChoices { get; } = [Strings.Root_Game, Strings.Root_Plugins];

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

        var updated = main.Engine.Rules.WithOverride(row.Entry.Root, row.Entry.Pattern, rule == row.Entry.DefaultRule ? null : rule);
        main.Engine.UpdateRules(updated);
    }

    [RelayCommand]
    public void ResetAll()
    {
        main.Engine.UpdateRules(new RuleSet(new RuleFile { Exclusions = [.. main.Engine.Rules.File.Exclusions] }));
        Reload();
    }

    [RelayCommand]
    public void AddExclusion()
    {
        var pattern = NewExclusionPattern.Trim().Replace('\\', '/');
        if (pattern.Length == 0 || pattern.Contains("..", StringComparison.Ordinal))
        {
            return;
        }

        var root = NewExclusionRoot == 0 ? Roots.Game : Roots.Plugins;
        main.Engine.UpdateRules(main.Engine.Rules.WithExclusion(root, pattern, true));
        NewExclusionPattern = string.Empty;
        Reload();
    }

    [RelayCommand]
    public void RemoveExclusion(ExclusionRow? row)
    {
        if (row is null)
        {
            return;
        }

        main.Engine.UpdateRules(main.Engine.Rules.WithExclusion(row.Root, row.Pattern, false));
        Reload();
    }

    /// <summary>Rebuilds the lists from the effective rules.</summary>
    public void Reload()
    {
        Rows.Clear();
        foreach (var entry in FileMatrix.Entries)
        {
            var effective = main.Engine.Rules.File.Overrides
                .FirstOrDefault(o => o.Root == entry.Root && string.Equals(o.Pattern, entry.Pattern, StringComparison.OrdinalIgnoreCase))?.Rule
                ?? entry.DefaultRule;
            Rows.Add(new RuleRow(this, entry, effective));
        }

        Exclusions.Clear();
        foreach (var exclusion in main.Engine.Rules.File.Exclusions)
        {
            Exclusions.Add(new ExclusionRow(exclusion.Root, exclusion.Pattern, exclusion.Root == Roots.Game ? Strings.Root_Game : Strings.Root_Plugins));
        }
    }
}
