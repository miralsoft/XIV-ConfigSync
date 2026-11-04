using System.Text.RegularExpressions;

namespace XivConfigSync.Core.Discovery;

/// <summary>Finds the configuration folders, the characters and the game version, read-only.</summary>
public static partial class GameFolders
{
    /// <summary>The game's configuration folder below Documents (standard and Steam edition alike).</summary>
    public static string DefaultGameConfigFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "FINAL FANTASY XIV - A Realm Reborn");

    /// <summary>
    /// Dalamud's plugin configuration folder. With the sandbox mode Dalamud keeps its data below
    /// <c>dalamudUserData</c>; that folder wins when it exists.
    /// </summary>
    public static string? FindPluginConfigFolder()
    {
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher");
        var sandbox = Path.Combine(launcher, "dalamudUserData", "pluginConfigs");
        if (Directory.Exists(sandbox))
        {
            return sandbox;
        }

        var classic = Path.Combine(launcher, "pluginConfigs");
        return Directory.Exists(classic) ? classic : null;
    }

    /// <summary>The character folders (<c>FFXIV_CHR*</c>) in a game configuration folder.</summary>
    public static IReadOnlyList<string> FindCharacterFolders(string? gameConfigFolder)
    {
        if (gameConfigFolder is null || !Directory.Exists(gameConfigFolder))
        {
            return [];
        }

        return Directory.GetDirectories(gameConfigFolder, "FFXIV_CHR*")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => CharacterFolderPattern().IsMatch(name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether a folder looks like the game's configuration folder.</summary>
    public static bool LooksLikeGameConfigFolder(string? folder) =>
        folder is not null && Directory.Exists(folder)
        && (File.Exists(Path.Combine(folder, "FFXIV.cfg")) || FindCharacterFolders(folder).Count > 0);

    /// <summary>The game version from <c>game\ffxivgame.ver</c> in an installation folder (measured in gate 3).</summary>
    public static string? ReadGameVersion(string? installFolder)
    {
        if (installFolder is null)
        {
            return null;
        }

        foreach (var candidate in new[]
        {
            Path.Combine(installFolder, "game", "ffxivgame.ver"),
            Path.Combine(installFolder, "FINAL FANTASY XIV - A Realm Reborn", "game", "ffxivgame.ver"),
        })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var text = File.ReadAllText(candidate).Trim();
                    if (VersionPattern().IsMatch(text))
                    {
                        return text;
                    }
                }
            }
            catch (IOException)
            {
                // Unreadable: treated as unknown.
            }
        }

        return null;
    }

    /// <summary>The installation folder that holds the version file, from candidates.</summary>
    public static string? PickInstallFolder(IEnumerable<string> candidates) =>
        candidates.FirstOrDefault(candidate => ReadGameVersion(candidate) is not null);

    [GeneratedRegex("^FFXIV_CHR[0-9A-Fa-f]{16}$")]
    private static partial Regex CharacterFolderPattern();

    [GeneratedRegex("^[0-9]{4}\\.[0-9]{2}\\.[0-9]{2}\\.[0-9]{4}\\.[0-9]{4}$")]
    private static partial Regex VersionPattern();
}
