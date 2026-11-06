using System.Globalization;
using System.Text.Json;

namespace XivConfigSync.Core.Discovery;

/// <summary>A character's name, and its home world when a source gives it as text.</summary>
public sealed record CharacterIdentity(string Name, string? World)
{
    /// <summary>"Name (World)", or the name alone.</summary>
    public string Display => World is null ? Name : $"{Name} ({World})";
}

/// <summary>
/// Suggests names for the character folders from what plugins already store, read-only (owner's
/// request, 2026-11-05). The game's own character files hold no name as text, so nothing is read
/// from them. Plugins key their data by the character's content ID, the same number as the
/// folder name <c>FFXIV_CHR&lt;ID in hexadecimal&gt;</c>. The shapes below were read from real files
/// on device A; a file of another shape is skipped, never guessed at. Only worlds given as text
/// are used: a table from world numbers to names would be material from the game (X-09).
/// </summary>
public static partial class CharacterIdentities
{
    private const int MaxFileSize = 8 * 1024 * 1024;

    /// <summary>
    /// Whether a name is empty or the numbered one the set-up gives ("Character 2", "Charakter 2"),
    /// which a suggestion may replace.
    /// </summary>
    public static bool IsPlaceholder(string? name) =>
        string.IsNullOrWhiteSpace(name) || PlaceholderPattern().IsMatch(name.Trim());

    [System.Text.RegularExpressions.GeneratedRegex(@"^(Character|Charakter) \d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PlaceholderPattern();

    /// <summary>Suggestions per character folder name, for the folders given; a folder no source knows is missing.</summary>
    public static IReadOnlyDictionary<string, CharacterIdentity> FromPluginData(string? pluginConfigFolder, IEnumerable<string> characterFolders)
    {
        var wanted = new Dictionary<ulong, string>();
        foreach (var folder in characterFolders)
        {
            if (folder.StartsWith("FFXIV_CHR", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(folder.AsSpan(9), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            {
                wanted[id] = folder;
            }
        }

        var names = new Dictionary<ulong, string>();
        var worlds = new Dictionary<ulong, string>();
        if (wanted.Count == 0 || string.IsNullOrWhiteSpace(pluginConfigFolder) || !Directory.Exists(pluginConfigFolder))
        {
            return new Dictionary<string, CharacterIdentity>();
        }

        // Most specific first: the first source that names a character wins, likewise for its world.
        Read(pluginConfigFolder, "AutoRetainer/DefaultConfig.json", root => AutoRetainer(root, names, worlds));
        Read(pluginConfigFolder, "Lifestream/DefaultConfig.json", root => NameAtWorldMap(root, "CharaMap", names, worlds));
        Read(pluginConfigFolder, "ICE/DefaultConfig.json", root => NameAtWorldMap(root, "KnownCharacters", names, worlds));
        Read(pluginConfigFolder, "InventoryTools.json", root => NamedObjects(root, "SavedCharacters", names));
        Read(pluginConfigFolder, "AllaganMarket.json", root => NamedObjects(root, "Characters", names));

        var result = new Dictionary<string, CharacterIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, folder) in wanted)
        {
            if (names.TryGetValue(id, out var name))
            {
                result[folder] = new CharacterIdentity(name, worlds.GetValueOrDefault(id));
            }
        }

        return result;
    }

    private static void Read(string folder, string relative, Action<JsonElement> parse)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSize)
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            parse(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            // A source that cannot be read or has another shape gives no suggestion.
        }
    }

    /// <summary>AutoRetainer: <c>OfflineData</c>, a list of objects with <c>CID</c>, <c>Name</c> and <c>World</c>.</summary>
    private static void AutoRetainer(JsonElement root, Dictionary<ulong, string> names, Dictionary<ulong, string> worlds)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("OfflineData", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("CID", out var cid) && cid.ValueKind == JsonValueKind.Number && cid.TryGetUInt64(out var id))
            {
                Add(names, id, Text(entry, "Name"));
                Add(worlds, id, Text(entry, "World"));
            }
        }
    }

    /// <summary>Lifestream and ICE: an object from the content ID in decimal to "Name@World" or "Name".</summary>
    private static void NameAtWorldMap(JsonElement root, string key, Dictionary<ulong, string> names, Dictionary<ulong, string> worlds)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var map) || map.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in map.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || !ulong.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var value = property.Value.GetString() ?? string.Empty;
            var at = value.LastIndexOf('@');
            Add(names, id, at > 0 ? value[..at] : value);
            Add(worlds, id, at > 0 ? value[(at + 1)..] : null);
        }
    }

    /// <summary>Allagan Tools and Allagan Market: an object from the content ID in decimal to an object with <c>Name</c>.</summary>
    private static void NamedObjects(JsonElement root, string key, Dictionary<ulong, string> names)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var map) || map.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in map.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && ulong.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                Add(names, id, Text(property.Value, "Name"));
            }
        }
    }

    private static string? Text(JsonElement entry, string key) =>
        entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Keeps the first plausible value: a short, single line of text.</summary>
    private static void Add(Dictionary<ulong, string> target, ulong id, string? value)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed.Length <= 40 && !trimmed.Any(char.IsControl))
        {
            target.TryAdd(id, trimmed);
        }
    }
}
