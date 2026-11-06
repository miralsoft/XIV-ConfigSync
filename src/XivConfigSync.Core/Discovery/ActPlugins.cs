using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.Discovery;

/// <summary>
/// ACT itself or one of its plugins, with the paths of its settings (owner's request, 2026-11-06).
/// Every path is relative to the roaming application data folder and may hold <c>*</c> and
/// <c>**</c>.
/// </summary>
/// <param name="Key">The stored key: <see cref="ActPlugins.ActKey"/> for ACT, else the plugin's file name without extension.</param>
/// <param name="Enabled">Whether ACT loads the plugin; always true for ACT itself.</param>
/// <param name="Proposal">The paths found for it.</param>
/// <param name="Paths">The paths backed up: the user's own list where there is one, else the proposal.</param>
/// <param name="IsCustom">Whether the user changed the paths.</param>
/// <param name="Outside">Paths its settings name that are outside the roaming folder, or too large; shown, never backed up.</param>
public sealed record ActPlugin(string Key, bool Enabled, IReadOnlyList<string> Proposal, IReadOnlyList<string> Paths, bool IsCustom, IReadOnlyList<string> Outside);

/// <summary>
/// Finds ACT's plugins and where their settings are, from ACT's own list of plugins and the
/// settings files themselves, so a new plugin needs no code of its own (owner's request,
/// 2026-11-06). Read-only. Three sources:
/// <list type="bullet">
/// <item>ACT's folder <c>Config</c>, where plugins keep <c>&lt;Name&gt;.config.xml</c> or
/// <c>.json</c> by ACT's convention; each file goes to the plugin whose name it contains.</item>
/// <item>Paths written in those files that point to a folder or file below the roaming folder,
/// such as cactbot's user folder in OverlayPlugin's settings. Paths that look like logs, caches,
/// temporary files or backups are never followed.</item>
/// <item>A short table of places no path points to (<see cref="KnownLocations"/>): data, not code.</item>
/// </list>
/// Program files, logs and caches are never proposed; the user can change every list.
/// </summary>
public static partial class ActPlugins
{
    /// <summary>The key of ACT itself.</summary>
    public const string ActKey = "ACT";

    /// <summary>ACT's folder below the roaming folder.</summary>
    public const string ActFolder = "Advanced Combat Tracker";

    /// <summary>ACT's settings folder, shared by the plugins.</summary>
    public const string ConfigFolder = ActFolder + "/Config";

    /// <summary>ACT's own settings: plugin paths, window positions and the log folder of this device.</summary>
    public const string MainConfig = ConfigFolder + "/Advanced Combat Tracker.config.xml";

    /// <summary>A folder named in a settings file above this size is not proposed, only shown.</summary>
    public const long MaxTracedFolderSize = 20L * 1024 * 1024;

    /// <summary>
    /// Places where plugins keep settings that no path in their files points to: the plugins by
    /// their file name, and the paths. Hojoring's plugins keep theirs below <c>anoyetta\ACT</c>.
    /// </summary>
    public static IReadOnlyList<(IReadOnlyList<string> Plugins, IReadOnlyList<string> Paths)> KnownLocations { get; } =
    [
        (["ACT.SpecialSpellTimer", "ACT.UltraScouter", "ACT.TTSYukkuri", "ACT.XIVLog", "ACT.Hojoring"], ["anoyetta/ACT/*.xml", "anoyetta/ACT/*.config"]),
    ];

    private static readonly string[] SettingsExtensions = [".json", ".xml", ".ini", ".cfg", ".config"];
    private static readonly string[] ProgramSuffixes = [".dll.config", ".exe.config", ".deps.json", ".runtimeconfig.json", ".nlog.config"];
    private static readonly string[] ProgramExtensions = [".dll", ".exe", ".pdb", ".ps1", ".bat", ".cmd"];

    /// <summary>Whether ACT has a settings folder below this roaming folder.</summary>
    public static bool IsInstalled(string? roaming) =>
        !string.IsNullOrEmpty(roaming) && Directory.Exists(Path.Combine(roaming, ActFolder, "Config"));

    /// <summary>The plugins ACT's settings list, with whether ACT loads each; empty when the file cannot be read.</summary>
    public static IReadOnlyList<(string Path, bool Enabled)> ReadPluginList(byte[] mainConfig)
    {
        try
        {
            using var stream = new MemoryStream(mainConfig);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            return [.. document.Descendants("Plugin")
                .Select(p => (Path: (string?)p.Attribute("Path"), Enabled: !string.Equals((string?)p.Attribute("Enabled"), "False", StringComparison.OrdinalIgnoreCase)))
                .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                .Select(p => (p.Path!, p.Enabled))];
        }
        catch (XmlException)
        {
            return [];
        }
    }

    /// <summary>The key of a plugin from its path: the file name without extension.</summary>
    public static string KeyOf(string pluginPath) => Path.GetFileNameWithoutExtension(pluginPath.Replace('/', '\\').Split('\\')[^1]);

    /// <summary>
    /// ACT and its plugins below a roaming folder, with their paths; empty when ACT is not there.
    /// </summary>
    /// <param name="roaming">The roaming application data folder.</param>
    /// <param name="custom">The user's own lists of paths, by key.</param>
    public static IReadOnlyList<ActPlugin> Discover(string? roaming, IReadOnlyDictionary<string, List<string>> custom)
    {
        if (!IsInstalled(roaming))
        {
            return [];
        }

        var root = roaming!;
        var main = Path.Combine(root, ActFolder, "Config", "Advanced Combat Tracker.config.xml");
        var listed = File.Exists(main) ? ReadPluginList(ReadSmall(main) ?? []) : [];
        var plugins = listed
            .Select(p => (Key: KeyOf(p.Path), p.Enabled, Folder: ToRelative(root, Path.GetDirectoryName(p.Path) ?? string.Empty)))
            .Where(p => p.Key.Length > 0)
            .DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var proposals = plugins.ToDictionary(p => p.Key, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var outside = plugins.ToDictionary(p => p.Key, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        proposals[ActKey] = [MainConfig];
        outside[ActKey] = [];

        // The shared settings folder: each file to the plugin whose name it holds, the rest to ACT.
        var sources = new List<(string Key, string Full)>();
        foreach (var file in SafeFiles(Path.Combine(root, ActFolder, "Config")))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "Advanced Combat Tracker.config.xml", StringComparison.OrdinalIgnoreCase) || !IsSettingsFile(name))
            {
                continue;
            }

            var owner = plugins.FirstOrDefault(p => Normalized(name).Contains(Normalized(p.Key), StringComparison.Ordinal)).Key ?? ActKey;
            proposals[owner].Add(ConfigFolder + "/" + name);
            sources.Add((owner, file));
        }

        // Settings files directly in a plugin's own folder, when the plugin has a folder of its own.
        foreach (var plugin in plugins.Where(p => p.Folder is { Length: > 0 } && !IsSharedFolder(p.Folder)))
        {
            foreach (var file in SafeFiles(Path.Combine(root, plugin.Folder!)))
            {
                if (IsSettingsFile(Path.GetFileName(file)))
                {
                    proposals[plugin.Key].Add(plugin.Folder + "/" + Path.GetFileName(file));
                    sources.Add((plugin.Key, file));
                }
            }
        }

        // Paths named in those files: to the plugin whose folder holds them, else to the file's plugin.
        foreach (var (key, full) in sources)
        {
            foreach (var path in TracedPaths(ReadSmall(full)))
            {
                // Only the part below the roaming folder is judged, so a user name never decides.
                var relative = ToRelative(root, path);
                if (LooksLikeNoSettings(relative ?? path))
                {
                    continue;
                }

                if (relative is null)
                {
                    outside[key].Add(path);
                    continue;
                }

                if (relative.Length == 0 || IsSharedFolder(relative) || IsWithin(ActFolder, relative) || plugins.Any(p => p.Folder is not null && IsWithin(p.Folder, relative)))
                {
                    // The roaming folder, ACT's folders or a folder that holds a plugin's program: too broad.
                    continue;
                }

                var owner = plugins.FirstOrDefault(p => p.Folder is { Length: > 0 } && !IsSharedFolder(p.Folder) && IsWithin(relative, p.Folder)).Key ?? key;
                var target = Path.Combine(root, relative.Replace('/', '\\'));
                if (Directory.Exists(target))
                {
                    if (FolderSize(target) > MaxTracedFolderSize)
                    {
                        outside[owner].Add(path);
                    }
                    else
                    {
                        proposals[owner].Add(relative + "/**");
                    }
                }
                else if (File.Exists(target) && !IsProgram(relative))
                {
                    proposals[owner].Add(relative);
                }
            }
        }

        // The table of places no path points to.
        foreach (var plugin in plugins)
        {
            foreach (var (names, paths) in KnownLocations)
            {
                if (names.Any(n => string.Equals(n, plugin.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    proposals[plugin.Key].AddRange(paths);
                }
            }
        }

        var result = new List<ActPlugin>();
        foreach (var (key, enabled) in new[] { (ActKey, true) }.Concat(plugins.Select(p => (p.Key, p.Enabled))))
        {
            var proposal = proposals[key].Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var own = custom.TryGetValue(key, out var list) ? list : null;
            result.Add(new ActPlugin(key, enabled, proposal, own ?? proposal, own is not null, outside[key].Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return result;
    }

    /// <summary>
    /// A line the user typed as a path: an absolute path below the roaming folder or a relative
    /// one, with '/' separators, wildcards allowed. Null when it is outside the roaming folder,
    /// climbs out of it or is empty.
    /// </summary>
    public static string? ParsePath(string? roaming, string line)
    {
        var text = line.Trim().Trim('"');
        if (text.Length == 0)
        {
            return null;
        }

        if (Path.IsPathFullyQualified(text))
        {
            text = roaming is null ? string.Empty : ToRelative(roaming, text) ?? string.Empty;
        }

        text = text.Replace('\\', '/').Trim('/');
        return text.Length == 0 || text.Split('/').Any(s => s is "" or "." or "..") || text.Contains(':', StringComparison.Ordinal) ? null : text;
    }

    /// <summary>
    /// The files below the roaming folder that a list of paths names, relative and with '/'.
    /// Only the folders the paths start in are walked.
    /// </summary>
    public static IEnumerable<string> Expand(string roaming, IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var segments = path.Split('/');
            var wild = Array.FindIndex(segments, s => s.Contains('*', StringComparison.Ordinal) || s.Contains('?', StringComparison.Ordinal));
            if (wild < 0)
            {
                if (File.Exists(Path.Combine(roaming, path.Replace('/', '\\'))) && seen.Add(path))
                {
                    yield return path;
                }

                continue;
            }

            var start = string.Join('/', segments[..wild]);
            var folder = Path.Combine(roaming, start.Replace('/', '\\'));
            if (start.Length == 0 || !Directory.Exists(folder))
            {
                continue;
            }

            var glob = new Glob(path);
            var deep = path.Contains("**", StringComparison.Ordinal) || wild < segments.Length - 1;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = deep, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var full in files)
            {
                var relative = Path.GetRelativePath(roaming, full).Replace('\\', '/');
                if (glob.IsMatch(relative) && seen.Add(relative))
                {
                    yield return relative;
                }
            }
        }
    }

    /// <summary>Absolute paths written in a settings file, as text (JSON escapes undone); judged by the caller.</summary>
    public static IReadOnlyList<string> TracedPaths(byte[]? content)
    {
        if (content is null)
        {
            return [];
        }

        var text = Encoding.UTF8.GetString(content).Replace(@"\\", @"\", StringComparison.Ordinal);
        var result = new List<string>();
        foreach (Match match in AbsolutePath().Matches(text))
        {
            result.Add(match.Value.TrimEnd('\\', ' ', '.'));
        }

        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A path whose names suggest logs, caches, temporary files or backups: never followed.</summary>
    public static bool LooksLikeNoSettings(string path) => NoSettings().IsMatch(path);

    private static bool IsSettingsFile(string name) =>
        SettingsExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))
        && !ProgramSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
        && !LooksLikeNoSettings(name);

    private static bool IsProgram(string path) => ProgramExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || LooksLikeNoSettings(path);

    /// <summary>ACT's own folder, its plugin folder and its settings folder: shared, never taken as a whole.</summary>
    private static bool IsSharedFolder(string relative) =>
        relative.Equals(ActFolder, StringComparison.OrdinalIgnoreCase)
        || relative.Equals(ActFolder + "/Plugins", StringComparison.OrdinalIgnoreCase)
        || relative.Equals(ConfigFolder, StringComparison.OrdinalIgnoreCase);

    private static bool IsWithin(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>A full path relative to the roaming folder with '/', empty for the folder itself; null when outside.</summary>
    private static string? ToRelative(string roaming, string full)
    {
        if (!Path.IsPathFullyQualified(full))
        {
            return null;
        }

        var relative = Path.GetRelativePath(Path.GetFullPath(roaming), Path.GetFullPath(full));
        if (relative == ".")
        {
            return string.Empty;
        }

        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) ? null : relative.Replace('\\', '/');
    }

    private static string Normalized(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase)] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static byte[]? ReadSmall(string full)
    {
        try
        {
            var info = new FileInfo(full);
            return info.Exists && info.Length <= 4 * 1024 * 1024 ? File.ReadAllBytes(full) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long FolderSize(string folder)
    {
        long size = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                size += file.Length;
                if (size > MaxTracedFolderSize)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }

        return size;
    }

    [GeneratedRegex(@"[A-Za-z]:\\[^""<>|\r\n\t*?]*")]
    private static partial Regex AbsolutePath();

    [GeneratedRegex(@"(?i)(log|cache|te?mp\b|\\temp\\|backup|\.bak\b|crash|dump)")]
    private static partial Regex NoSettings();
}
