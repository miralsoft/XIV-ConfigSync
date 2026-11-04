using System.Globalization;
using System.Text.RegularExpressions;
using XivConfigSync.Core.IO;
using XivConfigSync.Core.Model;

namespace XivConfigSync.Core.Store;

/// <summary>
/// The sync folder: a sync file (<c>*.xivsync</c>) and one part per device below <c>devices\</c>.
/// A device writes only its own part; every other part is read only (architecture, "One writer per folder").
/// </summary>
public sealed partial class SyncStore
{
    /// <summary>The extension of the sync file.</summary>
    public const string SyncFileExtension = ".xivsync";

    private const string DevicesFolder = "devices";

    private SyncStore(string root, string syncFilePath, SyncFileContent content)
    {
        Root = root;
        SyncFilePath = syncFilePath;
        Content = content;
    }

    /// <summary>The sync folder.</summary>
    public string Root { get; }

    /// <summary>The sync file inside it.</summary>
    public string SyncFilePath { get; }

    /// <summary>What the sync file says.</summary>
    public SyncFileContent Content { get; }

    /// <summary>How a chosen path relates to a sync folder.</summary>
    public enum FolderKind
    {
        /// <summary>The folder does not exist or is empty: a new sync folder can be made there.</summary>
        Empty,

        /// <summary>The folder holds exactly one valid sync file.</summary>
        SyncFolder,

        /// <summary>The folder holds files but no sync file; it is refused.</summary>
        Foreign,

        /// <summary>The folder holds more than one sync file, or one that cannot be read; it is refused.</summary>
        Invalid,
    }

    /// <summary>Accepts a folder or a sync file and returns the folder.</summary>
    public static string FolderOf(string path) =>
        path.EndsWith(SyncFileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? Path.GetDirectoryName(Path.GetFullPath(path))!
            : Path.GetFullPath(path);

    /// <summary>Tells what a chosen folder is, without changing anything.</summary>
    public static FolderKind Inspect(string folder)
    {
        if (File.Exists(folder))
        {
            // A file is never a folder; FolderOf turns a sync file into its folder first.
            return FolderKind.Invalid;
        }

        if (!Directory.Exists(folder))
        {
            return FolderKind.Empty;
        }

        var syncFiles = Directory.GetFiles(folder, "*" + SyncFileExtension, SearchOption.TopDirectoryOnly);
        if (syncFiles.Length > 1)
        {
            return FolderKind.Invalid;
        }

        if (syncFiles.Length == 1)
        {
            return TryReadSyncFile(syncFiles[0]) is null ? FolderKind.Invalid : FolderKind.SyncFolder;
        }

        var ignorable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "thumbs.db" };
        var hasContent = Directory.EnumerateFileSystemEntries(folder).Any(entry => !ignorable.Contains(Path.GetFileName(entry)));
        return hasContent ? FolderKind.Foreign : FolderKind.Empty;
    }

    /// <summary>Makes a new sync folder. Refuses a folder that is not empty.</summary>
    public static SyncStore Create(string folder, string deviceId)
    {
        var kind = Inspect(folder);
        if (kind != FolderKind.Empty)
        {
            throw new InvalidOperationException($"The folder is not empty ({kind}).");
        }

        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "XIV ConfigSync";
        }

        var content = new SyncFileContent { Created = DateTimeOffset.UtcNow, CreatedBy = deviceId };
        var syncFile = Path.Combine(folder, name + SyncFileExtension);
        FileOps.WriteJsonAtomic(syncFile, content);
        Directory.CreateDirectory(Path.Combine(folder, DevicesFolder));
        return new SyncStore(Path.GetFullPath(folder), syncFile, content);
    }

    /// <summary>Opens an existing sync folder, given the folder or its sync file.</summary>
    public static SyncStore Open(string path)
    {
        var folder = FolderOf(path);
        if (Inspect(folder) != FolderKind.SyncFolder)
        {
            throw new InvalidOperationException("The folder is not a sync folder of this application.");
        }

        var syncFile = Directory.GetFiles(folder, "*" + SyncFileExtension, SearchOption.TopDirectoryOnly)[0];
        var content = TryReadSyncFile(syncFile)!;
        return new SyncStore(folder, syncFile, content);
    }

    /// <summary>Makes a new random device ID.</summary>
    public static string NewDeviceId() => Guid.NewGuid().ToString("N");

    /// <summary>Makes a revision ID that sorts by time.</summary>
    public static string NewRevisionId(string deviceId, DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + deviceId[..8];

    /// <summary>Whether a text is a device ID this application makes.</summary>
    public static bool IsDeviceId(string? value) => value is not null && DeviceIdPattern().IsMatch(value);

    /// <summary>Whether a text is a revision ID this application makes.</summary>
    public static bool IsRevisionId(string? value) => value is not null && RevisionIdPattern().IsMatch(value);

    /// <summary>Whether a text is a SHA-256 in lower-case hex.</summary>
    public static bool IsHash(string? value) => value is not null && HashPattern().IsMatch(value);

    /// <summary>The folder of one device's part.</summary>
    public string DeviceFolder(string deviceId)
    {
        if (!IsDeviceId(deviceId))
        {
            throw new ArgumentException("Invalid device ID.", nameof(deviceId));
        }

        return Path.Combine(Root, DevicesFolder, deviceId);
    }

    /// <summary>Every device that has a valid <c>device.json</c>.</summary>
    public IReadOnlyList<DeviceInfo> ListDevices()
    {
        var devicesRoot = Path.Combine(Root, DevicesFolder);
        if (!Directory.Exists(devicesRoot))
        {
            return [];
        }

        var result = new List<DeviceInfo>();
        foreach (var folder in Directory.GetDirectories(devicesRoot))
        {
            var id = Path.GetFileName(folder);
            if (!IsDeviceId(id))
            {
                continue;
            }

            try
            {
                var info = FileOps.ReadJson<DeviceInfo>(Path.Combine(folder, "device.json"));
                if (info is not null && info.DeviceId == id)
                {
                    result.Add(info);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // A device part that cannot be read is skipped, not fatal: it belongs to another device.
            }
        }

        return result.OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Writes this device's <c>device.json</c>; only ever called for the own device.</summary>
    public void WriteDeviceInfo(DeviceInfo info)
    {
        var folder = DeviceFolder(info.DeviceId);
        Directory.CreateDirectory(Path.Combine(folder, "objects"));
        Directory.CreateDirectory(Path.Combine(folder, "revisions"));
        Directory.CreateDirectory(Path.Combine(folder, "received"));
        FileOps.WriteJsonAtomic(Path.Combine(folder, "device.json"), info);
    }

    /// <summary>The ID in a device's <c>HEAD</c>, or null when it has no complete revision.</summary>
    public string? ReadHead(string deviceId)
    {
        var path = Path.Combine(DeviceFolder(deviceId), "HEAD");
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path).Trim();
        return IsRevisionId(text) ? text : null;
    }

    /// <summary>Revision IDs of a device up to its <c>HEAD</c>, newest first. Revisions after it are incomplete.</summary>
    public IReadOnlyList<string> ListRevisions(string deviceId)
    {
        var head = ReadHead(deviceId);
        var folder = Path.Combine(DeviceFolder(deviceId), "revisions");
        if (head is null || !Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(id => IsRevisionId(id) && string.CompareOrdinal(id, head) <= 0)
            .OrderByDescending(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads and validates a revision manifest.</summary>
    public RevisionManifest ReadRevision(string deviceId, string revisionId)
    {
        if (!IsRevisionId(revisionId))
        {
            throw new ArgumentException("Invalid revision ID.", nameof(revisionId));
        }

        var path = Path.Combine(DeviceFolder(deviceId), "revisions", revisionId + ".json");
        var manifest = FileOps.ReadJson<RevisionManifest>(path)
            ?? throw new FileNotFoundException("Revision not found.", revisionId);
        Validate(manifest, deviceId, revisionId);
        return manifest;
    }

    /// <summary>The path of an object in a device's part.</summary>
    public string ObjectPath(string deviceId, string hash)
    {
        if (!IsHash(hash))
        {
            throw new ArgumentException("Invalid hash.", nameof(hash));
        }

        return Path.Combine(DeviceFolder(deviceId), "objects", hash[..2], hash);
    }

    /// <summary>Stores content in the own part, unless an object with this hash is already there and intact.</summary>
    public bool PutObject(string ownDeviceId, ReadOnlySpan<byte> content, string hash)
    {
        var path = ObjectPath(ownDeviceId, hash);
        if (File.Exists(path))
        {
            if (string.Equals(FileOps.HashFile(path), hash, StringComparison.Ordinal))
            {
                return false;
            }
        }

        FileOps.WriteAtomic(path, content, hash);
        return true;
    }

    /// <summary>Reads an object and checks its hash; throws when it is missing or damaged.</summary>
    public byte[] ReadObject(string deviceId, string hash)
    {
        var path = ObjectPath(deviceId, hash);
        var (content, actual) = FileOps.ReadStable(path);
        if (!string.Equals(actual, hash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A stored file does not match its hash.");
        }

        return content;
    }

    /// <summary>
    /// Publishes a revision of the own device: the manifest first, then <c>HEAD</c>. Every object
    /// must already be stored.
    /// </summary>
    public void Publish(RevisionManifest manifest)
    {
        Validate(manifest, manifest.DeviceId, manifest.RevisionId);
        foreach (var (_, file) in manifest.AllFiles())
        {
            if (!File.Exists(ObjectPath(manifest.DeviceId, file.Hash)))
            {
                throw new InvalidOperationException("A revision cannot be published before all of its files are stored.");
            }
        }

        var folder = DeviceFolder(manifest.DeviceId);
        FileOps.WriteJsonAtomic(Path.Combine(folder, "revisions", manifest.RevisionId + ".json"), manifest);
        FileOps.WriteTextAtomic(Path.Combine(folder, "HEAD"), manifest.RevisionId + "\n");
    }

    /// <summary>Checks that every object of a revision exists and matches its hash.</summary>
    public IReadOnlyList<string> FindDamaged(RevisionManifest manifest)
    {
        var damaged = new List<string>();
        foreach (var (root, file) in manifest.AllFiles())
        {
            try
            {
                var path = ObjectPath(manifest.DeviceId, file.Hash);
                if (!File.Exists(path) || !string.Equals(FileOps.HashFile(path), file.Hash, StringComparison.Ordinal))
                {
                    damaged.Add(root + "/" + file.Path);
                }
            }
            catch (IOException)
            {
                damaged.Add(root + "/" + file.Path);
            }
        }

        return damaged;
    }

    /// <summary>Records in the own part that a revision of another device was received and verified.</summary>
    public void WriteReceived(string ownDeviceId, string otherDeviceId, string revisionId)
    {
        if (!IsDeviceId(otherDeviceId) || !IsRevisionId(revisionId))
        {
            throw new ArgumentException("Invalid device or revision ID.");
        }

        var marker = new ReceivedMarker { RevisionId = revisionId, Verified = DateTimeOffset.UtcNow };
        FileOps.WriteJsonAtomic(Path.Combine(DeviceFolder(ownDeviceId), "received", otherDeviceId + ".json"), marker);
    }

    /// <summary>Which revision of <paramref name="aboutDeviceId"/> the device <paramref name="readerDeviceId"/> has verified.</summary>
    public ReceivedMarker? ReadReceived(string readerDeviceId, string aboutDeviceId)
    {
        try
        {
            var marker = FileOps.ReadJson<ReceivedMarker>(Path.Combine(DeviceFolder(readerDeviceId), "received", aboutDeviceId + ".json"));
            return marker is not null && IsRevisionId(marker.RevisionId) ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the own device's revisions beyond the newest <paramref name="keep"/>, except those
    /// another device marked as received last, then deletes objects no kept revision uses.
    /// </summary>
    public (int Revisions, int Objects) ApplyRetention(string ownDeviceId, int keep, IReadOnlySet<string> protectedIds) =>
        ApplyRetention(ownDeviceId, keep, protectedIds, TimeSpan.FromDays(2), DateTime.UtcNow);

    /// <summary>
    /// As above; objects are deleted only when older than <paramref name="grace"/>, so an object
    /// whose manifest has not arrived yet (or was written by a second PC under the same ID by
    /// mistake) is never taken away (review finding 9).
    /// </summary>
    public (int Revisions, int Objects) ApplyRetention(string ownDeviceId, int keep, IReadOnlySet<string> protectedIds, TimeSpan grace, DateTime nowUtc)
    {
        var revisions = ListRevisions(ownDeviceId);
        var receivedByOthers = ListDevices()
            .Where(device => device.DeviceId != ownDeviceId)
            .Select(device => ReadReceived(device.DeviceId, ownDeviceId)?.RevisionId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var keepIds = revisions.Take(keep).Concat(receivedByOthers).Concat(protectedIds).ToHashSet(StringComparer.Ordinal);
        var folder = DeviceFolder(ownDeviceId);
        var removed = 0;
        foreach (var id in revisions.Where(id => !keepIds.Contains(id)))
        {
            File.Delete(Path.Combine(folder, "revisions", id + ".json"));
            removed++;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ListRevisions(ownDeviceId))
        {
            foreach (var (_, file) in ReadRevision(ownDeviceId, id).AllFiles())
            {
                used.Add(file.Hash);
            }
        }

        var objectsRoot = Path.Combine(folder, "objects");
        var deleted = 0;
        if (Directory.Exists(objectsRoot))
        {
            foreach (var path in Directory.EnumerateFiles(objectsRoot, "*", SearchOption.AllDirectories).ToList())
            {
                var name = Path.GetFileName(path);
                if (IsHash(name) && !used.Contains(name) && nowUtc - File.GetLastWriteTimeUtc(path) > grace)
                {
                    File.Delete(path);
                    deleted++;
                }
            }
        }

        return (removed, deleted);
    }

    private static SyncFileContent? TryReadSyncFile(string path)
    {
        try
        {
            var content = FileOps.ReadJson<SyncFileContent>(path);
            return content is { Kind: SyncFileContent.ExpectedKind, Schema: >= 1 } ? content : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static void Validate(RevisionManifest manifest, string deviceId, string revisionId)
    {
        if (manifest.Schema < 1 || manifest.DeviceId != deviceId || manifest.RevisionId != revisionId)
        {
            throw new InvalidDataException("The revision does not belong where it was found.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, file) in manifest.AllFiles())
        {
            if (!seen.Add(root + "/" + file.Path))
            {
                throw new InvalidDataException("The revision lists a file twice and is rejected as a whole.");
            }

            if (!Model.Roots.All.Contains(root) || !IsHash(file.Hash) || !RelativePath.TryNormalize(file.Path, out var normalized) || normalized != file.Path)
            {
                throw new InvalidDataException("The revision contains an invalid entry and is rejected as a whole.");
            }
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex DeviceIdPattern();

    [GeneratedRegex("^[0-9]{8}T[0-9]{9}Z-[0-9a-f]{8}$")]
    private static partial Regex RevisionIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();
}
