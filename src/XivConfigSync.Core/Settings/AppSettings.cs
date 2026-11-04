using XivConfigSync.Core.IO;

namespace XivConfigSync.Core.Settings;

/// <summary>What happens when the main window is closed.</summary>
public enum CloseAction
{
    MinimizeToTray,
    Exit,
}

/// <summary>
/// The application's own settings (<c>settings.json</c>), versioned with migration (FR-062).
/// A null value means "not chosen yet": the default applies, and the user is asked once where X-08 requires it.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The schema this version writes.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public string DeviceId { get; set; } = string.Empty;

    /// <summary>This device's epoch; see <see cref="Model.DeviceInfo.Epoch"/>.</summary>
    public string DeviceEpoch { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? SyncFolder { get; set; }

    public string? GameConfigFolder { get; set; }

    public string? PluginConfigFolder { get; set; }

    public string? GameInstallFolder { get; set; }

    public Dictionary<string, string> CharacterNames { get; set; } = [];

    public string? Language { get; set; }

    public CloseAction? CloseAction { get; set; }

    public bool Autostart { get; set; }

    public bool? BackupOnGameExit { get; set; }

    public bool? FileAssociation { get; set; }

    /// <summary>Whether files with the rule "always" are applied from other devices without asking.</summary>
    public bool? AutoApplyAlways { get; set; }

    public bool BackUpPlugins { get; set; } = true;

    public int KeepRevisions { get; set; } = 50;

    /// <summary>Whether the first-start set-up was completed.</summary>
    public bool IsSetUp => !string.IsNullOrEmpty(DeviceId) && !string.IsNullOrEmpty(SyncFolder);

    /// <summary>Loads the settings, migrating older schemas; returns defaults when there are none.</summary>
    public static AppSettings Load(string path)
    {
        var settings = FileOps.ReadJson<AppSettings>(path) ?? new AppSettings();
        if (settings.Schema > CurrentSchema)
        {
            throw new InvalidDataException("The settings were written by a newer version of the application.");
        }

        // Schema 1 is the first; later migrations go here, oldest first.
        settings.Schema = CurrentSchema;
        settings.KeepRevisions = Math.Clamp(settings.KeepRevisions, 5, 500);
        return settings;
    }

    /// <summary>Saves the settings atomically.</summary>
    public void Save(string path) => FileOps.WriteJsonAtomic(path, this);

    /// <summary>A deep copy, to put the settings back when a save fails.</summary>
    public AppSettings Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this, Json.Options), Json.Options)!;

    /// <summary>Takes over every value of another instance.</summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
        {
            property.SetValue(this, property.GetValue(other));
        }
    }
}
