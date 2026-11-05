namespace XivConfigSync.Core.Platform;

/// <summary>Watches the game process. Implemented in the platform project.</summary>
public interface IGameMonitor : IDisposable
{
    /// <summary>Whether the game runs now.</summary>
    bool IsRunning { get; }

    /// <summary>Raised when the game starts or exits; the argument is the new running state.</summary>
    event EventHandler<bool>? RunningChanged;
}

/// <summary>The upload state of one file in a cloud-synced folder.</summary>
public enum CloudFileState
{
    /// <summary>Not a cloud folder, or the state cannot be read.</summary>
    Unknown,

    /// <summary>Written locally, upload pending.</summary>
    Pending,

    /// <summary>The cloud client reports the file in sync.</summary>
    InSync,
}

/// <summary>Reads the cloud client's state of a file without opening it (FR-049).</summary>
public interface ICloudStateReader
{
    /// <summary>The state of one file.</summary>
    CloudFileState GetState(string path);
}

/// <summary>Where the game is installed and which version it is.</summary>
public interface IGameLocator
{
    /// <summary>Candidate installation folders, most likely first.</summary>
    IReadOnlyList<string> InstallFolderCandidates();
}

/// <summary>Integration with the Windows shell: autostart and the file association (X-08).</summary>
public interface IShellIntegration
{
    bool IsAutostartEnabled { get; }

    void SetAutostart(bool enabled);

    bool IsFileAssociationRegistered { get; }

    void SetFileAssociation(bool enabled);
}

/// <summary>Moves files to the Windows Recycle Bin, so a removal can be undone there.</summary>
public interface IRecycleBin
{
    /// <summary>
    /// Moves one file to the Recycle Bin. Throws when the file cannot go there; it never deletes
    /// a file for good instead.
    /// </summary>
    void Recycle(string fullPath);
}
