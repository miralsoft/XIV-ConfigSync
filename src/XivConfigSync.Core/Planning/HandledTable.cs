using XivConfigSync.Core.IO;

namespace XivConfigSync.Core.Planning;

/// <summary>
/// Per other device, the latest revision this device has handled: applied as far as the rules
/// allow, or announced to the user (<c>handled.json</c>).
/// </summary>
public sealed class HandledTable
{
    public int Schema { get; set; } = 1;

    public Dictionary<string, string> Devices { get; set; } = [];

    /// <summary>Loads the table, or an empty one.</summary>
    public static HandledTable Load(string path)
    {
        var table = FileOps.ReadJson<HandledTable>(path) ?? new HandledTable();
        table.Devices ??= [];
        return table;
    }

    /// <summary>Saves it atomically.</summary>
    public void Save(string path) => FileOps.WriteJsonAtomic(path, this);

    /// <summary>The handled revision of a device, or null.</summary>
    public string? Get(string deviceId) => Devices.TryGetValue(deviceId, out var id) ? id : null;

    /// <summary>Records the handled revision of a device.</summary>
    public void Set(string deviceId, string revisionId) => Devices[deviceId] = revisionId;
}
