using XivConfigSync.Core.Model;

namespace XivConfigSync.Core.Rules;

/// <summary>
/// A group of settings inside a settings file of the game, such as "chat: font size and
/// transparency" (owner's request, 2026-11-06). It belongs to one area and can be switched on its
/// own. Settings are matched by name only, never by position.
/// </summary>
/// <param name="Key">The stored key, "area.part".</param>
/// <param name="AreaKey">The area it belongs to.</param>
/// <param name="FileName">The settings file it is in.</param>
/// <param name="Prefixes">Setting names starting with one of these belong to it.</param>
public sealed record SettingPart(string Key, string AreaKey, string FileName, IReadOnlyList<string> Prefixes)
{
    /// <summary>The resource key of the name.</summary>
    public string NameKey => "Part_" + Key.Replace('.', '_');

    /// <summary>Whether a setting belongs to it; an empty list takes every setting not claimed before.</summary>
    public bool Matches(string setting) => Prefixes.Count == 0 || Prefixes.Any(p => setting.StartsWith(p, StringComparison.Ordinal));
}

/// <summary>
/// The parts of the game's plain-text settings files (<c>COMMON.DAT</c>, <c>CONTROL0.DAT</c>,
/// <c>CONTROL1.DAT</c>). The order matters: the first part that matches a setting takes it, and the
/// last part of each file takes everything else, also settings a later game version adds.
/// </summary>
public static class SettingParts
{
    public const string Common = "COMMON.DAT";
    public const string Control0 = "CONTROL0.DAT";
    public const string Control1 = "CONTROL1.DAT";

    private static readonly string[] ControlFiles = [Control0, Control1];

    /// <summary>All parts, in the order they claim settings.</summary>
    public static IReadOnlyList<SettingPart> All { get; } = Build();

    /// <summary>The parts shown under an area, without duplicates for the two control files.</summary>
    public static IReadOnlyList<SettingPart> OfArea(string areaKey) =>
        [.. All.Where(p => p.AreaKey == areaKey).DistinctBy(p => p.Key)];

    /// <summary>The settings files that are split by part.</summary>
    public static bool IsSplitFile(string root, string path)
    {
        if (root != Roots.Game || !path.StartsWith("FFXIV_CHR", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = System.IO.Path.GetFileName(path);
        return string.Equals(name, Common, StringComparison.OrdinalIgnoreCase) || ControlFiles.Any(c => string.Equals(name, c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The part a setting of a file belongs to.</summary>
    public static SettingPart PartOf(string path, string setting)
    {
        var name = System.IO.Path.GetFileName(path).ToUpperInvariant();
        return All.First(p => p.FileName == name && p.Matches(setting));
    }

    /// <summary>The parts of one file.</summary>
    public static IReadOnlyList<SettingPart> OfFile(string path)
    {
        var name = System.IO.Path.GetFileName(path).ToUpperInvariant();
        return [.. All.Where(p => p.FileName == name)];
    }

    private static List<SettingPart> Build()
    {
        List<SettingPart> parts =
        [
            // COMMON.DAT. Name plates first: one of their settings begins with "Log".
            new("character.nameplates", Areas.Character, Common, ["NamePlate", "PlateType", "PlateDisp", "PlateDisable", "LogNamePlate", "TargetNamePlate", "FocusTargetNamePlate"]),
            new("chat.font", Areas.Chat, Common, ["LogFontSize", "LogPermeationRate"]),
            new("chat.time", Areas.Chat, Common, ["LogTimeDisp", "LogTimeSetting"]),
            new("chat.bubbles", Areas.Chat, Common, ["LogChatBubble"]),
            new("chat.colors", Areas.Chat, Common, ["Color", "LogSetRoleColor", "LogColor"]),
            new("chat.tabs", Areas.Chat, Common, ["Log", "IsLog", "ChatType"]),
            new("hotbars.display", Areas.Hotbars, Common, ["Hotbar", "ExHotbar"]),
            new("character.camera", Areas.Character, Common, ["FirstPerson", "ThirdPerson", "Lockon", "Camera", "FPSCamera", "LegacyCamera", "EventCamera", "IdlingCamera", "GPose", "Gpose", "GroupPose"]),
            new("character.battle", Areas.Character, Common, ["BattleEffect", "Target", "GroundTarget", "AutoNearestTarget", "RightClickExclusion", "PetTargetOffInCombat", "EnemyListCastbar"]),
            new("character.pets", Areas.Character, Common, ["Egi", "Bahamut", "Phoenix", "Garuda", "Titan", "Ifrit", "SolBahamut", "PetMirage"]),
            new("character.sizes", Areas.Character, Common, ["MapDispSize", "ToolTipDispSize", "FlyTextDispSize", "PopUpTextDispSize", "ItemInventryWindowSizeType", "ItemInventryRetainerWindowSizeType"]),
            new("character.other", Areas.Character, Common, []),
        ];

        foreach (var file in ControlFiles)
        {
            parts.Add(new("controls.camera", Areas.Controls, file, ["AutoChangePointOfView", "KeyboardCamera", "TiltOffset", "KeyboardSpeed", "PadSpeed", "PadFps", "PadTps", "MouseFps", "MouseTps", "Flying"]));
            parts.Add(new("controls.target", Areas.Controls, file, ["AutoFaceTarget", "SelfClick", "NoTargetClick", "AutoTarget", "TargetTypeSelect", "AutoLockOn", "Circle", "GroundTarget", "TargetCircle", "TargetLine", "LinkLine", "ObjectBordering", "MoveMode"]));
            parts.Add(new("controls.display", Areas.Controls, file, []));
        }

        return parts;
    }
}
