using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XivConfigSync.Core.Logging;

/// <summary>Log levels (FR-063).</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>One log line kept in memory for the log page.</summary>
public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Correlation, string Message);

/// <summary>
/// A small file log with daily files, rotation and redaction: character content IDs become a
/// pseudonym (X-06), user folders lose the user name. Values of files are never logged.
/// </summary>
public sealed partial class Log
{
    private const int KeepFiles = 14;
    private const int KeepInMemory = 500;
    private readonly object gate = new();
    private readonly string? folder;
    private readonly LinkedList<LogEntry> recent = new();

    public Log(string? folder)
    {
        this.folder = folder;
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
            Prune();
        }
    }

    /// <summary>A log that writes nothing; for tests.</summary>
    public static Log None { get; } = new(null);

    /// <summary>The lowest level written.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Raised for each entry written.</summary>
    public event EventHandler<LogEntry>? Written;

    /// <summary>The newest entries, oldest first.</summary>
    public IReadOnlyList<LogEntry> Recent
    {
        get
        {
            lock (gate)
            {
                return recent.ToList();
            }
        }
    }

    /// <summary>Removes what must not reach a log.</summary>
    public static string Redact(string text)
    {
        var result = CharacterIdPattern().Replace(text, "FFXIV_CHR<id>");
        result = UserFolderPattern().Replace(result, "$1<user>");
        return result;
    }

    public void Debug(string correlation, string message) => Write(LogLevel.Debug, correlation, message);

    public void Info(string correlation, string message) => Write(LogLevel.Info, correlation, message);

    public void Warning(string correlation, string message) => Write(LogLevel.Warning, correlation, message);

    public void Error(string correlation, string message) => Write(LogLevel.Error, correlation, message);

    /// <summary>Writes an entry.</summary>
    public void Write(LogLevel level, string correlation, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, correlation, Redact(message));
        lock (gate)
        {
            recent.AddLast(entry);
            while (recent.Count > KeepInMemory)
            {
                recent.RemoveFirst();
            }

            if (folder is not null)
            {
                try
                {
                    var line = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff zzz} {level,-7} [{correlation}] {entry.Message}{Environment.NewLine}");
                    File.AppendAllText(Path.Combine(folder, $"xivconfigsync-{entry.Time:yyyyMMdd}.log"), line, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // A log that cannot be written must never stop the application.
                }
            }
        }

        Written?.Invoke(this, entry);
    }

    private void Prune()
    {
        try
        {
            foreach (var old in Directory.GetFiles(folder!, "xivconfigsync-*.log").Order(StringComparer.Ordinal).SkipLast(KeepFiles))
            {
                File.Delete(old);
            }
        }
        catch (IOException)
        {
            // Rotation is best effort.
        }
    }

    [GeneratedRegex("FFXIV_CHR[0-9A-Fa-f]{16}")]
    private static partial Regex CharacterIdPattern();

    [GeneratedRegex("([A-Za-z]:\\\\Users\\\\)[^\\\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserFolderPattern();
}
