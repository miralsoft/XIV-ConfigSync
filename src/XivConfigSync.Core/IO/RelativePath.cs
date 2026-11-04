namespace XivConfigSync.Core.IO;

/// <summary>
/// A path relative to a configuration root, always with '/' as separator. Every path read from a
/// manifest or built from a scan goes through <see cref="TryNormalize"/>, so nothing outside the
/// root can ever be addressed (FR-067).
/// </summary>
public static class RelativePath
{
    private static readonly System.Buffers.SearchValues<char> InvalidChars = System.Buffers.SearchValues.Create("<>:\"|?*\0");

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Checks a relative path and returns it with '/' separators.</summary>
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 400)
        {
            return false;
        }

        var unified = path.Replace('\\', '/');
        if (unified.StartsWith('/') || unified.EndsWith('/'))
        {
            return false;
        }

        var segments = unified.Split('/');
        foreach (var segment in segments)
        {
            if (!IsValidSegment(segment))
            {
                return false;
            }
        }

        normalized = string.Join('/', segments);
        return true;
    }

    /// <summary>Returns the normalized path or throws when it is not a safe relative path.</summary>
    public static string Normalize(string path)
    {
        if (!TryNormalize(path, out var normalized))
        {
            throw new InvalidDataException($"Not a safe relative path: '{path}'.");
        }

        return normalized;
    }

    /// <summary>
    /// Combines a root with a relative path and checks that the result stays inside the root.
    /// </summary>
    public static string Combine(string root, string relativePath)
    {
        var normalized = Normalize(relativePath);
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path leaves its root: '{relativePath}'.");
        }

        return full;
    }

    /// <summary>The relative path of a file below a root, with '/' separators.</summary>
    public static string FromFullPath(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(fullPath));
        return Normalize(relative);
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment == "." || segment == "..")
        {
            return false;
        }

        if (segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            return false;
        }

        if (segment.AsSpan().IndexOfAny(InvalidChars) >= 0 || segment.Any(c => c < 32))
        {
            return false;
        }

        var stem = segment.Split('.')[0];
        return !ReservedNames.Contains(stem);
    }
}
