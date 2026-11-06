using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Dalamud;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.ViewModels;

/// <summary>A device to take settings from.</summary>
public sealed record SourceOption(string DeviceId, string Name)
{
    /// <summary>What a screen reader announces for the item: the name, never the device ID.</summary>
    public override string ToString() => Name;
}

/// <summary>A revision of the chosen device.</summary>
public sealed record RevisionOption(string? RevisionId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One file in the transfer preview.</summary>
public sealed partial class PlanRow : ObservableObject
{
    public PlanRow(PlanItem item, string displayPath)
    {
        Item = item;
        DisplayPath = displayPath;
        IsSelected = item.Selected;
    }

    public PlanItem Item { get; }

    public string DisplayPath { get; }

    public string Root => Text.RootName(Item.Root);

    /// <summary>The area a player recognises, such as "Hotbars and macros"; the root for files outside every area.</summary>
    public string AreaText
    {
        get
        {
            var areas = Areas.Of(Item.Root, Item.Path);
            return areas.Count == 0 ? Root : string.Join(", ", areas.Select(a => Text.Get(a.NameKey)));
        }
    }

    public string ActionText => Item.Reason == PlanReason.OnlyHere ? Strings.Action_OnlyHere : Item.Action switch
    {
        PlanAction.Apply => Strings.Action_Apply,
        PlanAction.Offer => Strings.Action_Offer,
        PlanAction.Conflict => Strings.Action_Conflict,
        _ => Strings.Action_Skip,
    };

    public bool IsConflict => Item.Action == PlanAction.Conflict;

    public string ReasonText => Text.Get(Item.Reason);

    public string RuleText => RulesViewModel.RuleName(Item.Rule);

    public string Note => Text.Get(Item.Matrix.NoteKey);

    public string Warnings => string.Join(
        " ",
        Item.Flags.Select(flag => flag switch
        {
            FileFlags.Credential => Strings.Flag_Credential,
            FileFlags.AbsolutePath => Strings.Flag_Path,
            FileFlags.Large => Strings.Flag_Large,
            _ => flag,
        }));

    public bool Selectable => Item.Selectable;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => Item.Selected = value && Item.Selectable;
}

/// <summary>One setting that differs, as shown under the transfer list.</summary>
public sealed partial class DiffRow : ObservableObject
{
    private readonly Action<DiffRow, int>? choose;

    /// <param name="name">A readable name where one is known, else the setting's own name.</param>
    /// <param name="setting">The setting's own name, shown small when <paramref name="name"/> is a readable one; else empty.</param>
    /// <param name="here">The value on this device.</param>
    /// <param name="there">The value on the other device.</param>
    /// <param name="outcome">What happens to it when the file is taken.</param>
    /// <param name="isKnown">Whether a readable name is known: such settings are the important ones and shown first.</param>
    /// <param name="key">The setting's own name in the file.</param>
    /// <param name="choice">The user's choice for this setting: 0 like its group, 1 always taken, 2 always kept here.</param>
    /// <param name="choose">Called when the user changes the choice; null where no choice is possible.</param>
    public DiffRow(string name, string setting, string here, string there, string outcome, bool isKnown, string key = "", int choice = 0, Action<DiffRow, int>? choose = null)
    {
        Name = name;
        Setting = setting;
        Here = here;
        There = there;
        Outcome = outcome;
        IsKnown = isKnown;
        Key = key;
        ChoiceIndex = choice;
        this.choose = choose;
    }

    /// <summary>The choices offered for a single setting, in the order of <see cref="ChoiceIndex"/>.</summary>
    public static IReadOnlyList<string> Choices { get; } = [Strings.Diff_ChoiceGroup, Strings.Diff_ChoiceTake, Strings.Diff_ChoiceKeep];

    public string Name { get; }

    public string Setting { get; }

    public string Here { get; }

    public string There { get; }

    public string Outcome { get; }

    public bool IsKnown { get; }

    public string Key { get; }

    /// <summary>Whether this setting can be chosen on its own (the game's character and control settings).</summary>
    public bool CanChoose => choose is not null;

    /// <summary>0 like its group, 1 always taken, 2 always kept here.</summary>
    [ObservableProperty]
    public partial int ChoiceIndex { get; set; }

    partial void OnChoiceIndexChanged(int value) => choose?.Invoke(this, value);

    public override string ToString() => Name;
}

/// <summary>The differing settings of one group, folded open or shut.</summary>
public sealed partial class DiffGroup : ObservableObject
{
    public DiffGroup(string name, IReadOnlyList<DiffRow> rows, bool expanded)
    {
        Name = name;
        Rows = rows;
        IsExpanded = expanded;
    }

    public string Name { get; }

    public IReadOnlyList<DiffRow> Rows { get; }

    public string Header => Text.Format(Strings.Diff_GroupHeader, Name, Rows.Count);

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public override string ToString() => Header;
}

/// <summary>The transfer page: choose a source, preview, apply, undo.</summary>
public sealed partial class TransferViewModel : ObservableObject
{
    private readonly MainViewModel main;
    private readonly IUiServices ui;
    private ApplyPlan? plan;
    private bool reloading;

    public TransferViewModel(MainViewModel main, IUiServices ui)
    {
        this.main = main;
        this.ui = ui;
    }

    public ObservableCollection<SourceOption> Sources { get; } = [];

    public ObservableCollection<RevisionOption> Revisions { get; } = [];

    public ObservableCollection<PlanRow> Rows { get; } = [];

    [ObservableProperty]
    public partial SourceOption? Source { get; set; }

    [ObservableProperty]
    public partial RevisionOption? Revision { get; set; }

    [ObservableProperty]
    public partial bool ShowSkipped { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Messages { get; set; } = string.Empty;

    /// <summary>Shown after a comparison when the chosen device has plugin repositories this one lacks.</summary>
    [ObservableProperty]
    public partial string RepositoryHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPlan { get; set; }

    /// <summary>The row whose differences are shown below the list.</summary>
    [ObservableProperty]
    public partial PlanRow? SelectedRow { get; set; }

    /// <summary>The differing settings of the selected file, by group.</summary>
    public ObservableCollection<DiffGroup> DiffGroups { get; } = [];

    /// <summary>The title above the differences; empty when nothing is shown.</summary>
    [ObservableProperty]
    public partial string DiffTitle { get; set; } = string.Empty;

    /// <summary>What the differences mean for this file: not readable, not selected, or a part stays here.</summary>
    [ObservableProperty]
    public partial string DiffNote { get; set; } = string.Empty;

    private int diffVersion;

    partial void OnSelectedRowChanged(PlanRow? value) => _ = ShowDifferencesAsync(value);

    /// <summary>
    /// Reads what differs inside the selected file (owner's request, 2026-11-06). The important
    /// settings, those with a readable name, come first and their groups are open; the rest is
    /// folded away under its group. Read-only.
    /// </summary>
    private async Task ShowDifferencesAsync(PlanRow? row)
    {
        var version = ++diffVersion;
        DiffGroups.Clear();
        DiffTitle = string.Empty;
        DiffNote = string.Empty;
        var current = plan;
        if (row is null || current is null || row.Item.Reason == PlanReason.OnlyHere)
        {
            return;
        }

        SettingsComparisonResult? result = null;
        var item = row.Item;
        await main.RunAsync(Strings.Status_ReadingDifferences, () => result = main.Engine.CompareSettings(current, item), showErrors: false).ConfigureAwait(true);
        if (version != diffVersion || result is null)
        {
            return;
        }

        DiffTitle = Text.Format(Strings.Diff_Title, row.AreaText, row.DisplayPath);
        var notes = new List<string>();
        if (!result.Readable)
        {
            notes.Add(Strings.Diff_Unreadable);
        }
        else if (result.Differences.Count == 0)
        {
            notes.Add(item.Action == PlanAction.Skip && item.LocalHash == item.SourceHash ? Strings.Diff_Same : Strings.Diff_None);
        }
        else
        {
            notes.Add(result.Differences.Count == 1 && !result.Truncated
                ? Strings.Diff_CountOne
                : Text.Format(Strings.Diff_Count, result.Differences.Count + (result.Truncated ? "+" : string.Empty)));
        }

        if (item.Rule == ApplyRule.Never && !current.IsOwnDevice)
        {
            notes.Add(Strings.Diff_NeverTaken);
        }
        else if (result.Differences.Any(d => d.StaysHere))
        {
            notes.Add(Strings.Diff_PartsStay);
        }

        var never = item.Rule == ApplyRule.Never && !current.IsOwnDevice;
        var rules = main.Engine.Rules;
        var choosable = result.Readable && !never && !current.IsOwnDevice && SettingParts.IsSplitFile(item.Root, item.Path)
            && rules.RuleFor(item.Root, item.Path) == rules.DefaultFor(item.Root, item.Path);
        if (choosable && result.Differences.Count > 0)
        {
            notes.Add(Strings.Diff_ChoiceHint);
        }

        DiffNote = string.Join(" ", notes);
        var groups = result.Differences
            .Select(d => (Group: d.GroupKey is not null ? Text.Get(d.GroupKey) : d.GroupText.Length > 0 ? d.GroupText : Strings.Diff_GroupGeneral, Row: ToRow(d, never, choosable ? (rules.SettingChoice(item.Path, d.Setting), (Action<DiffRow, int>)((r, c) => Choose(item, r, c))) : null)))
            .GroupBy(x => x.Group, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Rows: g.Select(x => x.Row).OrderByDescending(r => r.IsKnown).ToList()))
            .OrderByDescending(g => g.Rows.Any(r => r.IsKnown))
            .ToList();
        var openAll = result.Differences.Count <= 12;
        for (var i = 0; i < groups.Count; i++)
        {
            DiffGroups.Add(new DiffGroup(groups[i].Name, groups[i].Rows, openAll || i == 0 || groups[i].Rows.Any(r => r.IsKnown)));
        }

        if (DiffGroups.Any(g => !g.IsExpanded))
        {
            DiffNote += " " + Strings.Diff_Folded;
        }
    }

    private static DiffRow ToRow(SettingDifference difference, bool never, (bool? Current, Action<DiffRow, int> Choose)? choice)
    {
        var label = Strings.ResourceManager.GetString("Setting_" + difference.Setting, System.Globalization.CultureInfo.CurrentUICulture);
        var outcome = never ? Strings.Diff_OutcomeNever : difference.StaysHere ? Strings.Diff_OutcomeStays : Strings.Diff_OutcomeTaken;
        return new DiffRow(
            label ?? difference.Setting,
            label is null ? string.Empty : difference.Setting,
            difference.Here ?? Strings.Diff_Missing,
            difference.There ?? Strings.Diff_Missing,
            outcome,
            label is not null,
            difference.Setting,
            choice?.Current switch { true => 1, false => 2, null => 0 },
            choice?.Choose);
    }

    /// <summary>
    /// Stores the choice for one setting (owner's request, 2026-11-06: take only single settings
    /// of a file) and compares again, so the list and the differences show what is now taken.
    /// The choice applies to every later transfer, the automatic one too.
    /// </summary>
    private void Choose(PlanItem item, DiffRow row, int choice) => _ = ChooseAsync(item, row.Key, choice);

    private async Task ChooseAsync(PlanItem item, string setting, int choice)
    {
        bool? taken = choice switch { 1 => true, 2 => false, _ => null };
        if (!await main.RunAsync(Strings.Rules_Title, () => main.Engine.UpdateRules(main.Engine.Rules.WithSetting(item.Path, setting, taken))).ConfigureAwait(true))
        {
            return;
        }

        var title = DiffTitle;
        await CompareAsync().ConfigureAwait(true);
        SelectedRow = Rows.FirstOrDefault(r => r.Item.Root == item.Root && string.Equals(r.Item.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (SelectedRow is null)
        {
            // Everything that differed now stays here: the file has nothing left to take.
            DiffGroups.Clear();
            DiffTitle = title;
            DiffNote = Strings.Diff_NothingLeft;
        }
    }

    /// <summary>Fills the list of devices.</summary>
    public void LoadSources()
    {
        // Updated in place: a refresh (after a game exit, a backup, a save) must not clear the
        // comparison, the ticks or the chosen backup the user is working with (review 3, finding 2).
        var wanted = main.Devices.Select(d => new SourceOption(d.DeviceId, d.Name)).ToList();
        if (wanted.SequenceEqual(Sources))
        {
            return;
        }

        var current = Source?.DeviceId;
        reloading = true;
        try
        {
            Sources.Clear();
            foreach (var option in wanted)
            {
                Sources.Add(option);
            }

            var keep = Sources.FirstOrDefault(s => s.DeviceId == current);
            Source = keep
                ?? Sources.FirstOrDefault(s => !main.Devices.First(d => d.DeviceId == s.DeviceId).IsThisDevice)
                ?? Sources.FirstOrDefault();
        }
        finally
        {
            reloading = false;
        }

        if (Source?.DeviceId != current)
        {
            ResetForSource(Source);
        }
    }

    /// <summary>Selects a source device by ID.</summary>
    public void SelectSource(string deviceId) => Source = Sources.FirstOrDefault(s => s.DeviceId == deviceId) ?? Source;

    [RelayCommand]
    public async Task CompareAsync()
    {
        if (Source is null)
        {
            return;
        }

        ApplyPlan? built = null;
        var deviceId = Source.DeviceId;
        var revisionId = Revision?.RevisionId;
        IReadOnlyList<PluginRepository> missing = [];
        if (await main.RunAsync(Strings.Status_Comparing, () =>
        {
            built = main.Engine.Plan(deviceId, revisionId);
            missing = MissingRepositoriesOrNone(deviceId);
        }).ConfigureAwait(true))
        {
            plan = built;
            Fill();
            RepositoryHint = missing.Count > 0 ? Text.Format(Strings.Transfer_RepositoriesHint, missing.Count) : string.Empty;
        }
    }

    /// <summary>
    /// Adds the plugin repositories of the chosen device that this device lacks to Dalamud's
    /// settings here, after the user confirmed the list. Nothing else in the file changes.
    /// </summary>
    [RelayCommand]
    public async Task MergeRepositoriesAsync()
    {
        if (Source is null)
        {
            return;
        }

        var source = Source;
        IReadOnlyList<PluginRepository> missing = [];
        if (!await main.RunAsync(Strings.Status_Comparing, () => missing = main.Engine.MissingRepositories(source.DeviceId)).ConfigureAwait(true))
        {
            return;
        }

        if (missing.Count == 0)
        {
            RepositoryHint = string.Empty;
            ui.Inform(Strings.Transfer_Title, Text.Format(Strings.Transfer_RepositoriesNone, source.Name));
            return;
        }

        var list = Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(r => "  " + r.Url))
            + Environment.NewLine + Environment.NewLine;
        if (!ui.Confirm(Strings.Transfer_Title, Text.Format(Strings.Transfer_RepositoriesConfirm, missing.Count, source.Name, list)))
        {
            return;
        }

        IReadOnlyList<PluginRepository> added = [];
        if (await main.RunAsync(Strings.Status_Merging, () => added = main.Engine.MergeRepositories(source.DeviceId)).ConfigureAwait(true))
        {
            RepositoryHint = string.Empty;
            ui.Inform(Strings.Transfer_Title, Text.Format(Strings.Transfer_RepositoriesDone, added.Count));
            await main.RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>The hint after a comparison: quiet when either side has no Dalamud settings to compare.</summary>
    private IReadOnlyList<PluginRepository> MissingRepositoriesOrNone(string deviceId)
    {
        try
        {
            return main.Engine.MissingRepositories(deviceId);
        }
        catch (RefusedException)
        {
            return [];
        }
    }

    [RelayCommand]
    public async Task ApplyAsync()
    {
        if (plan is null)
        {
            return;
        }

        // The plan shown when the user clicked; a refresh while the dialog is open must not swap it.
        var current = plan;
        var selected = current.Selected.ToList();
        if (selected.Count == 0)
        {
            ui.Inform(Strings.Transfer_Title, Strings.Refuse_NothingSelected);
            return;
        }

        var message = Text.Format(Strings.Transfer_ConfirmText, selected.Count);
        if (current.IsOwnDevice)
        {
            message += Environment.NewLine + Environment.NewLine + Strings.Transfer_ConfirmOwn;
        }

        if (selected.Any(i => i.Rule == ApplyRule.Never || i.Flags.Contains(FileFlags.Credential)))
        {
            message += Environment.NewLine + Environment.NewLine + Strings.Transfer_ConfirmSensitive;
        }

        if (!ui.Confirm(Strings.Transfer_Title, message))
        {
            return;
        }

        ApplyResult? result = null;
        if (await main.RunAsync(Strings.Status_Applying, () => result = main.Engine.Apply(current)).ConfigureAwait(true))
        {
            ui.Inform(Strings.Transfer_Title, Text.Format(Strings.Transfer_Done, result!.Applied));
            await main.RefreshAsync().ConfigureAwait(true);
            await CompareAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task UndoLastAsync()
    {
        Apply.SnapshotManifest? last = null;
        IReadOnlyList<string> changed = [];
        if (!await main.RunAsync(Strings.Status_Comparing, () =>
        {
            var snapshots = main.Engine.ListSnapshots();
            last = snapshots.Count > 0 ? snapshots[0] : null;
            changed = last is null ? [] : main.Engine.ChangedSinceApply(last);
        }).ConfigureAwait(true))
        {
            return;
        }

        if (last is null)
        {
            ui.Inform(Strings.Transfer_Title, Strings.Transfer_NothingToUndo);
            return;
        }

        var question = Text.Format(Strings.Transfer_UndoConfirm, Text.Time(last.Created), last.Files.Count);
        if (changed.Count > 0)
        {
            question += Environment.NewLine + Environment.NewLine + Text.Format(Strings.Transfer_UndoChanged, changed.Count);
        }

        if (!ui.Confirm(Strings.Transfer_Title, question))
        {
            return;
        }

        var undo = last;
        if (await main.RunAsync(Strings.Status_Applying, () => main.Engine.Undo(undo.SnapshotId)).ConfigureAwait(true))
        {
            ui.Inform(Strings.Transfer_Title, Strings.Transfer_Undone);
            await main.RefreshAsync().ConfigureAwait(true);
            if (HasPlan)
            {
                await CompareAsync().ConfigureAwait(true);
            }
        }
    }

    [RelayCommand]
    public void SelectAllConflicts()
    {
        foreach (var row in Rows.Where(r => r.IsConflict))
        {
            row.IsSelected = true;
        }

        UpdateSummary();
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }

        UpdateSummary();
    }

    /// <summary>Recomputes the counts after a check box changed.</summary>
    public void UpdateSummary()
    {
        if (plan is null)
        {
            Summary = string.Empty;
            return;
        }

        Summary = Text.Format(
            Strings.Transfer_Summary,
            plan.Selected.Count(),
            plan.Items.Count(i => i.Action == PlanAction.Conflict),
            plan.Items.Count(i => i.Action == PlanAction.Offer),
            plan.Items.Count(i => i.Action == PlanAction.Skip));
    }

    partial void OnSourceChanged(SourceOption? value)
    {
        if (!reloading)
        {
            ResetForSource(value);
        }
    }

    private void ResetForSource(SourceOption? value)
    {
        RepositoryHint = string.Empty;
        Revisions.Clear();
        plan = null;
        Rows.Clear();
        HasPlan = false;
        Summary = string.Empty;
        Messages = string.Empty;
        if (value is null)
        {
            return;
        }

        Revisions.Add(new RevisionOption(null, Strings.Transfer_Latest));
        try
        {
            foreach (var (id, created, origin, files) in main.Engine.ListRevisions(value.DeviceId))
            {
                Revisions.Add(new RevisionOption(id, Text.Format(Strings.Transfer_RevisionLabel, Text.Time(created), Text.Get("Origin_" + origin), files)));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or RefusedException)
        {
            Messages = Text.ForError(ex);
        }

        Revision = Revisions[0];
    }

    partial void OnShowSkippedChanged(bool value) => Fill();

    private void Fill()
    {
        Rows.Clear();
        HasPlan = plan is not null;
        if (plan is null)
        {
            return;
        }

        var names = main.Engine.Settings.CharacterNames;
        foreach (var item in plan.Items
            .Concat(ShowSkipped ? plan.NotInSource.Select(OnlyHereItem) : [])
            .Where(i => ShowSkipped || i.Action != PlanAction.Skip)
            .OrderBy(i => i.Action switch { PlanAction.Conflict => 0, PlanAction.Apply => 1, PlanAction.Offer => 2, _ => 3 })
            .ThenBy(i => i.Root, StringComparer.Ordinal)
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
        {
            var row = new PlanRow(item, DisplayPath(item.Root, item.Path, names));
            row.PropertyChanged += (_, _) => UpdateSummary();
            Rows.Add(row);
        }

        var messages = plan.Refusals.Select(Text.Get).Concat(plan.Warnings.Select(Text.Get)).ToList();
        if (plan.NotInSource.Count > 0)
        {
            messages.Add(Text.Format(Strings.Transfer_NotInSource, plan.NotInSource.Count));
        }

        Messages = string.Join(Environment.NewLine, messages);
        UpdateSummary();
    }

    /// <summary>
    /// A row for a file this device has and the source does not (owner's request, 2026-11-05: show
    /// which files these are). For display only: it can never be selected, and nothing is done
    /// with it.
    /// </summary>
    private PlanItem OnlyHereItem(string key)
    {
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        var root = key[..slash];
        var path = key[(slash + 1)..];
        return new PlanItem
        {
            Root = root,
            Path = path,
            SourceHash = string.Empty,
            SourceSize = 0,
            Rule = main.Engine.Rules.RuleFor(root, path),
            Matrix = FileMatrix.Lookup(root, path),
            Action = PlanAction.Skip,
            Reason = PlanReason.OnlyHere,
        };
    }

    /// <summary>A path for display: the character folder is replaced by the name the user gave it (X-06).</summary>
    public static string DisplayPath(string root, string path, IReadOnlyDictionary<string, string> names)
    {
        if (root != Roots.Game)
        {
            return path;
        }

        var slash = path.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0 && path.StartsWith("FFXIV_CHR", StringComparison.OrdinalIgnoreCase))
        {
            var folder = path[..slash];
            var name = names.TryGetValue(folder, out var given) ? given : Strings.Character_Unnamed;
            return Text.Format(Strings.Character_Path, name, path[(slash + 1)..]);
        }

        return path;
    }
}
