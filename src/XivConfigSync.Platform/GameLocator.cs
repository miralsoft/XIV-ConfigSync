using System.Text.Json;
using Microsoft.Win32;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Platform;

/// <summary>
/// Finds the game's installation folder read-only: the uninstall entry of the standard edition,
/// XIVLauncher's configured game path, and the default folder (measured on device A, gate 3).
/// </summary>
public sealed class GameLocator : IGameLocator
{
    private const string GameFolderName = "FINAL FANTASY XIV - A Realm Reborn";

    public IReadOnlyList<string> InstallFolderCandidates()
    {
        var candidates = new List<string>();
        AddFromUninstall(candidates);
        AddFromLauncher(candidates);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(programFilesX86, "SquareEnix", GameFolderName));
        candidates.Add(Path.Combine(programFilesX86, "Steam", "steamapps", "common", "FINAL FANTASY XIV Online"));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddFromUninstall(List<string> candidates)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string display && display.Contains("FINAL FANTASY XIV", StringComparison.OrdinalIgnoreCase)
                        && entry.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                    {
                        candidates.Add(Path.Combine(location, GameFolderName));
                        candidates.Add(location);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Not readable: other candidates remain.
            }
        }
    }

    private static void AddFromLauncher(List<string> candidates)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "launcherConfigV3.json");
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("GamePath", out var gamePath) && gamePath.ValueKind == JsonValueKind.String)
            {
                candidates.Add(gamePath.GetString()!);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Not readable: other candidates remain.
        }
    }
}
