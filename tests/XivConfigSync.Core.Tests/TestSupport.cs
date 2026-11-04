using System.Security.Cryptography;
using XivConfigSync.Core.Apply;
using XivConfigSync.Core.Engine;
using XivConfigSync.Core.Logging;
using XivConfigSync.Core.Platform;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Settings;

namespace XivConfigSync.Core.Tests;

/// <summary>A temporary folder removed after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xivcs-test-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Left for the system to clean up.
        }
    }
}

/// <summary>A game monitor the test controls.</summary>
internal sealed class FakeGame : IGameMonitor
{
    private bool running;
    private int checksUntilStart = -1;

    public event EventHandler<bool>? RunningChanged;

    /// <summary>The game "starts" after this many more checks of <see cref="IsRunning"/>.</summary>
    public void StartAfterChecks(int checks) => checksUntilStart = checks;

    public bool IsRunning
    {
        get
        {
            if (checksUntilStart == 0)
            {
                running = true;
                checksUntilStart = -1;
            }
            else if (checksUntilStart > 0)
            {
                checksUntilStart--;
            }

            return running;
        }
        set
        {
            running = value;
            RunningChanged?.Invoke(this, value);
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>A cloud state reader that returns a fixed state.</summary>
internal sealed class FakeCloud(CloudFileState state) : ICloudStateReader
{
    public CloudFileState GetState(string path) => state;
}

/// <summary>Throws before a chosen stage, to simulate a crash there.</summary>
internal sealed class CrashProbe(string stage, int index) : IApplyProbe
{
    public void Before(string stage1, int index1)
    {
        if (stage1 == stage && index1 == index)
        {
            throw new SimulatedCrashException();
        }
    }
}

internal sealed class SimulatedCrashException : Exception
{
    public SimulatedCrashException()
        : base("Simulated crash.")
    {
    }
}

/// <summary>
/// One simulated device: its own game and plugin folders and local data, sharing a sync folder with
/// others. File contents are random bytes; the product treats files as opaque, so that is enough
/// and no real game data is used (X-07, X-09).
/// </summary>
internal sealed class Device
{
    public const string Chr = "FFXIV_CHR0011223344556677";

    public Device(TempFolder temp, string name, IApplyProbe? probe = null)
    {
        Name = name;
        GameFolder = temp.Sub(name + "-game");
        PluginFolder = temp.Sub(name + "-plugins");
        Local = new LocalData(temp.Sub(name + "-local"));
        Directory.CreateDirectory(Path.Combine(GameFolder, Chr));
        Directory.CreateDirectory(PluginFolder);
        Settings = new AppSettings
        {
            GameConfigFolder = GameFolder,
            PluginConfigFolder = PluginFolder,
        };
        Game = new FakeGame();
        Engine = new SyncEngine(Local, Settings, new RuleSet(new RuleFile()), Game, new FakeCloud(CloudFileState.InSync), Log.None, probe);
    }

    public string Name { get; }

    public string GameFolder { get; }

    public string PluginFolder { get; }

    public LocalData Local { get; }

    public AppSettings Settings { get; }

    public FakeGame Game { get; }

    public SyncEngine Engine { get; }

    public static byte[] Random(int size = 512) => RandomNumberGenerator.GetBytes(size);

    public string GamePath(string relative) => Path.Combine(GameFolder, relative.Replace('/', Path.DirectorySeparatorChar));

    public void WriteGame(string relative, byte[] content)
    {
        var path = GamePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public byte[] ReadGame(string relative) => File.ReadAllBytes(GamePath(relative));

    public bool ExistsGame(string relative) => File.Exists(GamePath(relative));

    /// <summary>A typical game folder.</summary>
    public void Populate()
    {
        WriteGame("FFXIV.cfg", Random());
        WriteGame("FFXIV_BOOT.cfg", Random());
        WriteGame("MACROSYS.dat", Random());
        WriteGame(Chr + "/HOTBAR.DAT", Random());
        WriteGame(Chr + "/KEYBIND.DAT", Random());
        WriteGame(Chr + "/ADDON.DAT", Random());
        WriteGame(Chr + "/MACRO.DAT", Random());
        WriteGame(Chr + "/HOTBAR.DAT.old", Random());
        File.WriteAllText(Path.Combine(PluginFolder, "SomePlugin.json"), "{ \"Enabled\": true }");
    }
}
