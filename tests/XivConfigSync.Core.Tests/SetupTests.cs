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

    private sealed class QuietUi : IUiServices
    {
        public string? PickFolder(string title, string? initial) => null;

        public bool Confirm(string title, string message) => true;

        public bool? Ask(string title, string message) => true;

        public void Inform(string title, string message)
        {
        }

        public void ShowError(string message) => throw new InvalidOperationException(message);

        public void Notify(string title, string message)
        {
        }

        public bool OnUiThread(Action action)
        {
            action();
            return true;
        }

        public void OpenFolder(string path)
        {
        }
    }

    private sealed class NoShell : IShellIntegration
    {
        public bool IsAutostartEnabled => false;

        public bool IsFileAssociationRegistered { get; private set; }

        public void SetAutostart(bool enabled)
        {
        }

        public void SetFileAssociation(bool enabled) => IsFileAssociationRegistered = enabled;
    }

    private sealed class NoLocator : IGameLocator
    {
        public IReadOnlyList<string> InstallFolderCandidates() => [];
    }
}
