using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Planning;
using XivConfigSync.Core.Rules;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>Every text exists in both languages with the same placeholders (technical gate, localisation).</summary>
public sealed partial class LocalizationTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void BothLanguagesHaveTheSameKeysAndPlaceholders()
    {
        var english = Load(CultureInfo.InvariantCulture);
        var german = Load(German);
        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), german.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, text) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), key);
            Assert.False(string.IsNullOrWhiteSpace(german[key]), key);
            Assert.Equal(Placeholders(text), Placeholders(german[key]));
            Assert.DoesNotContain((char)0x2014, text);
            Assert.DoesNotContain((char)0x2014, german[key]);
        }
    }

    [Fact]
    public void EveryKeyUsedAsDataExists()
    {
        var english = Load(CultureInfo.InvariantCulture);
        var keys = new List<string>();
        keys.AddRange(typeof(PlanReason).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)!));
        keys.AddRange(FileMatrix.Entries.Select(e => e.NoteKey));
        keys.Add(FileMatrix.Lookup(Roots.Game, "unknown/file.bin").NoteKey);
        keys.AddRange(FileMatrix.Exclusions.Select(e => e.ReasonKey));
        keys.AddRange(["Exclude_TooLarge", "Exclude_User"]);
        keys.AddRange(Enum.GetNames<Evidence>().Select(n => "Evidence_" + n));
        keys.AddRange(Enum.GetNames<RevisionOrigin>().Select(n => "Origin_" + n));
        keys.AddRange(Enum.GetNames<Logging.LogLevel>().Select(n => "Level_" + n));
        keys.AddRange(Areas.All.SelectMany(a => new[] { a.NameKey, a.DescriptionKey }));
        keys.AddRange(["Area_" + Areas.Repositories, "Area_" + Areas.Repositories + "_Hint"]);
        keys.AddRange(SettingParts.All.Select(p => p.NameKey));

        // Refusal and warning keys are written as literals in the code; find them there.
        var source = Path.Combine(FindRepositoryRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            keys.AddRange(LiteralKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
        }

        var missing = keys.Distinct().Where(k => !english.ContainsKey(k)).ToList();
        Assert.Empty(missing);
    }

    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new MissingManifestResourceException(culture.Name);
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static List<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToList();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XivConfigSync.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [GeneratedRegex("\\{[0-9]+\\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex("\"((?:Refuse|Warn|Exclude)_[A-Za-z]+)\"")]
    private static partial Regex LiteralKey();
}
