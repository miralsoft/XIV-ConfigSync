using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XivConfigSync.Core.Dalamud;

/// <summary>A custom plugin repository in Dalamud's settings.</summary>
public sealed record PluginRepository(string Url, bool IsEnabled);

/// <summary>
/// Adds the custom plugin repositories another device has and this one lacks to this device's
/// <c>dalamudConfig.json</c>. Only the repository list changes, and only by appending: entries
/// already there are never removed, reordered or changed, and every other setting stays as it is
/// (owner's request, 2026-11-05). The structure was read from a real file: Dalamud writes the
/// list as <c>ThirdRepoList</c>, an object with <c>$type</c> and <c>$values</c>, each entry with
/// <c>$type</c>, <c>Url</c> and <c>IsEnabled</c>. A file of another shape is refused, not guessed at.
/// </summary>
public static class RepositoryMerge
{
    public const string FileName = "dalamudConfig.json";

    private const string ListKey = "ThirdRepoList";
    private const string ValuesKey = "$values";

    /// <summary>The repositories in a settings file.</summary>
    /// <exception cref="InvalidDataException">The file is not a settings file of the known shape.</exception>
    public static IReadOnlyList<PluginRepository> Read(byte[] content) =>
        [.. Values(Parse(content)).Select(ToRepository)];

    /// <summary>The repositories of the source that this device lacks, in the source's order.</summary>
    public static IReadOnlyList<PluginRepository> Missing(byte[] local, byte[] source)
    {
        var have = Read(local).Select(r => Key(r.Url)).ToHashSet(StringComparer.Ordinal);
        var missing = new List<PluginRepository>();
        foreach (var repository in Read(source))
        {
            if (have.Add(Key(repository.Url)))
            {
                missing.Add(repository);
            }
        }

        return missing;
    }

    /// <summary>
    /// The local file with the source's missing repositories appended, copied entry by entry from
    /// the source, or null when nothing is missing.
    /// </summary>
    public static byte[]? Merge(byte[] local, byte[] source)
    {
        var localRoot = Parse(local);
        var localValues = Values(localRoot);
        var have = localValues.Select(ToRepository).Select(r => Key(r.Url)).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var entry in Values(Parse(source)))
        {
            if (have.Add(Key(ToRepository(entry).Url)))
            {
                localValues.Add(entry!.DeepClone());
                added++;
            }
        }

        if (added == 0)
        {
            return null;
        }

        // Dalamud writes two-space indentation; non-ASCII characters stay as they are, and the line
        // ends stay those of the file. Set explicitly: the default follows the machine, which on
        // Windows is already CRLF (a conversion on top doubled the CR, found on a real file).
        var original = Encoding.UTF8.GetString(local);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NewLine = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
        };
        var text = localRoot.ToJsonString(options);

        var bom = local.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var body = Encoding.UTF8.GetBytes(text);
        return bom ? [.. Encoding.UTF8.Preamble, .. body] : body;
    }

    /// <summary>A repository address compared without case, surrounding space or a trailing slash.</summary>
    public static string Key(string url) => url.Trim().TrimEnd('/').ToUpperInvariant();

    private static JsonObject Parse(byte[] content)
    {
        try
        {
            var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject
                ?? throw new InvalidDataException("Not a settings object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Not readable as JSON.", ex);
        }
    }

    private static JsonArray Values(JsonObject root)
    {
        if (root[ListKey] is JsonObject list && list[ValuesKey] is JsonArray values
            && values.All(v => v is JsonObject entry && entry["Url"] is JsonValue url && url.TryGetValue<string>(out _)))
        {
            return values;
        }

        throw new InvalidDataException("The repository list has an unknown shape.");
    }

    private static PluginRepository ToRepository(JsonNode? node)
    {
        var entry = (JsonObject)node!;
        var enabled = entry["IsEnabled"] is JsonValue value && value.TryGetValue<bool>(out var on) && on;
        return new PluginRepository(entry["Url"]!.GetValue<string>(), enabled);
    }
}
