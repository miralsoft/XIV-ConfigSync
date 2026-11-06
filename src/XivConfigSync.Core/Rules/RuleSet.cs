using XivConfigSync.Core.IO;

namespace XivConfigSync.Core.Rules;

/// <summary>A user override of the matrix's default rule.</summary>
public sealed class RuleOverride
{
    public string Root { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public ApplyRule Rule { get; set; }
}

/// <summary>A user exclusion from the backup.</summary>
public sealed class UserExclusion
{
    public string Root { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;
}

/// <summary>Content of <c>rules.json</c>.</summary>
public sealed class RuleFile
{
    public int Schema { get; set; } = 1;

    public List<RuleOverride> Overrides { get; set; } = [];

    public List<UserExclusion> Exclusions { get; set; } = [];

    /// <summary>Per area key, whether it is taken from other devices; a missing key means on (owner's request, 2026-11-06).</summary>
    public Dictionary<string, bool> Areas { get; set; } = [];

    /// <summary>Per part of a settings file ("chat.font"), whether it is taken; a missing key means on (owner's request, 2026-11-06).</summary>
    public Dictionary<string, bool> Parts { get; set; } = [];
}

/// <summary>The effective rules: the built-in matrix with the user's overrides and exclusions.</summary>
public sealed class RuleSet
{
    private readonly List<(RuleOverride Override, Glob Glob)> overrides;
    private readonly List<(UserExclusion Exclusion, Glob Glob)> exclusions;

    public RuleSet(RuleFile file)
    {
        File = file;
        overrides = file.Overrides.Select(o => (o, new Glob(o.Pattern))).ToList();
        exclusions = file.Exclusions.Select(e => (e, new Glob(e.Pattern))).ToList();
    }

    /// <summary>The stored form.</summary>
    public RuleFile File { get; }

    /// <summary>Loads <c>rules.json</c>, or the defaults when it does not exist.</summary>
    public static RuleSet Load(string path) => new(FileOps.ReadJson<RuleFile>(path) ?? new RuleFile());

    /// <summary>Saves the rules atomically.</summary>
    public void Save(string path) => FileOps.WriteJsonAtomic(path, File);

    /// <summary>The rule for a file: the most specific override, else the matrix default.</summary>
    public ApplyRule RuleFor(string root, string relativePath)
    {
        (RuleOverride Override, Glob Glob)? best = null;
        foreach (var candidate in overrides)
        {
            if (candidate.Override.Root == root && candidate.Glob.IsMatch(relativePath)
                && (best is null || candidate.Glob.Specificity >= best.Value.Glob.Specificity))
            {
                best = candidate;
            }
        }

        if (best is not null)
        {
            return best.Value.Override.Rule;
        }

        var areas = Rules.Areas.Of(root, relativePath);
        if (areas.Count > 0)
        {
            return areas.Any(a => IsAreaOn(a.Key)) ? ApplyRule.Always : ApplyRule.Never;
        }

        return FileMatrix.Lookup(root, relativePath).DefaultRule;
    }

    /// <summary>The reason key when a file is excluded from the backup, else null.</summary>
    public string? ExclusionReason(string root, string relativePath)
    {
        foreach (var (exclusionRoot, glob, reason) in FileMatrix.Exclusions)
        {
            if (exclusionRoot == root && glob.IsMatch(relativePath))
            {
                return reason;
            }
        }

        foreach (var (exclusion, glob) in exclusions)
        {
            if (exclusion.Root == root && glob.IsMatch(relativePath))
            {
                return "Exclude_User";
            }
        }

        return null;
    }

    /// <summary>Sets or replaces the override for exactly this pattern.</summary>
    public RuleSet WithOverride(string root, string pattern, ApplyRule? rule)
    {
        var copy = new RuleFile
        {
            Areas = new(File.Areas),
            Parts = new(File.Parts),
            Overrides = File.Overrides.Where(o => !(o.Root == root && string.Equals(o.Pattern, pattern, StringComparison.OrdinalIgnoreCase))).ToList(),
            Exclusions = [.. File.Exclusions],
        };
        if (rule is not null)
        {
            copy.Overrides.Add(new RuleOverride { Root = root, Pattern = pattern, Rule = rule.Value });
        }

        return new RuleSet(copy);
    }

    /// <summary>Adds or removes a user exclusion for exactly this pattern.</summary>
    public RuleSet WithExclusion(string root, string pattern, bool excluded)
    {
        var copy = new RuleFile
        {
            Areas = new(File.Areas),
            Parts = new(File.Parts),
            Overrides = [.. File.Overrides],
            Exclusions = File.Exclusions.Where(e => !(e.Root == root && string.Equals(e.Pattern, pattern, StringComparison.OrdinalIgnoreCase))).ToList(),
        };
        if (excluded)
        {
            copy.Exclusions.Add(new UserExclusion { Root = root, Pattern = pattern });
        }

        return new RuleSet(copy);
    }

    /// <summary>
    /// The rule a matrix row has without a user override on it: its area's switch, or the
    /// matrix default for files outside every area.
    /// </summary>
    public ApplyRule DefaultFor(string root, string pattern)
    {
        var sample = pattern.Replace("**", "x", StringComparison.Ordinal).Replace('*', 'x').Replace('?', 'x');
        var areas = Rules.Areas.Of(root, sample);
        return areas.Count > 0
            ? (areas.Any(a => IsAreaOn(a.Key)) ? ApplyRule.Always : ApplyRule.Never)
            : FileMatrix.Lookup(root, sample).DefaultRule;
    }

    /// <summary>Whether an area is taken from other devices on this device.</summary>
    public bool IsAreaOn(string key) => File.Areas.TryGetValue(key, out var on) ? on : Rules.Areas.DefaultOn(key);

    /// <summary>
    /// Switches an area. An override on exactly one of its patterns is removed, so the switch is
    /// what decides; overrides on single plugins or narrower patterns stay.
    /// </summary>
    public RuleSet WithArea(string key, bool on)
    {
        var area = Rules.Areas.All.FirstOrDefault(a => a.Key == key);
        var copy = new RuleFile
        {
            Areas = new(File.Areas) { [key] = on },
            Parts = new(File.Parts),
            Overrides = File.Overrides
                .Where(o => area is null || !area.Patterns.Any(p => p.Root == o.Root && string.Equals(p.Glob.Pattern, o.Pattern.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            Exclusions = [.. File.Exclusions],
        };
        return new RuleSet(copy);
    }

    /// <summary>Whether a part of a settings file is taken: its area and the part itself are on.</summary>
    public bool IsPartOn(SettingPart part) => IsAreaOn(part.AreaKey) && IsPartSwitchOn(part.Key);

    /// <summary>The part's own switch, regardless of its area.</summary>
    public bool IsPartSwitchOn(string key) => !File.Parts.TryGetValue(key, out var on) || on;

    /// <summary>Switches a part of a settings file.</summary>
    public RuleSet WithPart(string key, bool on) => new(new RuleFile
    {
        Areas = new(File.Areas),
        Parts = new(File.Parts) { [key] = on },
        Overrides = [.. File.Overrides],
        Exclusions = [.. File.Exclusions],
    });

    /// <summary>
    /// For a settings file split by part (<see cref="SettingParts"/>) when some of its parts are
    /// off: which settings keep this device's values. Null when the file is taken whole, not at
    /// all, or a rule on the file decides.
    /// </summary>
    public Func<string, bool>? KeepLocalKeys(string root, string relativePath)
    {
        if (!SettingParts.IsSplitFile(root, relativePath) || HasOverride(root, relativePath)
            || SettingParts.OfFile(relativePath).All(IsPartOn)
            || !SettingParts.OfFile(relativePath).Any(IsPartOn))
        {
            return null;
        }

        return setting => !IsPartOn(SettingParts.PartOf(relativePath, setting));
    }

    private bool HasOverride(string root, string relativePath) => overrides.Any(o => o.Override.Root == root && o.Glob.IsMatch(relativePath));
}
