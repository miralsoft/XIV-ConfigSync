using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Discovery;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Platform;
using XivConfigSync.Core.Store;

namespace XivConfigSync.Core.ViewModels;

/// <summary>A device already in the chosen sync folder.</summary>
public sealed record ExistingDevice(string DeviceId, string Name, DateTimeOffset LastSeen);

/// <summary>
/// The first-start wizard (FR-003, FR-045): one sync folder for every device; the application
/// decides whether it makes a new one or joins an existing one.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly SyncEngine engine;
    private readonly IUiServices ui;
    private readonly IShellIntegration shell;

    public SetupViewModel(SyncEngine engine, IUiServices ui, IShellIntegration shell, IGameLocator locator, string? initialPath)
    {
        this.engine = engine;
        this.ui = ui;
        this.shell = shell;
        // Values the user chose before (for example when changing the sync folder) win over detection.
        var known = engine.Settings;
        DeviceName = string.IsNullOrWhiteSpace(known.DisplayName) ? Environment.MachineName : known.DisplayName;
        GameFolder = known.GameConfigFolder
            ?? (GameFolders.LooksLikeGameConfigFolder(GameFolders.DefaultGameConfigFolder) ? GameFolders.DefaultGameConfigFolder : string.Empty);
        PluginFolder = known.PluginConfigFolder ?? GameFolders.FindPluginConfigFolder() ?? string.Empty;
        BackUpPlugins = PluginFolder.Length > 0 && known.BackUpPlugins;
        InstallFolder = known.GameInstallFolder ?? GameFolders.PickInstallFolder(locator.InstallFolderCandidates()) ?? string.Empty;
        BackupOnGameExit = known.BackupOnGameExit ?? true;
        AutoApply = known.AutoApplyAlways ?? true;
        FileAssociation = known.FileAssociation ?? true;
        if (initialPath is not null)
        {
            Folder = SyncStore.FolderOf(initialPath);
        }
        else if (!string.IsNullOrWhiteSpace(known.SyncFolder))
        {
            // Changing the sync folder, or one that was not reachable: start from the one in use.
            Folder = known.SyncFolder;
        }
        else
        {
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveCommercial");
            Folder = oneDrive is null ? string.Empty : Path.Combine(oneDrive, "FFXIV Sync");
        }
    }

    /// <summary>Raised when the set-up is complete.</summary>
    public event EventHandler? Completed;

    public ObservableCollection<ExistingDevice> ExistingDevices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderStep), nameof(IsDeviceStep), nameof(IsFoldersStep), nameof(IsOptionsStep), nameof(CanGoBack), nameof(NextText))]
    public partial int Step { get; set; }

    [ObservableProperty]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FolderState { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool FolderUsable { get; set; }

    [ObservableProperty]
    public partial bool JoinsExisting { get; set; }

    [ObservableProperty]
    public partial bool SetUpAgain { get; set; }

    [ObservableProperty]
    public partial ExistingDevice? AgainAs { get; set; }

    [ObservableProperty]
    public partial string DeviceName { get; set; }

    [ObservableProperty]
    public partial string GameFolder { get; set; }

    [ObservableProperty]
    public partial string PluginFolder { get; set; }

    [ObservableProperty]
    public partial string InstallFolder { get; set; }

    [ObservableProperty]
    public partial bool BackUpPlugins { get; set; }

    [ObservableProperty]
    public partial bool BackupOnGameExit { get; set; } = true;

    [ObservableProperty]
    public partial bool AutoApply { get; set; } = true;

    [ObservableProperty]
    public partial bool Autostart { get; set; }

    [ObservableProperty]
    public partial bool FileAssociation { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    public partial bool Working { get; set; }

    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    public bool IsFolderStep => Step == 0;

    public bool IsDeviceStep => Step == 1;

    public bool IsFoldersStep => Step == 2;

    public bool IsOptionsStep => Step == 3;

    public bool CanGoBack => Step > 0 && !Working;

    public string NextText => Step == 3 ? Strings.Setup_Finish : Strings.Setup_Next;

    public string GameFolderState => GameFolders.LooksLikeGameConfigFolder(GameFolder) ? Strings.Setup_GameFolderFound : Strings.Setup_GameFolderMissing;

    public string GameVersionText => GameFolders.ReadGameVersion(InstallFolder) is { } version ? Text.Format(Strings.Setup_GameVersion, version) : Strings.Setup_GameVersionUnknown;

    [RelayCommand]
    public void BrowseFolder()
    {
        var chosen = ui.PickFolder(Strings.Setup_ChooseFolder, Folder);
        if (chosen is not null)
        {
            Folder = chosen;
        }
    }

    [RelayCommand]
    public void BrowseGameFolder()
    {
        var chosen = ui.PickFolder(Strings.Settings_GameFolder, GameFolder);
        if (chosen is not null)
        {
            GameFolder = chosen;
        }
    }

    [RelayCommand]
    public void BrowsePluginFolder()
    {
        var chosen = ui.PickFolder(Strings.Settings_PluginFolder, PluginFolder);
        if (chosen is not null)
        {
            PluginFolder = chosen;
        }
    }

    [RelayCommand]
    public void BrowseInstallFolder()
    {
        var chosen = ui.PickFolder(Strings.Settings_InstallFolder, InstallFolder);
        if (chosen is not null)
        {
            InstallFolder = chosen;
        }
    }

    [RelayCommand]
    public void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        switch (Step)
        {
            case 0:
                // Inspected again with the text as it is now (review 3, finding 10).
                InspectFolder();
                if (FolderUsable)
                {
                    Step = 1;
                }

                break;
            case 1 when !string.IsNullOrWhiteSpace(DeviceName) && (!SetUpAgain || AgainAs is not null):
                // Two PCs under one identity would disturb each other (review finding 9).
                if (SetUpAgain && AgainAs is not null && DateTimeOffset.UtcNow - AgainAs.LastSeen < TimeSpan.FromDays(1)
                    && !ui.Confirm(Strings.Setup_Title, Text.Format(Strings.Setup_AgainRecent, AgainAs.Name, Text.Time(AgainAs.LastSeen))))
                {
                    break;
                }

                Step = 2;
                break;
            case 2 when GameFolders.LooksLikeGameConfigFolder(GameFolder):
                Step = 3;
                break;
            case 2:
                ui.Inform(Strings.Setup_Title, Strings.Setup_GameFolderMissing);
                break;
            case 3:
                await FinishAsync().ConfigureAwait(true);
                break;
        }
    }

    partial void OnFolderChanged(string value) => InspectFolder();

    partial void OnGameFolderChanged(string value) => OnPropertyChanged(nameof(GameFolderState));

    partial void OnInstallFolderChanged(string value) => OnPropertyChanged(nameof(GameVersionText));

    partial void OnAgainAsChanged(ExistingDevice? value)
    {
        if (value is not null)
        {
            DeviceName = value.Name;
        }
    }

    /// <summary>Whether this device is already set up in that sync folder.</summary>
    private bool AlreadyJoined(string folder)
    {
        var settings = engine.Settings;
        if (string.IsNullOrWhiteSpace(settings.SyncFolder) || !SyncStore.IsDeviceId(settings.DeviceId)
            || !string.Equals(Path.GetFullPath(settings.SyncFolder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
            || SyncStore.Inspect(folder) != SyncStore.FolderKind.SyncFolder)
        {
            return false;
        }

        return SyncStore.Open(folder).ListDevices().Any(d => d.DeviceId == settings.DeviceId);
    }

    private void InspectFolder()
    {
        ExistingDevices.Clear();
        JoinsExisting = false;
        SetUpAgain = false;
        if (string.IsNullOrWhiteSpace(Folder))
        {
            FolderUsable = false;
            FolderState = string.Empty;
            return;
        }

        // A path to the sync file stands for its folder (a double-click hands over the file).
        var typed = Folder.Trim().Trim('"');
        if (typed.EndsWith(SyncStore.SyncFileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(typed))
        {
            Folder = SyncStore.FolderOf(typed);
            return;
        }

        try
        {
            switch (SyncStore.Inspect(Folder))
            {
                case SyncStore.FolderKind.Empty:
                    FolderUsable = true;
                    FolderState = Strings.Setup_Folder_Empty;
                    break;
                case SyncStore.FolderKind.SyncFolder:
                    var devices = SyncStore.Open(Folder).ListDevices();
                    foreach (var device in devices)
                    {
                        ExistingDevices.Add(new ExistingDevice(device.DeviceId, device.DisplayName, device.LastSeen));
                    }

                    FolderUsable = true;
                    JoinsExisting = true;
                    FolderState = Text.Format(Strings.Setup_Folder_Sync, devices.Count, string.Join(", ", devices.Select(d => d.DisplayName)));
                    break;
                case SyncStore.FolderKind.Foreign:
                    FolderUsable = false;
                    FolderState = Strings.Setup_Folder_Foreign;
                    break;
                default:
                    FolderUsable = false;
                    FolderState = Strings.Setup_Folder_Invalid;
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FolderUsable = false;
            FolderState = Text.ForError(ex);
        }
    }

    private async Task FinishAsync()
    {
        var settings = engine.Settings;
        settings.GameConfigFolder = GameFolder;
        settings.PluginConfigFolder = string.IsNullOrWhiteSpace(PluginFolder) ? null : PluginFolder;
        settings.GameInstallFolder = string.IsNullOrWhiteSpace(InstallFolder) ? null : InstallFolder;
        settings.BackUpPlugins = BackUpPlugins && settings.PluginConfigFolder is not null;
        settings.BackupOnGameExit = BackupOnGameExit;
        settings.AutoApplyAlways = AutoApply;
        settings.FileAssociation = FileAssociation;
        var number = 1;
        foreach (var folder in GameFolders.FindCharacterFolders(GameFolder))
        {
            settings.CharacterNames.TryAdd(folder, Text.Format(Strings.Character_Number, number++));
        }

        Working = true;
        Progress = Strings.Setup_Running;
        var folderPath = Folder;
        var name = DeviceName.Trim();
        var joins = JoinsExisting;
        var again = SetUpAgain ? AgainAs?.DeviceId : null;
        try
        {
            string? backupProblem = null;
            await Task.Run(() =>
            {
                // A retry after a partial failure, or choosing the folder already in use, must not
                // add this PC a second time (review 3, finding 3).
                var alreadyIn = AlreadyJoined(folderPath);
                if (alreadyIn)
                {
                    if (again is null && !string.Equals(engine.Settings.DisplayName, name, StringComparison.Ordinal))
                    {
                        engine.Settings.DisplayName = name;
                        engine.UpdateDeviceInfo();
                    }
                }
                else if (joins)
                {
                    engine.JoinSyncFolder(folderPath, name, again);
                }
                else
                {
                    engine.CreateSyncFolder(folderPath, name);
                }

                engine.SaveSettings();
                try
                {
                    engine.Backup(RevisionOrigin.Manual);
                }
                catch (Exception ex) when (ex is RefusedException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // The set-up is complete; the first backup follows later (game exit, next start).
                    backupProblem = Text.ForError(ex);
                }
            }).ConfigureAwait(true);

            try
            {
                if (Autostart != shell.IsAutostartEnabled)
                {
                    shell.SetAutostart(Autostart);
                }

                if (FileAssociation != shell.IsFileAssociationRegistered)
                {
                    shell.SetFileAssociation(FileAssociation);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                ui.ShowError(Text.ForError(ex));
            }

            engine.SaveSettings();
            Progress = string.Empty;
            var done = again is not null ? Strings.Setup_DoneAgain : joins ? Strings.Setup_DoneJoined : Strings.Setup_DoneNew;
            if (backupProblem is not null)
            {
                done = Text.Format(Strings.Setup_DoneNoBackup, backupProblem);
            }

            ui.Inform(Strings.Setup_Title, done);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is RefusedException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            Progress = Text.ForError(ex);
            ui.ShowError(Text.ForError(ex));
        }
        finally
        {
            Working = false;
        }
    }
}
