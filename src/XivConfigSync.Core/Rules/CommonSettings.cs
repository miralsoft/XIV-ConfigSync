using System.Text;

namespace XivConfigSync.Core.Rules;

/// <summary>
/// The character settings file <c>COMMON.DAT</c>: plain text, one <c>Key&lt;TAB&gt;Value</c> per
/// line, section headers in angle brackets, empty lines, and a single NUL near the end
/// (file matrix: format confirmed). Splitting it lets one area be taken from another device while
/// the other keeps this device's values, by key only: no position in the file is assumed.
/// </summary>
public static class CommonSettings
{
    /// <summary>
    /// The source file with the values of the kept keys replaced by this device's values. Lines,
    /// order, line ends and everything else come from the source. Null when either file has a
    /// line that is not of the known shape, so nothing of another shape is ever written.
    /// </summary>
    public static byte[]? Merge(byte[] source, byte[] local, Func<string, bool> keepLocal)
    {
        var sourceLines = Split(source);
        var localLines = Split(local);
        if (sourceLines is null || localLines is null)
        {
            return null;
        }

        // A setting can occur more than once (measured: LockonDefaultZoom, with two values); the
        // n-th occurrence in the source takes the n-th occurrence here.
        var localValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in localLines)
        {
            if (line.Key is not null)
            {
                if (!localValues.TryGetValue(line.Key, out var values))
                {
                    values = [];
                    localValues[line.Key] = values;
                }

                values.Add(line.Value!);
            }
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var output = new StringBuilder(source.Length);
        foreach (var line in sourceLines)
        {
            var occurrence = 0;
            if (line.Key is not null)
            {
                occurrence = seen.GetValueOrDefault(line.Key);
                seen[line.Key] = occurrence + 1;
            }

            if (line.Key is not null && keepLocal(line.Key) && localValues.TryGetValue(line.Key, out var values) && occurrence < values.Count)
            {
                output.Append(line.Key).Append('\t').Append(values[occurrence]).Append(line.End);
            }
            else
            {
                output.Append(line.Text).Append(line.End);
            }
        }

        return Latin1.GetBytes(output.ToString());
    }

    /// <summary>
    /// The file without the kept keys, to compare two versions in what is taken from another
    /// device only. Null when the file is not of the known shape.
    /// </summary>
    public static byte[]? Comparable(byte[] content, Func<string, bool> keepLocal)
    {
        var lines = Split(content);
        if (lines is null)
        {
            return null;
        }

        var output = new StringBuilder(content.Length);
        foreach (var line in lines.Where(l => l.Key is null || !keepLocal(l.Key)))
        {
            output.Append(line.Text).Append(line.End);
        }

        return Latin1.GetBytes(output.ToString());
    }

    // Latin-1 maps every byte to one char and back, so bytes outside ASCII survive unchanged.
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private sealed record Line(string Text, string End, string? Key, string? Value);

    private static List<Line>? Split(byte[] content)
    {
        var text = Latin1.GetString(content);
        var lines = new List<Line>();
        var start = 0;
        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline + 1;
            var raw = text[start..end];
            var body = raw.TrimEnd('\r', '\n');
            var lineEnd = raw[body.Length..];
            start = end;

            if (body.Length == 0 || body == "\0" || (body.StartsWith('<') && body.EndsWith('>')))
            {
                lines.Add(new Line(body, lineEnd, null, null));
                continue;
            }

            // The NUL may follow the last value on the same line.
            var trailing = body.EndsWith('\0') ? "\0" : string.Empty;
            var pair = body[..(body.Length - trailing.Length)];
            var tab = pair.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0 || pair.IndexOf('\t', tab + 1) >= 0 || pair.Contains('\0', StringComparison.Ordinal) || !IsKey(pair[..tab]))
            {
                return null;
            }

            lines.Add(new Line(body, lineEnd, pair[..tab], pair[(tab + 1)..] + trailing));
        }

        return lines;
    }

    private static bool IsKey(string key) => key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
