using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.ViewModels;

/// <summary>A device to take settings from.</summary>
public sealed record SourceOption(string DeviceId, string Name);

/// <summary>A revision of the chosen device.</summary>
public sealed record RevisionOption(string? RevisionId, string Label);

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

    public string Root => Item.Root == Roots.Game ? Strings.Root_Game : Strings.Root_Plugins;

    public string ActionText => Item.Action switch
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

    [ObservableProperty]
    public partial bool HasPlan { get; set; }

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
        if (await main.RunAsync(Strings.Status_Comparing, () => built = main.Engine.Plan(deviceId, revisionId)).ConfigureAwait(true))
        {
            plan = built;
            Fill();
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
