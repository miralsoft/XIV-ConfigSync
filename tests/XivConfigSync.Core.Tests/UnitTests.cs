using System.Text;
using XivConfigSync.Core.IO;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;
using XivConfigSync.Core.Store;
using Xunit;

namespace XivConfigSync.Core.Tests;

public sealed class RelativePathTests
{
    [Theory]
    [InlineData("FFXIV.cfg", "FFXIV.cfg")]
    [InlineData(@"FFXIV_CHR0011223344556677\HOTBAR.DAT", "FFXIV_CHR0011223344556677/HOTBAR.DAT")]
    [InlineData("a/b/c.json", "a/b/c.json")]
    public void AcceptsSafePaths(string input, string expected)
    {
        Assert.True(RelativePath.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil.dat")]
    [InlineData("a/../../evil.dat")]
    [InlineData("/abs.dat")]
    [InlineData(@"C:\Windows\x.dll")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("a/b?.txt")]
    [InlineData("trailing. ")]
    public void RejectsUnsafePaths(string input) => Assert.False(RelativePath.TryNormalize(input, out _));

    [Fact]
    public void CombineStaysInsideTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");
        Assert.StartsWith(Path.GetFullPath(root), RelativePath.Combine(root, "x/y.dat"), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() => RelativePath.Combine(root, "../y.dat"));
    }
}

public sealed class RuleTests
{
    private static readonly RuleSet Defaults = new(new RuleFile());

    [Theory]
    [InlineData(Roots.Game, "FFXIV.cfg", ApplyRule.Never)]
    [InlineData(Roots.Game, "FFXIV_BOOT.cfg", ApplyRule.Never)]
    [InlineData(Roots.Game, "cfgcopy/FFXIV_CFGCPY0000000003D8CE4F.dat", ApplyRule.Never)]
    [InlineData(Roots.Game, "MACROSYS.dat", ApplyRule.Always)]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/HOTBAR.DAT", ApplyRule.Always)]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/KEYBIND.DAT", ApplyRule.Always)]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/CONTROL0.DAT", ApplyRule.Always)]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/ADDON.DAT", ApplyRule.OnRequest)]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/UISAVE.DAT", ApplyRule.OnRequest)]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/MACRO.DAT", ApplyRule.OnRequest)]
    [InlineData(Roots.Game, "something/unknown.bin", ApplyRule.OnRequest)]
    [InlineData(Roots.Plugins, "SomePlugin.json", ApplyRule.OnRequest)]
    [InlineData(Roots.Plugins, "Deep/Folder/x.json", ApplyRule.OnRequest)]
    public void MatrixDefaults(string root, string path, ApplyRule expected) => Assert.Equal(expected, Defaults.RuleFor(root, path));

    [Theory]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/HOTBAR.DAT.old", "Exclude_Old")]
    [InlineData(Roots.Game, "FFXIV-NB-Device-2.cfg", "Exclude_ConflictCopy")]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/ADDON-NB-Device-24.DAT", "Exclude_ConflictCopy")]
    [InlineData(Roots.Game, "screenshots/ffxiv_001.png", "Exclude_NotConfig")]
    [InlineData(Roots.Plugins, "Nav/cache/mesh.bin", "Exclude_Cache")]
    [InlineData(Roots.Plugins, "Plugin/output.log", "Exclude_Log")]
    public void Exclusions(string root, string path, string reason) => Assert.Equal(reason, Defaults.ExclusionReason(root, path));

    [Theory]
    [InlineData(Roots.Game, "FFXIV.cfg")]
    [InlineData(Roots.Game, "FFXIV_CHR0011223344556677/HOTBAR.DAT")]
    [InlineData(Roots.Plugins, "SomePlugin.json")]
    public void NotExcluded(string root, string path) => Assert.Null(Defaults.ExclusionReason(root, path));

    [Fact]
    public void OverridesWinAndTheMostSpecificWins()
    {
        var rules = Defaults
            .WithOverride(Roots.Game, "FFXIV_CHR*/ADDON.DAT", ApplyRule.Always)
            .WithOverride(Roots.Game, "FFXIV_CHR*/*.DAT", ApplyRule.Never);
        Assert.Equal(ApplyRule.Always, rules.RuleFor(Roots.Game, "FFXIV_CHR0011223344556677/ADDON.DAT"));
        Assert.Equal(ApplyRule.Never, rules.RuleFor(Roots.Game, "FFXIV_CHR0011223344556677/HOTBAR.DAT"));
        Assert.Equal(ApplyRule.OnRequest, rules.WithOverride(Roots.Game, "FFXIV_CHR*/ADDON.DAT", null).WithOverride(Roots.Game, "FFXIV_CHR*/*.DAT", null).RuleFor(Roots.Game, "FFXIV_CHR0011223344556677/ADDON.DAT"));
    }

    [Fact]
    public void UserExclusion()
    {
        var rules = Defaults.WithExclusion(Roots.Plugins, "Secret*.json", true);
        Assert.Equal("Exclude_User", rules.ExclusionReason(Roots.Plugins, "SecretPlugin.json"));
        Assert.Null(rules.WithExclusion(Roots.Plugins, "Secret*.json", false).ExclusionReason(Roots.Plugins, "SecretPlugin.json"));
    }

    [Theory]
    [InlineData("**/*.old", "a.old", true)]
    [InlineData("**/*.old", "x/y/a.old", true)]
    [InlineData("*.json", "x/a.json", false)]
    [InlineData("FFXIV_CHR*/HOTBAR.DAT", "ffxiv_chr00aa/hotbar.dat", true)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "a/c", false)]
    public void GlobMatching(string pattern, string path, bool expected) => Assert.Equal(expected, new Glob(pattern).IsMatch(path));
}

public sealed class ContentScannerTests
{
    [Fact]
    public void FlagsCredentialsAndPaths()
    {
        var json = "{ \"ApiKey\": \"abcdefghijklmnop1234\", \"Folder\": \"C:\\\\Users\\\\x\\\\Docs\" }";
        var flags = ContentScanner.Scan(Roots.Plugins, "P.json", Encoding.UTF8.GetBytes(json));
        Assert.Contains(FileFlags.Credential, flags);
        Assert.Contains(FileFlags.AbsolutePath, flags);
    }

    [Fact]
    public void PlainSettingsAreNotFlagged()
    {
        var flags = ContentScanner.Scan(Roots.Plugins, "P.json", Encoding.UTF8.GetBytes("{ \"Enabled\": true, \"Token\": \"\" }"));
        Assert.Empty(flags);
    }

    [Fact]
    public void GameConfigIsAlwaysSensitive() =>
        Assert.Contains(FileFlags.Credential, ContentScanner.Scan(Roots.Game, "FFXIV.cfg", [1, 2, 3]));

    [Fact]
    public void BinaryFilesAreNotRead() =>
        Assert.Empty(ContentScanner.Scan(Roots.Game, "FFXIV_CHR0011223344556677/HOTBAR.DAT", Encoding.UTF8.GetBytes("pass" + "word=abcdefghijklmnop")));
}

public sealed class SyncStoreTests
{
    [Fact]
    public void InspectTellsTheKindOfFolder()
    {
        using var temp = new TempFolder();
        Assert.Equal(SyncStore.FolderKind.Empty, SyncStore.Inspect(temp.Sub("missing")));

        var foreign = temp.Sub("foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "notes.txt"), "x");
        Assert.Equal(SyncStore.FolderKind.Foreign, SyncStore.Inspect(foreign));
        Assert.Throws<Engine.RefusedException>(() => SyncStore.Create(foreign, SyncStore.NewDeviceId()));

        var sync = temp.Sub("FFXIV Sync");
        var store = SyncStore.Create(sync, SyncStore.NewDeviceId());
        Assert.Equal(SyncStore.FolderKind.SyncFolder, SyncStore.Inspect(sync));
        Assert.Equal("FFXIV Sync.xivsync", Path.GetFileName(store.SyncFilePath));
        Assert.Equal(store.Root, SyncStore.Open(store.SyncFilePath).Root);

        Assert.Equal(SyncStore.FolderKind.Invalid, SyncStore.Inspect(store.SyncFilePath));
        Assert.Equal(store.Root, SyncStore.FolderOf(store.SyncFilePath));

        File.WriteAllText(Path.Combine(sync, "second.xivsync"), "{}");
        Assert.Equal(SyncStore.FolderKind.Invalid, SyncStore.Inspect(sync));
    }

    [Fact]
    public void ARenamedSyncFileStillWorks()
    {
        using var temp = new TempFolder();
        var store = SyncStore.Create(temp.Sub("S"), SyncStore.NewDeviceId());
        File.Move(store.SyncFilePath, Path.Combine(store.Root, "Renamed.xivsync"));
        Assert.Equal(SyncStore.FolderKind.SyncFolder, SyncStore.Inspect(store.Root));
    }

    [Fact]
    public void ARevisionWithoutHeadIsIgnoredAndAnInvalidOneRejected()
    {
        using var temp = new TempFolder();
        var device = SyncStore.NewDeviceId();
        var store = SyncStore.Create(temp.Sub("S"), device);
        store.WriteDeviceInfo(new DeviceInfo { DeviceId = device, DisplayName = "A" });

        var content = Encoding.UTF8.GetBytes("x");
        var hash = FileOps.HashBytes(content);
        store.PutObject(device, content, hash);
        var first = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(device, DateTimeOffset.UtcNow.AddMinutes(-1)), DeviceId = device };
        first.Roots[Roots.Game] = new RootEntry { Files = [new FileEntry { Path = "a.dat", Hash = hash, Size = 1 }] };
        store.Publish(first);

        // A manifest written without HEAD, as by a crash between the two steps.
        var orphan = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(device, DateTimeOffset.UtcNow), DeviceId = device };
        FileOps.WriteJsonAtomic(Path.Combine(store.DeviceFolder(device), "revisions", orphan.RevisionId + ".json"), orphan);
        Assert.Equal([first.RevisionId], store.ListRevisions(device));

        var evil = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(device, DateTimeOffset.UtcNow.AddMinutes(1)), DeviceId = device };
        evil.Roots[Roots.Game] = new RootEntry { Files = [new FileEntry { Path = "../../evil.dll", Hash = hash }] };
        Assert.Throws<InvalidDataException>(() => store.Publish(evil));
    }

    [Fact]
    public void ADamagedObjectIsFound()
    {
        using var temp = new TempFolder();
        var device = SyncStore.NewDeviceId();
        var store = SyncStore.Create(temp.Sub("S"), device);
        store.WriteDeviceInfo(new DeviceInfo { DeviceId = device, DisplayName = "A" });
        var content = Encoding.UTF8.GetBytes("payload");
        var hash = FileOps.HashBytes(content);
        store.PutObject(device, content, hash);
        var revision = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(device, DateTimeOffset.UtcNow), DeviceId = device };
        revision.Roots[Roots.Game] = new RootEntry { Files = [new FileEntry { Path = "a.dat", Hash = hash, Size = content.Length }] };
        store.Publish(revision);
        Assert.Empty(store.FindDamaged(revision));

        File.WriteAllText(store.ObjectPath(device, hash), "tampered");
        Assert.Single(store.FindDamaged(revision));
        Assert.Throws<InvalidDataException>(() => store.ReadObject(device, hash));
    }

    [Fact]
    public void RetentionKeepsTheNewestAndWhatOthersReceived()
    {
        using var temp = new TempFolder();
        var a = SyncStore.NewDeviceId();
        var b = SyncStore.NewDeviceId();
        var store = SyncStore.Create(temp.Sub("S"), a);
        store.WriteDeviceInfo(new DeviceInfo { DeviceId = a, DisplayName = "A" });
        store.WriteDeviceInfo(new DeviceInfo { DeviceId = b, DisplayName = "B" });
        var ids = new List<string>();
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < 6; i++)
        {
            var content = Encoding.UTF8.GetBytes("v" + i);
            var hash = FileOps.HashBytes(content);
            store.PutObject(a, content, hash);
            var revision = new RevisionManifest { RevisionId = SyncStore.NewRevisionId(a, start.AddMinutes(i)), DeviceId = a };
            revision.Roots[Roots.Game] = new RootEntry { Files = [new FileEntry { Path = "f.dat", Hash = hash, Size = content.Length }] };
            store.Publish(revision);
            ids.Add(revision.RevisionId);
        }

        store.WriteReceived(b, a, ids[1]);
        // Fresh objects are kept for a grace period; here the grace is zero.
        var early = store.ApplyRetention(a, 2, new HashSet<string>());
        Assert.Equal(3, early.Revisions);
        Assert.Equal(0, early.Objects);
        var (revisions, objects) = store.ApplyRetention(a, 2, new HashSet<string>(), TimeSpan.Zero, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(0, revisions);
        Assert.Equal(3, objects);
        Assert.Equal([ids[5], ids[4], ids[1]], store.ListRevisions(a));
    }
}
