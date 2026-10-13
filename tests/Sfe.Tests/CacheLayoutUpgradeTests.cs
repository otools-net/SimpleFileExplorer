using Microsoft.Extensions.Logging.Abstractions;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;
using Sfe.Core.Upgrades;
using Sfe.Core.Upgrades.Steps;
using Sfe.Core.Upgrades.Steps.V1_40_0;

namespace Sfe.Tests;

/// <summary>
/// The 1.40.0 step moving the thumbnail and trickplay caches from one folder per source folder to two levels of 256
/// folders (<see cref="ShardCacheLayout"/>), and the choice of steps an update runs (<see cref="UpgradeSteps"/>).
/// </summary>
[TestClass]
public sealed class CacheLayoutUpgradeTests
{
    private static readonly DateTime Written = new(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    private string _base = null!;
    private string _cache = null!;
    private readonly List<string> _notes = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        _base = Path.Combine(Path.GetTempPath(), "sfe-cache-" + Guid.NewGuid().ToString("N"));
        _cache = Path.Combine(_base, "thumbs");
        Directory.CreateDirectory(_cache);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_base))
            Directory.Delete(_base, recursive: true);
    }

    [TestMethod]
    public async Task EveryOldEntryIsRenamedToItsNewPlaceWithItsContentAndDate()
    {
        var photo = Source("Albums", "beach day.jpg");
        var other = Source("Albums", "Русский.png");
        var noThumbnail = Source("Clips", "broken.mkv");
        var video = Source("Clips", "holiday.mp4");
        var tenantCache = Path.Combine(_cache, "tenants", "other");
        var tenantPhoto = Source("Elsewhere", "cat.jpg");
        var leftover = OldThumbnail(_cache, photo, [1, 2, 3]) + ".0123.tmp";
        File.WriteAllBytes(leftover, [9]);
        OldThumbnail(_cache, other, [4, 5]);
        OldThumbnail(_cache, noThumbnail, []);
        var sheets = OldTrickplay(_cache, video);
        OldThumbnail(tenantCache, tenantPhoto, [7]);

        await RunAsync(_cache, tenantCache);

        AssertMoved(NewThumbnail(_cache, photo), [1, 2, 3]);
        AssertMoved(NewThumbnail(_cache, other), [4, 5]);
        AssertMoved(NewThumbnail(_cache, noThumbnail), []);
        AssertMoved(NewThumbnail(tenantCache, tenantPhoto), [7]);
        foreach (var (name, content) in sheets)
            AssertMoved(Path.Combine(NewTrickplay(_cache, video), name), content);
        Assert.IsEmpty(Directory.GetDirectories(_cache).Select(Path.GetFileName).Where(name => name!.Length == 40).ToList(), "The old folders are gone");
        Assert.IsEmpty(Directory.GetDirectories(Path.Combine(_cache, TrickplayService.FolderName)).Select(Path.GetFileName).Where(name => name!.Length == 40).ToList());
        Assert.IsTrue(Directory.Exists(tenantCache), "Other libraries' caches stay where they are");
        Assert.IsFalse(File.Exists(leftover));
        Assert.HasCount(3, Directory.GetFiles(_cache, "*.bin", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(Path.Combine(_cache, "tenants")) && !f.StartsWith(Path.Combine(_cache, TrickplayService.FolderName))).ToList(),
            "No thumbnail is left behind or duplicated");
        CollectionAssert.Contains(_notes, $"{_cache}: moved 3 thumbnails and 1 trickplays.");
    }

    [TestMethod]
    public void AKeyIsLowerCaseHexSplitOverTwoLevels()
    {
        var photo = Source("Media", "photo.jpg");

        var key = ThumbnailService.CacheKey(photo);

        Assert.AreEqual(Path.Combine(_cache, key[..2], key[2..4], key[4..] + ".bin"), NewThumbnail(_cache, photo));
        Assert.AreEqual(40, key.Length);
        Assert.AreEqual(key.ToLowerInvariant(), key);
        Assert.AreNotEqual(key, ThumbnailService.CacheKey(Source("Other", "photo.jpg")), "The folder is part of the key");
    }

    [TestMethod]
    public async Task RunningAgainChangesNothingAndANewerEntryAtTheNewPlaceWins()
    {
        var photo = Source("Albums", "beach day.jpg");
        var video = Source("Clips", "holiday.mp4");
        OldThumbnail(_cache, photo, [1]);
        await RunAsync(_cache);
        var snapshot = Snapshot();

        await RunAsync(_cache);
        Assert.AreEqual(snapshot, Snapshot(), "A second run finds nothing to move");

        // An old entry left behind (e.g. it was locked) while this version rendered the file again.
        OldThumbnail(_cache, photo, [2]);
        File.SetLastWriteTimeUtc(NewThumbnail(_cache, photo), Written.AddDays(1));
        OldTrickplay(_cache, video);
        Directory.CreateDirectory(NewTrickplay(_cache, video));
        await File.WriteAllTextAsync(Path.Combine(NewTrickplay(_cache, video), "info.json"), "new", TestContext.CancellationToken);
        await RunAsync(_cache);

        CollectionAssert.AreEqual(new byte[] { 1 }, await File.ReadAllBytesAsync(NewThumbnail(_cache, photo), TestContext.CancellationToken));
        Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(NewTrickplay(_cache, video), "info.json"), TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(NewTrickplay(_cache, video), "0.jpg")), "The newer folder is kept whole");
        Assert.IsEmpty(Directory.GetDirectories(_cache).Select(Path.GetFileName).Where(name => name!.Length == 40).ToList());
    }

    [TestMethod]
    public async Task AnEntryThatCannotBeMovedStaysAndIsReported()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Only Windows refuses to rename an open file.");
        var locked = Source("Albums", "locked.jpg");
        var free = Source("Albums", "free.jpg");
        var lockedFile = OldThumbnail(_cache, locked, [1]);
        OldThumbnail(_cache, free, [2]);

        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            await RunAsync(_cache);

        Assert.IsTrue(File.Exists(lockedFile));
        AssertMoved(NewThumbnail(_cache, free), [2]);
        Assert.IsTrue(_notes.Any(note => note.Contains(lockedFile, StringComparison.Ordinal)), string.Join("\n", _notes));
        CollectionAssert.Contains(_notes, $"{_cache}: moved 1 thumbnails and 0 trickplays; 1 could not be moved and stay in the old layout.");
    }

    [TestMethod]
    public void AnUpdateRunsTheStepsAfterTheInstalledVersionUpToTheRunningOne()
    {
        var app = new SemanticVersion(1, 40, 0);

        CollectionAssert.Contains(UpgradeSteps.Due(null, app).Select(s => s.Version).ToList(), app);
        CollectionAssert.Contains(UpgradeSteps.Due(new SemanticVersion(1, 39, 0), app).Select(s => s.Version).ToList(), app);
        Assert.IsEmpty(UpgradeSteps.Due(app, app));
        Assert.IsEmpty(UpgradeSteps.Due(null, new SemanticVersion(1, 39, 5)), "Never a step of a later version");
        Assert.IsTrue(UpgradeSteps.All.All(step => step.GetType().Namespace!.EndsWith($".V{step.Version.Major}_{step.Version.Minor}_{step.Version.Patch}")),
            "Each step lives in the folder of its version");
    }

    private Task RunAsync(params string[] caches) =>
        new ShardCacheLayout().RunAsync(new UpgradeContext(caches, _notes.Add, NullLogger.Instance), TestContext.CancellationToken);

    /// <summary>A source file's full path (nothing is written there: the step never reads the sources).</summary>
    private string Source(string folder, string name) => Path.Combine(_base, "library", folder, name);

    /// <summary>Writes a thumbnail where versions before 1.40.0 kept it: <c>{SHA1(folder)}/{SHA1(name)}.bin</c>.</summary>
    private static string OldThumbnail(string cache, string source, byte[] content)
    {
        var path = Path.Combine(cache, ThumbnailService.Hash(Path.GetDirectoryName(source)!), ThumbnailService.Hash(Path.GetFileName(source)) + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, Written);
        return path;
    }

    /// <summary>Writes a video's trickplay where versions before 1.40.0 kept it; answers its files.</summary>
    private static Dictionary<string, byte[]> OldTrickplay(string cache, string source)
    {
        var folder = Path.Combine(cache, TrickplayService.FolderName, ThumbnailService.Hash(Path.GetDirectoryName(source)!),
            ThumbnailService.Hash(Path.GetFileName(source)));
        Directory.CreateDirectory(folder);
        var files = new Dictionary<string, byte[]> { ["info.json"] = "{}"u8.ToArray(), ["0.jpg"] = [1, 2], ["1.jpg"] = [3], ["fingerprint.bin"] = [4, 5, 6] };
        foreach (var (name, content) in files)
        {
            File.WriteAllBytes(Path.Combine(folder, name), content);
            File.SetLastWriteTimeUtc(Path.Combine(folder, name), Written);
        }

        return files;
    }

    private static string NewThumbnail(string cache, string source) => Path.Combine(cache, ThumbnailService.ShardedPath(ThumbnailService.CacheKey(source)) + ".bin");

    private static string NewTrickplay(string cache, string source) =>
        Path.Combine(cache, TrickplayService.FolderName, ThumbnailService.ShardedPath(ThumbnailService.CacheKey(source)));

    private void AssertMoved(string path, byte[] content)
    {
        Assert.IsTrue(File.Exists(path), $"{path} is missing");
        CollectionAssert.AreEqual(content, File.ReadAllBytes(path), path);
        Assert.AreEqual(Written, File.GetLastWriteTimeUtc(path), "A rename keeps the date the freshness check reads");
    }

    private string Snapshot() => string.Join("\n", Directory.GetFileSystemEntries(_cache, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
}
