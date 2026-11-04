using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Logging;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Core.ViewModels;

/// <summary>One device in the overview.</summary>
public sealed partial class DeviceRow : ObservableObject
{
    public required string DeviceId { get; init; }

    public required string Name { get; init; }

    public required bool IsThisDevice { get; init; }

    public required string LastBackup { get; init; }

    public required string Receipt { get; init; }
}

/// <summary>The main window: navigation, the overview and the work that runs on its own.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IUiServices ui;
    private readonly Log log;
    private readonly IGameMonitor game;
    private readonly SemaphoreSlim work = new(1, 1);
    private readonly Timer periodic;
    private bool disposed;
    private int sequences;

    public MainViewModel(SyncEngine engine, IUiServices ui, Log log, IGameMonitor game, IShellIntegration shell, IGameLocator locator)
    {
        Engine = engine;
        this.ui = ui;
        this.log = log;
        this.game = game;
        Transfer = new TransferViewModel(this, ui);
        Rules = new RulesViewModel(this);
        Settings = new SettingsViewModel(this, ui, shell, locator);
        LogPage = new LogViewModel(log, ui);
        game.RunningChanged += OnGameRunningChanged;
        GameRunning = game.IsRunning;
        SelectedPage = 0;
        periodic = new Timer(_ => _ = PeriodicAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>Raised when the user wants to choose another sync folder; the application runs the set-up again.</summary>
    public event EventHandler? SetupRequested;

    public SyncEngine Engine { get; }

    public TransferViewModel Transfer { get; }

    public RulesViewModel Rules { get; }

    public SettingsViewModel Settings { get; }

    public LogViewModel LogPage { get; }

    public ObservableCollection<DeviceRow> Devices { get; } = [];

    [ObservableProperty]
    public partial int SelectedPage { get; set; }

    [ObservableProperty]
    public partial bool Busy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = Strings.Status_Ready;

    [ObservableProperty]
    public partial bool GameRunning { get; set; }

    [ObservableProperty]
    public partial string ThisDeviceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastBackupText { get; set; } = Strings.Common_Never;

    [ObservableProperty]
    public partial string UploadText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceiptText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncFolderText { get; set; } = string.Empty;

    public string GameStateText => GameRunning ? Strings.Overview_GameRunning : Strings.Overview_GameNotRunning;

    /// <summary>Runs one operation at a time, off the UI thread, and turns failures into messages.</summary>
    public async Task<bool> RunAsync(string busyText, Action operation, bool showErrors = true)
    {
        if (disposed)
        {
            // A continuation after the window model was replaced (review finding 10).
            return false;
        }

        await work.WaitAsync().ConfigureAwait(true);
        if (disposed)
        {
            work.Release();
            return false;
        }

        Busy = true;
        StatusText = busyText;
        try
        {
            await Task.Run(operation).ConfigureAwait(true);
            StatusText = Strings.Status_Ready;
            return true;
        }
        catch (Exception ex) when (ex is RefusedException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            log.Warning("ui", $"{busyText}: {ex.GetType().Name}: {ex.Message}");
            StatusText = Text.ForError(ex);
            if (showErrors)
            {
                ui.ShowError(Text.ForError(ex));
            }

            return false;
        }
        finally
        {
            Busy = false;
            work.Release();
        }
    }

    /// <summary>Called once after the window opened.</summary>
    public async Task StartAsync()
    {
        await AskRecoveryAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        if (!game.IsRunning)
        {
            await BackupAndPullAsync(RevisionOrigin.AppStart, quiet: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        List<DeviceStatus> devices = [];
        CloudFileState upload = CloudFileState.Unknown;
        int conflictCopies = 0;
        var ok = await RunAsync(Strings.Status_Refreshing, () =>
        {
            devices = [.. Engine.GetDevices()];
            upload = Engine.GetOwnUploadState();
            conflictCopies = Engine.ScanOnly().ConflictCopies;
        }, showErrors: false).ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        Devices.Clear();
        foreach (var device in devices)
        {
            Devices.Add(new DeviceRow
            {
                DeviceId = device.Info.DeviceId,
                Name = device.IsThisDevice ? device.Info.DisplayName + " " + Strings.Transfer_ThisDevice : device.Info.DisplayName,
                IsThisDevice = device.IsThisDevice,
                LastBackup = Text.Time(device.HeadCreated),
                Receipt = device.ReceivedBy.Count > 0 ? Text.Format(Strings.Overview_ReceivedBy, string.Join(", ", device.ReceivedBy)) : Strings.Overview_NotReceived,
            });
        }

        var self = devices.FirstOrDefault(d => d.IsThisDevice);
        ThisDeviceName = Engine.Settings.DisplayName;
        SyncFolderText = Engine.Settings.SyncFolder ?? string.Empty;
        LastBackupText = Text.Time(self?.HeadCreated);
        ReceiptText = self is null || self.ReceivedBy.Count == 0 ? Strings.Overview_NotReceived : Text.Format(Strings.Overview_ReceivedBy, string.Join(", ", self.ReceivedBy));
        UploadText = upload switch
        {
            CloudFileState.InSync => Strings.Upload_InSync,
            CloudFileState.Pending => Strings.Upload_Pending,
            _ => Strings.Upload_Unknown,
        };
        Notice = conflictCopies > 0 ? Text.Format(Strings.Overview_ConflictCopies, conflictCopies) : string.Empty;
        Transfer.LoadSources();
    }

    [RelayCommand]
    public async Task BackupNowAsync()
    {
        BackupResult? result = null;
        if (await RunAsync(Strings.Status_BackingUp, () => result = Engine.Backup(RevisionOrigin.Manual)).ConfigureAwait(true))
        {
            StatusText = result!.Unchanged ? Strings.Backup_Unchanged : Text.Format(Strings.Backup_Done, result.Files, result.NewObjects);
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Opens the transfer page for a device.</summary>
    [RelayCommand]
    public void ShowTransfer(DeviceRow? row)
    {
        SelectedPage = 1;
        if (row is not null)
        {
            Transfer.SelectSource(row.DeviceId);
        }
    }

    /// <summary>Called when a sync file is opened while the application runs.</summary>
    public void OpenSyncFile(string path)
    {
        var folder = Store.SyncStore.FolderOf(path);
        if (!string.Equals(Path.GetFullPath(folder), Path.GetFullPath(Engine.Settings.SyncFolder ?? string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            ui.Inform(Strings.App_Title, Text.Format(Strings.SyncFile_OtherFolder, folder, Engine.Settings.SyncFolder));
        }

        SelectedPage = 0;
        _ = RefreshAsync();
    }

    /// <summary>Asks whether to finish or roll back an interrupted apply, if there is one.</summary>
    public async Task AskRecoveryAsync()
    {
        if (!Engine.HasInterruptedApply || game.IsRunning)
        {
            return;
        }

        var finish = ui.Ask(Strings.Recovery_Title, Strings.Recovery_Text);
        if (finish is null)
        {
            return;
        }

        IReadOnlyList<string> leftAlone = [];
        if (await RunAsync(Strings.Status_Recovering, () => leftAlone = Engine.ResolveInterruptedApply(rollBack: finish == false)).ConfigureAwait(true)
            && leftAlone.Count > 0)
        {
            ui.Inform(Strings.Recovery_Title, Text.Format(Strings.Recovery_LeftAlone, leftAlone.Count));
        }
    }

    /// <summary>Clears the sync folder and asks the application to run the set-up again.</summary>
    public void RequestSetup()
    {
        if (Busy || sequences > 0)
        {
            ui.Inform(Strings.App_Title, Strings.Close_Busy);
            return;
        }

        Engine.Settings.SyncFolder = null;
        Engine.SaveSettings();
        SetupRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        game.RunningChanged -= OnGameRunningChanged;
        periodic.Dispose();
        LogPage.Dispose();

        // The semaphore is not disposed: a continuation that is still pending may wait on it, and
        // RunAsync returns at once once disposed is set.
    }

    partial void OnGameRunningChanged(bool value) => OnPropertyChanged(nameof(GameStateText));

    /// <summary>Backs up this device, then takes what the rules allow from the others.</summary>
    private async Task BackupAndPullAsync(RevisionOrigin origin, bool quiet, IReadOnlyList<DeviceInfo>? alreadyFound = null)
    {
        sequences++;
        try
        {
            await BackupAndPullCoreAsync(origin, quiet, alreadyFound).ConfigureAwait(true);
        }
        finally
        {
            sequences--;
        }
    }

    private async Task BackupAndPullCoreAsync(RevisionOrigin origin, bool quiet, IReadOnlyList<DeviceInfo>? alreadyFound)
    {
        await AskRecoveryAsync().ConfigureAwait(true);
        if (Engine.Settings.BackupOnGameExit != false || origin != RevisionOrigin.GameExit)
        {
            await RunAsync(Strings.Status_BackingUp, () => Engine.Backup(origin), showErrors: !quiet).ConfigureAwait(true);
        }

        List<DeviceInfo> fresh = [.. alreadyFound ?? []];
        await RunAsync(Strings.Status_Refreshing, () => fresh.AddRange(Engine.FindNewRevisions()), showErrors: false).ConfigureAwait(true);
        foreach (var device in fresh.DistinctBy(d => d.DeviceId).ToList())
        {
            if (Engine.Settings.AutoApplyAlways == true && !game.IsRunning)
            {
                (int Applied, int Waiting) result = (0, 0);
                if (await RunAsync(Strings.Status_Applying, () => result = Engine.AutoApply(device.DeviceId), showErrors: false).ConfigureAwait(true))
                {
                    if (result.Applied > 0)
                    {
                        ui.Notify(Strings.App_Title, Text.Format(Strings.Notify_Applied, result.Applied, device.DisplayName));
                    }

                    if (result.Waiting > 0)
                    {
                        ui.Notify(Strings.App_Title, Text.Format(Strings.Notify_Conflicts, result.Waiting, device.DisplayName));
                    }
                }
            }
            else
            {
                ui.Notify(Strings.App_Title, Text.Format(Strings.Overview_NewFrom, device.DisplayName));
            }
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnGameRunningChanged(object? sender, bool running)
    {
        ui.OnUiThread(async () =>
        {
            try
            {
                GameRunning = running;
                if (!running)
                {
                    // The game writes its files while it shuts down; give it time to finish (V00).
                    await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                    if (!disposed && !game.IsRunning)
                    {
                        await BackupAndPullAsync(RevisionOrigin.GameExit, quiet: true).ConfigureAwait(true);
                    }
                }
            }
            catch (Exception ex)
            {
                // An async void path: nothing may escape to the dispatcher (review 2, finding 11).
                log.Error("game-exit", ex.ToString());
            }
        });
    }

    private async Task PeriodicAsync()
    {
        if (disposed || game.IsRunning || Busy || sequences > 0)
        {
            return;
        }

        var tcs = new TaskCompletionSource();
        var posted = ui.OnUiThread(async () =>
        {
            try
            {
                // Checked again on the UI thread, where the flags are set (review 2, finding 12).
                if (disposed || game.IsRunning || Busy || sequences > 0)
                {
                    return;
                }

                List<DeviceInfo> fresh = [];
                await RunAsync(Strings.Status_Refreshing, () => fresh = [.. Engine.FindNewRevisions()], showErrors: false).ConfigureAwait(true);
                if (fresh.Count > 0)
                {
                    await BackupAndPullAsync(RevisionOrigin.Manual, quiet: true, fresh).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                log.Error("periodic", ex.ToString());
            }
            finally
            {
                tcs.TrySetResult();
            }
        });
        if (!posted)
        {
            return;
        }

        await tcs.Task.ConfigureAwait(false);
    }
}
