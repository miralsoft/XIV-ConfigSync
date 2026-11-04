using System.Diagnostics;
using XivConfigSync.Core.Platform;

namespace XivConfigSync.Platform;

/// <summary>
/// Watches for the game process. It looks every few seconds by process name, which costs a process
/// list, and additionally reacts at once when the watched process exits (gate 3, measurement 7).
/// Read-only towards the game (X-04).
/// </summary>
public sealed class GameMonitor : IGameMonitor
{
    /// <summary>
    /// The processes that keep the configuration folder busy: the game client, and the game's own
    /// launcher and configuration tool, which edit FFXIV.cfg and FFXIV_BOOT.cfg (review 3, finding 11).
    /// The Steam edition runs the same client executable.
    /// </summary>
    public static readonly IReadOnlyList<string> ProcessNames = ["ffxiv_dx11", "ffxiv", "ffxivboot", "ffxivboot64", "ffxivlauncher", "ffxivlauncher64", "ffxivconfig", "ffxivconfig64"];

    private readonly object pollGate = new();

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
        // One poll at a time: the timer and the exit event must not report out of order.
        if (!Monitor.TryEnter(pollGate))
        {
            return;
        }

        try
        {
            PollOnce();
        }
        finally
        {
            Monitor.Exit(pollGate);
        }
    }

    private void PollOnce()
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
