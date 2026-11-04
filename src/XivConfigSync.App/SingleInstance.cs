using System.IO;
using System.IO.Pipes;
using System.Text;

namespace XivConfigSync.App;

/// <summary>
/// Keeps one instance per user. A second start (for example a double-click on a sync file) hands
/// its argument to the running instance through a named pipe and ends.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();

    public SingleInstance(string name)
    {
        var user = Environment.UserName;
        mutex = new Mutex(initiallyOwned: true, $@"Local\{name}-{user}", out var created);
        IsFirst = created;
        pipeName = $"{name}-{user}-pipe";
    }

    /// <summary>Whether this is the first instance.</summary>
    public bool IsFirst { get; }

    /// <summary>Sends an argument to the first instance; true when it was received.</summary>
    public bool SendToFirst(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(message);
            client.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens for arguments from later instances until disposed.</summary>
    public async Task ListenAsync(Action<string> received)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(stop.Token).ConfigureAwait(false);
                received(message);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A broken connection: wait for the next one.
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
        if (IsFirst)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}
