using System.Text;
using System.Text.RegularExpressions;
using XivConfigSync.Core.Model;

namespace XivConfigSync.Core.Rules;

/// <summary>
/// Looks into text files for credentials and absolute paths (FR-025). It only reads; what it finds
/// becomes a flag on the file, never a log line with the value (FR-063).
/// </summary>
public static partial class ContentScanner
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".cfg", ".ini", ".txt", ".yaml", ".yml", ".xml", ".toml", ".conf",
    };

    /// <summary>The flags for a file's content.</summary>
    public static List<string> Scan(string root, string relativePath, ReadOnlySpan<byte> content)
    {
        var flags = new List<string>();
        if (content.Length > FileMatrix.LargeFileSize)
        {
            flags.Add(FileFlags.Large);
        }

        if (root == Roots.Game && string.Equals(relativePath, "FFXIV.cfg", StringComparison.OrdinalIgnoreCase))
        {
            // Holds the last login and a session ID (decisions, 2026-11-04).
            flags.Add(FileFlags.Credential);
            return flags;
        }

        if (!TextExtensions.Contains(Path.GetExtension(relativePath)) || content.Length > FileMatrix.LargeFileSize)
        {
            return flags;
        }

        var text = Encoding.UTF8.GetString(content);
        if (CredentialPattern().IsMatch(text))
        {
            flags.Add(FileFlags.Credential);
        }

        if (AbsolutePathPattern().IsMatch(text))
        {
            flags.Add(FileFlags.AbsolutePath);
        }

        return flags;
    }

    // A key that names a secret, followed by a non-empty value of some length.
    [GeneratedRegex("\"?(api[_-]?key|apikey|access[_-]?token|auth[_-]?token|token|secret|password|passwd)\"?\\s*[:=]\\s*\"?[A-Za-z0-9_\\-\\.+/=]{12,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CredentialPattern();

    // A drive path such as C:\\Users or C:/Users inside text.
    [GeneratedRegex("[A-Za-z]:(\\\\\\\\|\\\\|/)[A-Za-z0-9]", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AbsolutePathPattern();
}
