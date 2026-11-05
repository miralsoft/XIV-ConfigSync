using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Inventory;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Core.ViewModels;

/// <summary>One conflict copy on the clean-up page.</summary>
public sealed partial class CleanupRow : ObservableObject
{
    private readonly Action changed;

    public CleanupRow(ConflictCopy copy, Action changed)
    {
        Copy = copy;
        this.changed = changed;
    }

    public ConflictCopy Copy { get; }

    public string DisplayPath => Copy.Path;

    public string Original => Copy.Original;

    public string SizeText => Copy.Size < 1024
        ? Text.Format(Strings.Cleanup_Bytes, Copy.Size)
        : Text.Format(Strings.Cleanup_KiB, (Copy.Size / 1024.0).ToString("0.#", CultureInfo.CurrentCulture));

    public string ModifiedText => Text.Time(Copy.Modified);

    public string Note => Copy.Removable ? string.Empty : Strings.Cleanup_OriginalMissing;

    public bool Selectable => Copy.Removable;

    /// <summary>Never ticked by the application: only the user chooses what goes.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !Selectable)
        {
            IsSelected = false;
            return;
        }

        changed();
    }
}

/// <summary>
/// The clean-up page: lists the conflict copies a cloud client left in the game folder and moves
/// the ones the user ticks to the Recycle Bin, only when the user asks for it and confirms.
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly MainViewModel main;
    private readonly IUiServices ui;
    private readonly IRecycleBin bin;

    public CleanupViewModel(MainViewModel main, IUiServices ui, IRecycleBin bin)
    {
        this.main = main;
        this.ui = ui;
        this.bin = bin;
    }

    public ObservableCollection<CleanupRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRows { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    /// <summary>Lists the conflict copies again. Ticks of files still listed are kept.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IReadOnlyList<ConflictCopy> found = [];
        if (!await main.RunAsync(Strings.Status_Refreshing, () => found = main.Engine.ListConflictCopies()).ConfigureAwait(true))
        {
            return;
        }

        var ticked = Rows.Where(r => r.IsSelected).Select(r => r.Copy).ToHashSet();
        Rows.Clear();
        foreach (var copy in found)
        {
            var row = new CleanupRow(copy, UpdateSummary);
            if (ticked.Contains(copy))
            {
                row.IsSelected = true;
            }

            Rows.Add(row);
        }

        UpdateSummary();
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var row in Rows.Where(r => r.Selectable))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>Moves the ticked files to the Recycle Bin after the user confirms.</summary>
    [RelayCommand]
    public async Task RemoveSelectedAsync()
    {
        var chosen = Rows.Where(r => r.IsSelected && r.Selectable).Select(r => r.Copy).ToList();
        if (chosen.Count == 0)
        {
            ui.Inform(Strings.Cleanup_Title, Strings.Refuse_NothingSelected);
            return;
        }

        if (!ui.Confirm(Strings.Cleanup_Title, Text.Format(Strings.Cleanup_Confirm, chosen.Count)))
        {
            return;
        }

        IReadOnlyList<string> moved = [];
        IReadOnlyList<(string Path, string Reason)> left = [];
        if (!await main.RunAsync(Strings.Status_Cleaning, () => (moved, left) = main.Engine.RemoveConflictCopies(chosen, bin)).ConfigureAwait(true))
        {
            return;
        }

        var message = Text.Format(Strings.Cleanup_Done, moved.Count);
        if (left.Count > 0)
        {
            message += Environment.NewLine + Environment.NewLine + Strings.Cleanup_NotMoved + Environment.NewLine
                + string.Join(Environment.NewLine, left.Select(l => $"{l.Path}: {l.Reason}"));
        }

        ui.Inform(Strings.Cleanup_Title, message);
        await LoadAsync().ConfigureAwait(true);
        await main.RefreshAsync().ConfigureAwait(true);
    }

    private void UpdateSummary()
    {
        HasRows = Rows.Count > 0;
        var selected = Rows.Count(r => r.IsSelected);
        HasSelection = selected > 0;
        Summary = Rows.Count == 0
            ? Strings.Cleanup_None
            : Text.Format(Strings.Cleanup_Summary, Rows.Count, selected, Rows.Count(r => !r.Selectable));
    }
}
