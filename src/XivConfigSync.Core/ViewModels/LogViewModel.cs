using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Logging;

namespace XivConfigSync.Core.ViewModels;

/// <summary>One line on the log page.</summary>
public sealed record LogRow(string Time, string Level, string Message);

/// <summary>The log page.</summary>
public sealed partial class LogViewModel
{
    private readonly Log log;
    private readonly IUiServices ui;

    public LogViewModel(Log log, IUiServices ui)
    {
        this.log = log;
        this.ui = ui;
        foreach (var entry in log.Recent)
        {
            Rows.Insert(0, ToRow(entry));
        }

        log.Written += (_, entry) => ui.OnUiThread(() =>
        {
            Rows.Insert(0, ToRow(entry));
            while (Rows.Count > 500)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }
        });
    }

    public ObservableCollection<LogRow> Rows { get; } = [];

    /// <summary>The folder with the log files.</summary>
    public string? Folder { get; set; }

    [RelayCommand]
    public void OpenFolder()
    {
        if (Folder is not null)
        {
            ui.OpenFolder(Folder);
        }
    }

    private static LogRow ToRow(LogEntry entry) =>
        new(entry.Time.ToString("G", CultureInfo.CurrentCulture), Text.Get("Level_" + entry.Level), entry.Message);
}
