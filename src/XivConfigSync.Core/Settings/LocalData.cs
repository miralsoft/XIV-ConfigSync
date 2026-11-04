namespace XivConfigSync.Core.Settings;

/// <summary>Paths of the local data folder (<c>%LOCALAPPDATA%\XIVConfigSync</c> in normal use).</summary>
public sealed class LocalData
{
    public LocalData(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
    }

    /// <summary>The default location.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVConfigSync");

    public string Root { get; }

    public string SettingsPath => Path.Combine(Root, "settings.json");

    public string RulesPath => Path.Combine(Root, "rules.json");

    public string JournalPath => Path.Combine(Root, "journal.json");

    public string HandledPath => Path.Combine(Root, "handled.json");

    public string LockPath => Path.Combine(Root, "busy.lock");

    public string SnapshotsFolder => Path.Combine(Root, "snapshots");

    public string BasesFolder => Path.Combine(Root, "bases");

    public string LogsFolder => Path.Combine(Root, "logs");

    /// <summary>The base table for one source device.</summary>
    public string BasePath(string sourceDeviceId) => Path.Combine(BasesFolder, sourceDeviceId + ".json");
}
