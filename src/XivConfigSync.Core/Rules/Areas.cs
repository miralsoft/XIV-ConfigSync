using XivConfigSync.Core.Model;

namespace XivConfigSync.Core.Rules;

/// <summary>What happens when both devices changed a file of an area.</summary>
public enum ConflictPolicy
{
    /// <summary>The user decides.</summary>
    Ask,

    /// <summary>
    /// The state backed up last wins. For files the game rewrites on its own (V00), where a
    /// question would come after nearly every session.
    /// </summary>
    NewestWins,
}

/// <summary>
/// One area a player recognises, such as "Hotbars and macros" (owner's request, 2026-11-06): the
/// application decides which files belong to it, the player only switches it on or off per device.
/// </summary>
/// <param name="Key">The stored key.</param>
/// <param name="Patterns">The files of the area, per root.</param>
/// <param name="Conflicts">What happens when both devices changed a file.</param>
public sealed record Area(string Key, IReadOnlyList<(string Root, Glob Glob)> Patterns, ConflictPolicy Conflicts)
{
    /// <summary>The resource key of the name.</summary>
    public string NameKey => "Area_" + Key;

    /// <summary>The resource key of the description.</summary>
    public string DescriptionKey => "Area_" + Key + "_Hint";

    /// <summary>Whether a file belongs to the area.</summary>
    public bool Contains(string root, string path) => Patterns.Any(p => p.Root == root && p.Glob.IsMatch(path));
}

/// <summary>The built-in areas and which files belong to which.</summary>
public static class Areas
{
    public const string Hotbars = "hotbars";
    public const string Controls = "controls";
    public const string Gearsets = "gearsets";
    public const string Character = "character";
    public const string Chat = "chat";
    public const string Hud = "hud";
    public const string Inventory = "inventory";
    public const string Plugins = "plugins";

    /// <summary>Not a set of files: the custom plugin repositories merged into Dalamud's settings.</summary>
    public const string Repositories = "repositories";

    /// <summary>The character settings file that holds both <see cref="Character"/> and <see cref="Chat"/>.</summary>
    public const string CommonFile = "COMMON.DAT";

    private const string Chr = "FFXIV_CHR*/";

    /// <summary>All areas that are sets of files, in the order shown.</summary>
    public static IReadOnlyList<Area> All { get; } =
    [
        new(Hotbars, Game(Chr + "HOTBAR.DAT", Chr + "MACRO.DAT", "MACROSYS.dat", Chr + CommonFile), ConflictPolicy.Ask),
        new(Controls, Game(Chr + "KEYBIND.DAT", Chr + "CONTROL0.DAT", Chr + "CONTROL1.DAT"), ConflictPolicy.Ask),
        new(Gearsets, Game(Chr + "GEARSET.DAT", Chr + "GS.DAT"), ConflictPolicy.Ask),
        new(Character, Game(Chr + CommonFile, Chr + "ACQ.DAT", Chr + "ITEMFDR.DAT", "FFXIV_CHARA_*.dat"), ConflictPolicy.Ask),
        new(Chat, Game(Chr + "LOGFLTR.DAT", Chr + CommonFile), ConflictPolicy.Ask),
        new(Hud, Game(Chr + "ADDON.DAT"), ConflictPolicy.NewestWins),
        new(Inventory, Game(Chr + "ITEMODR.DAT"), ConflictPolicy.NewestWins),
        new(Plugins, [(Roots.Plugins, new Glob("**"))], ConflictPolicy.Ask),
    ];

    /// <summary>The keys of everything that can be switched, the repositories included.</summary>
    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(a => a.Key), Repositories];

    /// <summary>Every area is on unless the user turned it off on this device.</summary>
    public static bool DefaultOn(string key) => true;

    /// <summary>The areas a file belongs to; several only for <see cref="CommonFile"/>, whose settings are split by part.</summary>
    public static IReadOnlyList<Area> Of(string root, string path) => [.. All.Where(a => a.Contains(root, path))];

    private static IReadOnlyList<(string Root, Glob Glob)> Game(params string[] patterns) =>
        [.. patterns.Select(p => (Roots.Game, new Glob(p)))];
}
