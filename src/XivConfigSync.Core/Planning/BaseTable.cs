using XivConfigSync.Core.IO;

namespace XivConfigSync.Core.Planning;

/// <summary>
/// Per file, the hash both sides shared the last time this device applied from, or matched,
/// a source device (<c>bases\&lt;device&gt;.json</c>). Keyed by <c>root/path</c>.
/// </summary>
public sealed class BaseTable
{
    public int Schema { get; set; } = 1;

    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads a base table, or an empty one.</summary>
    public static BaseTable Load(string path)
    {
        var table = FileOps.ReadJson<BaseTable>(path) ?? new BaseTable();
        table.Files = new Dictionary<string, string>(table.Files, StringComparer.OrdinalIgnoreCase);
        return table;
    }

    /// <summary>Saves it atomically.</summary>
    public void Save(string path) => FileOps.WriteJsonAtomic(path, this);

    /// <summary>The key of a file.</summary>
    public static string Key(string root, string path) => root + "/" + path;

    /// <summary>The base hash of a file, or null.</summary>
    public string? Get(string root, string path) => Files.TryGetValue(Key(root, path), out var hash) ? hash : null;

    /// <summary>Sets the base hash of a file.</summary>
    public void Set(string root, string path, string hash) => Files[Key(root, path)] = hash;
}
