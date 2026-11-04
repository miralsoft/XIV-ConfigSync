using System.Text;
using System.Text.RegularExpressions;

namespace XivConfigSync.Core.Rules;

/// <summary>
/// A path pattern: <c>*</c> matches within one folder, <c>**</c> across folders, <c>?</c> one
/// character. Case-insensitive, '/' as separator, matched against the whole relative path.
/// </summary>
public sealed class Glob
{
    private readonly Regex regex;

    public Glob(string pattern)
    {
        Pattern = pattern.Replace('\\', '/');
        regex = new Regex(ToRegex(Pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        Specificity = Pattern.Count(c => c != '*' && c != '?') - (Pattern.Contains("**", StringComparison.Ordinal) ? 1000 : 0);
    }

    /// <summary>The pattern as given, with '/' separators.</summary>
    public string Pattern { get; }

    /// <summary>Higher for more literal patterns; used to pick the most specific of several matches.</summary>
    public int Specificity { get; }

    /// <summary>Whether a relative path matches.</summary>
    public bool IsMatch(string relativePath) => regex.IsMatch(relativePath);

    private static string ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return builder.Append('$').ToString();
    }
}
