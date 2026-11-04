using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XivConfigSync.Core.IO;

/// <summary>File operations every component uses: hashing, atomic writes, stable reads.</summary>
public static class FileOps
{
    /// <summary>Lower-case hexadecimal SHA-256 of a file, read with sharing so the game can keep it open.</summary>
    public static string HashFile(string path)
    {
        using var stream = OpenShared(path);
        return HashStream(stream);
    }

    /// <summary>Lower-case hexadecimal SHA-256 of a stream from its current position.</summary>
    public static string HashStream(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Lower-case hexadecimal SHA-256 of bytes.</summary>
    public static string HashBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Opens a file for reading while allowing others to read, write and delete it.</summary>
    public static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);

    /// <summary>
    /// Reads a file and its hash so that the hash belongs to exactly the bytes returned. A file that
    /// changes while it is read is read again, up to <paramref name="attempts"/> times.
    /// </summary>
    public static (byte[] Content, string Hash) ReadStable(string path, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            var before = new FileInfo(path);
            var lengthBefore = before.Length;
            var writeBefore = before.LastWriteTimeUtc;
            byte[] content;
            using (var stream = OpenShared(path))
            {
                content = new byte[stream.Length];
                stream.ReadExactly(content);
            }

            var after = new FileInfo(path);
            if (after.Length == lengthBefore && after.LastWriteTimeUtc == writeBefore && content.Length == lengthBefore)
            {
                return (content, HashBytes(content));
            }

            if (attempt >= attempts)
            {
                throw new IOException($"The file kept changing while it was read: {Path.GetFileName(path)}");
            }

            Thread.Sleep(200 * attempt);
        }
    }

    /// <summary>
    /// Writes bytes to a temporary file in the target folder, checks them, then renames the
    /// temporary file over the target. A reader sees either the old or the new file, never a mix.
    /// </summary>
    public static void WriteAtomic(string path, ReadOnlySpan<byte> content, string? expectedHash = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(true);
            }

            if (expectedHash is not null)
            {
                var written = HashFile(temp);
                if (!string.Equals(written, expectedHash, StringComparison.Ordinal))
                {
                    throw new IOException($"Written content does not match its hash: {Path.GetFileName(path)}");
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Writes UTF-8 text atomically.</summary>
    public static void WriteTextAtomic(string path, string text) => WriteAtomic(path, Encoding.UTF8.GetBytes(text));

    /// <summary>Serialises an object to indented JSON and writes it atomically.</summary>
    public static void WriteJsonAtomic<T>(string path, T value) =>
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(value, Json.Options));

    /// <summary>Reads a JSON file, or returns null when it does not exist.</summary>
    public static T? ReadJson<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = OpenShared(path);
        return JsonSerializer.Deserialize<T>(stream, Json.Options);
    }

    /// <summary>Copies a file through a temporary file and an atomic rename, checking its hash.</summary>
    public static void CopyAtomic(string source, string destination, string expectedHash)
    {
        var (content, hash) = ReadStable(source);
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
        {
            throw new IOException($"Source does not match its expected hash: {Path.GetFileName(source)}");
        }

        WriteAtomic(destination, content, expectedHash);
    }
}
