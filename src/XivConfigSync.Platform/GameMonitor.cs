using System.Diagnostics;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Platform;

/// <summary>
/// Watches for the game process. While the game is not running it looks every few seconds by
/// process name, which costs a process list; while it runs it waits for the process to exit
/// instead of polling (gate 3, measurement 7). Read-only towards the game (X-04).
/// </summary>
public sealed class GameMonitor : IGameMonitor
{
    /// <summary>The process names of the game client.</summary>
    public static readonly IReadOnlyList<string> ProcessNames = ["ffxiv_dx11", "ffxiv"];

    private readonly Timer timer;
    private readonly object gate = new();
    private Process? watched;
    private bool running;
    private bool disposed;

    public GameMonitor(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(5);
        running = FindGame() is { } found && Watch(found);
        timer = new Timer(_ => Poll(), null, period, period);
    }

    public event EventHandler<bool>? RunningChanged;

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return running;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            watched?.Dispose();
            watched = null;
        }

        timer.Dispose();
    }

    private static Process? FindGame()
    {
        foreach (var name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            var first = processes.FirstOrDefault();
            foreach (var other in processes.Where(p => p != first))
            {
                other.Dispose();
            }

            if (first is not null)
            {
                return first;
            }
        }

        return null;
    }

    private bool Watch(Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnExited;
            lock (gate)
            {
                watched = process;
            }

            return !process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A process we may not open: fall back to polling by name.
            process.Dispose();
            return true;
        }
    }

    private void OnExited(object? sender, EventArgs e) => Poll();

    private void Poll()
    {
        bool changed;
        bool now;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
        }

        var found = FindGame();
        lock (gate)
        {
            now = found is not null;
            changed = now != running;
            running = now;
            if (!now && watched is not null)
            {
                watched.Exited -= OnExited;
                watched.Dispose();
                watched = null;
            }
        }

        if (found is not null)
        {
            lock (gate)
            {
                if (watched is null)
                {
                    Watch(found);
                }
                else
                {
                    found.Dispose();
                }
            }
        }

        if (changed)
        {
            RunningChanged?.Invoke(this, now);
        }
    }
}
