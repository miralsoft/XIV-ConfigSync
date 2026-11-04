using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivConfigSync.Core.Discovery;
using XivConfigSync.Core.Platform;
using XivConfigSync.Core.Settings;

namespace XivConfigSync.Core.ViewModels;

/// <summary>A language choice; a null code means the system's language.</summary>
public sealed record LanguageChoice(string? Code, string Name);

/// <summary>A name for a character folder (X-06: the folder's ID is not shown).</summary>
public sealed partial class CharacterRow : ObservableObject
{
    public CharacterRow(string folder, string name, int number)
    {
        Folder = folder;
        Label = Text.Format(Strings.Character_Number, number);
        Name = name;
    }

    public string Folder { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial string Name { get; set; }
}

/// <summary>The settings page.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel main;
    private readonly IUiServices ui;
    private readonly IShellIntegration shell;
    private readonly IGameLocator locator;

    public SettingsViewModel(MainViewModel main, IUiServices ui, IShellIntegration shell, IGameLocator locator)
    {
        this.main = main;
        this.ui = ui;
        this.shell = shell;
        this.locator = locator;
        Load();
    }

    public IReadOnlyList<LanguageChoice> Languages { get; } =
    [
        new(null, Strings.Language_System),
        new("de", "Deutsch"),
        new("en", "English"),
    ];

    public ObservableCollection<CharacterRow> Characters { get; } = [];

    [ObservableProperty]
    public partial string DeviceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PluginFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InstallFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool BackUpPlugins { get; set; }

    [ObservableProperty]
    public partial bool Autostart { get; set; }

    [ObservableProperty]
    public partial bool FileAssociation { get; set; }

    [ObservableProperty]
    public partial bool BackupOnGameExit { get; set; }

    [ObservableProperty]
    public partial bool AutoApply { get; set; }

    [ObservableProperty]
    public partial bool MinimizeOnClose { get; set; }

    [ObservableProperty]
    public partial LanguageChoice? Language { get; set; }

    [ObservableProperty]
    public partial int KeepRevisions { get; set; }

    public string SyncFolder => main.Engine.Settings.SyncFolder ?? string.Empty;

    public string DeviceId => main.Engine.Settings.DeviceId;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Bound from XAML, which needs an instance member.")]
    public string Version => SyncEngine_Version();

    /// <summary>Reads the settings into the page.</summary>
    public void Load()
    {
        var s = main.Engine.Settings;
        DeviceName = s.DisplayName;
        GameFolder = s.GameConfigFolder ?? string.Empty;
        PluginFolder = s.PluginConfigFolder ?? string.Empty;
        InstallFolder = s.GameInstallFolder ?? string.Empty;
        GameVersion = main.Engine.LocalGameVersion ?? Strings.Common_Unknown;
        BackUpPlugins = s.BackUpPlugins;
        Autostart = shell.IsAutostartEnabled;
        FileAssociation = shell.IsFileAssociationRegistered;
        BackupOnGameExit = s.BackupOnGameExit != false;
        AutoApply = s.AutoApplyAlways == true;
        MinimizeOnClose = s.CloseAction != CloseAction.Exit;
        Language = Languages.FirstOrDefault(l => l.Code == s.Language) ?? Languages[0];
        KeepRevisions = s.KeepRevisions;
        Characters.Clear();
        var number = 1;
        foreach (var folder in GameFolders.FindCharacterFolders(s.GameConfigFolder))
        {
            Characters.Add(new CharacterRow(folder, s.CharacterNames.TryGetValue(folder, out var name) ? name : string.Empty, number++));
        }
    }

    [RelayCommand]
    public void Save()
    {
        var s = main.Engine.Settings;
        var languageChanged = s.Language != Language?.Code;
        s.DisplayName = string.IsNullOrWhiteSpace(DeviceName) ? s.DisplayName : DeviceName.Trim();
        s.GameConfigFolder = string.IsNullOrWhiteSpace(GameFolder) ? null : GameFolder.Trim();
        s.PluginConfigFolder = string.IsNullOrWhiteSpace(PluginFolder) ? null : PluginFolder.Trim();
        s.GameInstallFolder = string.IsNullOrWhiteSpace(InstallFolder) ? null : InstallFolder.Trim();
        s.BackUpPlugins = BackUpPlugins;
        s.BackupOnGameExit = BackupOnGameExit;
        s.AutoApplyAlways = AutoApply;
        s.CloseAction = MinimizeOnClose ? CloseAction.MinimizeToTray : CloseAction.Exit;
        s.Language = Language?.Code;
        s.KeepRevisions = Math.Clamp(KeepRevisions, 5, 500);
        s.CharacterNames = Characters.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToDictionary(c => c.Folder, c => c.Name.Trim());
        try
        {
            shell.SetAutostart(Autostart);
            shell.SetFileAssociation(FileAssociation);
            s.FileAssociation = FileAssociation;
            main.Engine.SaveSettings();
            main.Engine.UpdateDeviceInfo();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ui.ShowError(Text.ForError(ex));
            return;
        }

        main.StatusText = languageChanged ? Strings.Settings_LanguageRestart : Strings.Settings_Saved;
        Load();
        _ = main.RefreshAsync();
    }

    [RelayCommand]
    public void BrowseGameFolder()
    {
        var folder = ui.PickFolder(Strings.Settings_GameFolder, GameFolder);
        if (folder is not null)
        {
            GameFolder = folder;
        }
    }

    [RelayCommand]
    public void BrowsePluginFolder()
    {
        var folder = ui.PickFolder(Strings.Settings_PluginFolder, PluginFolder);
        if (folder is not null)
        {
            PluginFolder = folder;
        }
    }

    [RelayCommand]
    public void BrowseInstallFolder()
    {
        var folder = ui.PickFolder(Strings.Settings_InstallFolder, InstallFolder);
        if (folder is not null)
        {
            InstallFolder = folder;
            GameVersion = GameFolders.ReadGameVersion(folder) ?? Strings.Common_Unknown;
        }
    }

    [RelayCommand]
    public void DetectFolders()
    {
        GameFolder = GameFolders.LooksLikeGameConfigFolder(GameFolders.DefaultGameConfigFolder) ? GameFolders.DefaultGameConfigFolder : GameFolder;
        PluginFolder = GameFolders.FindPluginConfigFolder() ?? PluginFolder;
        var install = GameFolders.PickInstallFolder(locator.InstallFolderCandidates());
        if (install is not null)
        {
            InstallFolder = install;
            GameVersion = GameFolders.ReadGameVersion(install) ?? Strings.Common_Unknown;
        }
    }

    [RelayCommand]
    public void ChangeSyncFolder()
    {
        if (ui.Confirm(Strings.Settings_ChangeFolder, Strings.Settings_ChangeFolderConfirm))
        {
            main.RequestSetup();
        }
    }

    [RelayCommand]
    public void OpenSyncFolder()
    {
        if (!string.IsNullOrEmpty(SyncFolder))
        {
            ui.OpenFolder(SyncFolder);
        }
    }

    private static string SyncEngine_Version() => Engine.SyncEngine.AppVersion;
}
