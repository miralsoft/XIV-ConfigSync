using XivConfigSync.Core.Platform;
using XivConfigSync.Core.ViewModels;
using Xunit;

namespace XivConfigSync.Core.Tests;

/// <summary>The set-up wizard's finish, found in the owner's first real set-up (2026-11-05).</summary>
public sealed class SetupTests
{
    [Fact]
    public async Task FinishReportsCompletionOnceAndNoLongerWorking()
    {
        using var temp = new TempFolder();
        var device = new Device(temp, "a");
        var model = new SetupViewModel(device.Engine, new QuietUi(), new NoShell(), new NoLocator(), null)
        {
            Folder = temp.Sub("sync"),
            DeviceName = "A",
            Step = 3,
        };
        var completions = 0;
        var workingAtCompletion = true;
        model.Completed += (_, _) =>
        {
            completions++;
            workingAtCompletion = model.Working;
        };

        await model.NextAsync();

        // The window refuses to close while the set-up works; Completed closes it.
        Assert.Equal(1, completions);
        Assert.False(workingAtCompletion);
        Assert.True(model.IsFinished);
        Assert.True(device.Settings.IsSetUp);

        // A second click on Finish neither runs the set-up again nor reports completion again.
        // A changed file would make a second run publish a new revision.
        var head = device.Engine.GetDevices().Single().HeadRevisionId;
        Assert.NotNull(head);
        device.WriteGame(Device.Chr + "/HOTBAR.DAT", Device.Random());
        await model.NextAsync();
        Assert.Equal(1, completions);
        Assert.Equal(head, device.Engine.GetDevices().Single().HeadRevisionId);
    }
}

/// <summary>User interface services that answer without a window; counts what was asked.</summary>
internal sealed class QuietUi : IUiServices
{
    private readonly System.Threading.Lock uiThread = new();

    public bool ConfirmAnswer { get; set; } = true;

    public int Confirmations { get; private set; }

    public List<string> Informed { get; } = [];

    public string? PickFolder(string title, string? initial) => null;

    public bool Confirm(string title, string message)
    {
        Confirmations++;
        return ConfirmAnswer;
    }

    public bool? Ask(string title, string message) => true;

    public void Inform(string title, string message) => Informed.Add(message);

    public void ShowError(string message) => throw new InvalidOperationException(message);

    public void Notify(string title, string message)
    {
    }

    // One action at a time, like the real UI thread: the view models rely on it.
    public bool OnUiThread(Action action)
    {
        lock (uiThread)
        {
            action();
        }

        return true;
    }

    public void OpenFolder(string path)
    {
    }
}

internal sealed class NoShell : IShellIntegration
{
    public bool IsAutostartEnabled => false;

    public bool IsFileAssociationRegistered { get; private set; }

    public void SetAutostart(bool enabled)
    {
    }

    public void SetFileAssociation(bool enabled) => IsFileAssociationRegistered = enabled;
}

internal sealed class NoLocator : IGameLocator
{
    public IReadOnlyList<string> InstallFolderCandidates() => [];
}

/// <summary>A Recycle Bin that moves files into a folder of its own and records them.</summary>
internal sealed class FakeRecycleBin(string folder) : IRecycleBin
{
    public List<string> Recycled { get; } = [];

    public void Recycle(string fullPath)
    {
        Directory.CreateDirectory(folder);
        File.Move(fullPath, Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(fullPath)));
        Recycled.Add(fullPath);
    }
}
