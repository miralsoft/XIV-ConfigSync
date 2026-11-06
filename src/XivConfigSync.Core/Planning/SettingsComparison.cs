using System.Text.Json;
using System.Text.RegularExpressions;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.Planning;

/// <summary>One setting that differs between this device and the source.</summary>
/// <param name="GroupKey">The resource key of the group's name, for the parts of a split settings file; else null.</param>
/// <param name="GroupText">The group as written in the file (a section or a top-level field), when there is no resource key.</param>
/// <param name="Setting">The setting's name in the file, or the path of a field (<c>Window.Position.X</c>).</param>
/// <param name="Here">The value on this device; null when this device does not have the setting.</param>
/// <param name="There">The value in the source; null when the source does not have it.</param>
/// <param name="StaysHere">Whether this device keeps its value when the file is taken (a group switched off).</param>
public sealed record SettingDifference(string? GroupKey, string GroupText, string Setting, string? Here, string? There, bool StaysHere);

/// <summary>What differs inside one file, as far as its content can be read.</summary>
/// <param name="Readable">Whether the file's content could be read setting by setting.</param>
/// <param name="Differences">The settings that differ, in file order.</param>
/// <param name="Truncated">Whether more differ than are listed.</param>
public sealed record SettingsComparisonResult(bool Readable, IReadOnlyList<SettingDifference> Differences, bool Truncated)
{
    /// <summary>A file whose format is binary or unknown: only "differs" can be said.</summary>
    public static SettingsComparisonResult Unreadable { get; } = new(false, [], false);
}

/// <summary>
/// Compares two versions of a settings file setting by setting, read-only, to show the user what a
/// transfer would change (owner's request, 2026-11-06). The game's text files are compared by
/// setting name, JSON files (plugins, Dalamud) by field path. Binary files are not read.
/// </summary>
public static partial class SettingsComparison
{
    /// <summary>At most this many differences are listed; the rest is counted as truncated.</summary>
    public const int MaxDifferences = 400;

    private const int MaxValueLength = 120;

    /// <summary>Compares the source's file with this device's.</summary>
    /// <param name="root">The root of the file.</param>
    /// <param name="path">The path below the root.</param>
    /// <param name="local">This device's content, or null when the file is missing here.</param>
    /// <param name="source">The source's content.</param>
    /// <param name="keepLocal">For a split settings file, the settings that keep this device's value; else null.</param>
    public static SettingsComparisonResult Compare(string root, string path, byte[]? local, byte[] source, Func<string, bool>? keepLocal)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return CompareJson(local, source);
        }

        if (root == Roots.Game && (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)))
        {
            return CompareText(path, local, source, keepLocal);
        }

        return SettingsComparisonResult.Unreadable;
    }

    /// <summary>Whether a value is hidden because its name suggests a credential or an identifier of a session.</summary>
    public static bool IsSensitive(string setting) => SensitiveName().IsMatch(setting);

    private static SettingsComparisonResult CompareText(string path, byte[]? local, byte[] source, Func<string, bool>? keepLocal)
    {
        var there = CommonSettings.Read(source);
        IReadOnlyList<(string Section, string Key, string Value)>? here = local is null ? [] : CommonSettings.Read(local);
        if (there is null || here is null)
        {
            return SettingsComparisonResult.Unreadable;
        }

        var split = SettingParts.IsSplitFile(Roots.Game, path);
        var hereValues = Occurrences(here.Select(s => (s.Key, s.Value)));
        var thereValues = Occurrences(there.Select(s => (s.Key, s.Value)));
        var sections = there.Concat(here).GroupBy(s => s.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Section, StringComparer.Ordinal);

        var differences = new List<SettingDifference>();
        var truncated = false;
        foreach (var key in thereValues.Keys.Concat(hereValues.Keys.Where(k => !thereValues.ContainsKey(k))))
        {
            var mine = hereValues.GetValueOrDefault(key) ?? [];
            var theirs = thereValues.GetValueOrDefault(key) ?? [];
            for (var i = 0; i < Math.Max(mine.Count, theirs.Count); i++)
            {
                var a = i < mine.Count ? mine[i] : null;
                var b = i < theirs.Count ? theirs[i] : null;
                if (a == b)
                {
                    continue;
                }

                if (differences.Count >= MaxDifferences)
                {
                    truncated = true;
                    break;
                }

                var part = split ? SettingParts.PartOf(path, key) : null;
                differences.Add(new SettingDifference(
                    part?.NameKey,
                    part is null ? sections[key] : string.Empty,
                    key,
                    Shown(key, a),
                    Shown(key, b),
                    keepLocal?.Invoke(key) ?? false));
            }
        }

        return new SettingsComparisonResult(true, differences, truncated);
    }

    private static SettingsComparisonResult CompareJson(byte[]? local, byte[] source)
    {
        var there = Flatten(source);
        var here = local is null ? new Dictionary<string, string>(StringComparer.Ordinal) : Flatten(local);
        if (there is null || here is null)
        {
            return SettingsComparisonResult.Unreadable;
        }

        var differences = new List<SettingDifference>();
        var truncated = false;
        foreach (var key in there.Keys.Concat(here.Keys.Where(k => !there.ContainsKey(k))))
        {
            var a = here.GetValueOrDefault(key);
            var b = there.GetValueOrDefault(key);
            if (a == b)
            {
                continue;
            }

            if (differences.Count >= MaxDifferences)
            {
                truncated = true;
                break;
            }

            var dot = key.IndexOfAny(['.', '[']);
            differences.Add(new SettingDifference(null, dot < 0 ? string.Empty : key[..dot], key, Shown(key, a), Shown(key, b), false));
        }

        return new SettingsComparisonResult(true, differences, truncated);
    }

    /// <summary>Every value of a JSON document by its path, in document order; null when it is not JSON.</summary>
    private static Dictionary<string, string>? Flatten(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            Walk(document.RootElement, string.Empty, values);
            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Walk(JsonElement element, string path, Dictionary<string, string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var any = false;
                foreach (var property in element.EnumerateObject())
                {
                    any = true;
                    Walk(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, values);
                }

                if (!any)
                {
                    values[path] = "{}";
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, path + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", values);
                    index++;
                }

                if (index == 0)
                {
                    values[path] = "[]";
                }

                break;
            case JsonValueKind.String:
                values[path] = element.GetString() ?? string.Empty;
                break;
            default:
                values[path] = element.GetRawText();
                break;
        }
    }

    private static Dictionary<string, List<string>> Occurrences(IEnumerable<(string Key, string Value)> settings)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in settings)
        {
            if (!result.TryGetValue(key, out var list))
            {
                list = [];
                result[key] = list;
            }

            list.Add(value);
        }

        return result;
    }

    private static string? Shown(string setting, string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (IsSensitive(setting))
        {
            return "••••";
        }

        var oneLine = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return oneLine.Length <= MaxValueLength ? oneLine : string.Concat(oneLine.AsSpan(0, MaxValueLength), "…");
    }

    [GeneratedRegex(@"(?i)(password|passwd|token|secret|session|api_?key|auth|cookie|lastlogin|guid|webhook)")]
    private static partial Regex SensitiveName();
}
