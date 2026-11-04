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

        return best?.Override.Rule ?? FileMatrix.Lookup(root, relativePath).DefaultRule;
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
            Overrides = [.. File.Overrides],
            Exclusions = File.Exclusions.Where(e => !(e.Root == root && string.Equals(e.Pattern, pattern, StringComparison.OrdinalIgnoreCase))).ToList(),
        };
        if (excluded)
        {
            copy.Exclusions.Add(new UserExclusion { Root = root, Pattern = pattern });
        }

        return new RuleSet(copy);
    }
}
