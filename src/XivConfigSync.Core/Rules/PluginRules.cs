using XivConfigSync.Core.Model;

namespace XivConfigSync.Core.Rules;

/// <summary>One plugin in the plugin settings folder.</summary>
public sealed record PluginInfo(string Name, int Files, long Size);

/// <summary>
/// Rules per plugin (owner's request, 2026-11-05). A plugin keeps its settings as
/// <c>&lt;Name&gt;.json</c> (sometimes with other extensions) and, for many, a folder
/// <c>&lt;Name&gt;\</c>. Its rule is an override on the two patterns <c>&lt;Name&gt;.*</c> and
/// <c>&lt;Name&gt;/**</c>, and "do not back up" is a user exclusion on the same two, so both go
/// through the existing rule set and stay visible and editable as such.
/// </summary>
public static class PluginRules
{
    /// <summary>The plugins in a plugin settings folder, by name; empty without one.</summary>
    public static IReadOnlyList<PluginInfo> List(string? pluginFolder)
    {
        if (string.IsNullOrWhiteSpace(pluginFolder) || !Directory.Exists(pluginFolder))
        {
            return [];
        }

        var plugins = new Dictionary<string, (int Files, long Size)>(StringComparer.OrdinalIgnoreCase);
        var folder = new DirectoryInfo(pluginFolder);
        foreach (var file in folder.EnumerateFiles())
        {
            var dot = file.Name.IndexOf('.', StringComparison.Ordinal);
            Count(plugins, dot > 0 ? file.Name[..dot] : file.Name, 1, file.Length);
        }

        foreach (var sub in folder.EnumerateDirectories())
        {
            if (sub.LinkTarget is not null)
            {
                continue;
            }

            var files = 0;
            long size = 0;
            try
            {
                foreach (var file in sub.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    files++;
                    size += file.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Counted as far as it could be read.
            }

            Count(plugins, sub.Name, files, size);
        }

        return [.. plugins
            .Where(p => IsUsableName(p.Key))
            .Select(p => new PluginInfo(p.Key, p.Value.Files, p.Value.Size))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The two patterns that stand for a plugin.</summary>
    public static IReadOnlyList<string> Patterns(string name) => [name + ".*", name + "/**"];

    /// <summary>The plugin's own rule, or null when it follows the rule for all plugin files.</summary>
    public static ApplyRule? RuleOf(RuleSet rules, string name)
    {
        var patterns = Patterns(name);
        return rules.File.Overrides
            .FirstOrDefault(o => o.Root == Roots.Plugins && patterns.Contains(o.Pattern, StringComparer.OrdinalIgnoreCase))?.Rule;
    }

    /// <summary>Sets the plugin's own rule, or removes it with null.</summary>
    public static RuleSet WithRule(RuleSet rules, string name, ApplyRule? rule)
    {
        foreach (var pattern in Patterns(name))
        {
            rules = rules.WithOverride(Roots.Plugins, pattern, rule);
        }

        return rules;
    }

    /// <summary>Whether the plugin is left out of the backup by the user.</summary>
    public static bool IsExcluded(RuleSet rules, string name)
    {
        var patterns = Patterns(name);
        return rules.File.Exclusions.Any(e => e.Root == Roots.Plugins && patterns.Contains(e.Pattern, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Leaves the plugin out of the backup, or takes it in again.</summary>
    public static RuleSet WithExcluded(RuleSet rules, string name, bool excluded)
    {
        foreach (var pattern in Patterns(name))
        {
            rules = rules.WithExclusion(Roots.Plugins, pattern, excluded);
        }

        return rules;
    }

    /// <summary>A name that makes a safe pattern: no wildcard or path characters, not hidden.</summary>
    public static bool IsUsableName(string name) =>
        name.Length > 0 && name[0] != '.' && name.IndexOfAny(['*', '?', '[', ']', '{', '}', '/', '\\']) < 0;

    private static void Count(Dictionary<string, (int Files, long Size)> plugins, string name, int files, long size)
    {
        plugins.TryGetValue(name, out var current);
        plugins[name] = (current.Files + files, current.Size + size);
    }
}
