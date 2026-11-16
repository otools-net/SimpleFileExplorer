using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Previews;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Thumbnails;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The connections' content cache (agents/docs/features/connection-previews.md) and its attack vectors: the cache
/// filled by one caller, a huge file, many parallel previews, a file changing while cached, a removed connection.
/// </summary>
[TestClass]
public sealed class ConnectionContentCacheTests
{
    private const string Kilobyte = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private TestLibrary _library = null!;
    private ConnectionWorld _world = null!;
    private FakeAccount _account = null!;
    private int _id;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    private ConnectionCacheStore Store => _world.Services.GetRequiredService<ConnectionCacheStore>();

    private ConnectionContentCache Cache => _world.Services.GetRequiredService<ConnectionContentCache>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _world = await ConnectionWorld.CreateAsync(
            _library,
            Cancel,
            new Dictionary<string, string?>
            {
                ["Sfe:Connections:Cache:MaxSizeMegabytes"] = "1",
                ["Sfe:Connections:Cache:MaxFileMegabytes"] = "1",
            }
        );
        _account = _world
            .Provider.Account("acct")
            .WithBytes("clip.mp4", File.ReadAllBytes(TestSamples.PathOf("testsrc-1s-320x180.mp4")))
            .WithFile("list.mp4", "#EXTM3U\nhttp://127.0.0.1:9/a.ts\n");
        var made = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: _world.Alice),
            "acct",
            Cancel
        );
        _id = made.Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _world.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ManyRequestsForOneFileDownloadItOnce()
    {
        var item = await ItemAsync("clip.mp4");
        _account.HoldReads = new SemaphoreSlim(0);

        var asked = Enumerable
            .Range(0, 5)
            .Select(i => Cache.OpenAsync(new ContentAsk(_id, item, true, $"caller-{i % 2}"), Cancel))
            .ToList();
        _account.HoldReads.Release();
        var opened = await Task.WhenAll(asked);

        Assert.AreEqual(1, _account.Calls.Count(c => c == "read clip.mp4"));
        Assert.AreEqual(1, opened.Select(c => c.FullPath).Distinct().Count());
        Assert.AreEqual(ItemKind.Video, opened[0].Media);
        StringAssert.StartsWith(opened[0].FullPath, Store.Folder);
        foreach (var content in opened)
            content.Dispose();
    }

    [TestMethod]
    public async Task ACallerDownloadsOnlyAFewFilesAtOnce()
    {
        for (var i = 0; i < ConnectionContentCache.DownloadsPerCaller + 1; i++)
            _account.WithFile($"f{i}.mp4", $"content {i}");
        _account.HoldReads = new SemaphoreSlim(0);
        var held = new List<Task<CachedContent>>();
        for (var i = 0; i < ConnectionContentCache.DownloadsPerCaller; i++)
            held.Add(Cache.OpenAsync(new ContentAsk(_id, await ItemAsync($"f{i}.mp4"), true, "greedy"), Cancel));
        var last = await ItemAsync($"f{ConnectionContentCache.DownloadsPerCaller}.mp4");

        var refused = await Assert.ThrowsExactlyAsync<ConnectionCacheException>(() =>
            Cache.OpenAsync(new ContentAsk(_id, last, true, "greedy"), Cancel)
        );
        var other = Cache.OpenAsync(new ContentAsk(_id, last, true, "someone else"), Cancel);
        _account.HoldReads.Release();

        Assert.AreEqual(CacheRefusal.Busy, refused.Reason);
        (await other).Dispose();
        foreach (var content in await Task.WhenAll(held))
            content.Dispose();
    }

    [TestMethod]
    public async Task AFileOverTheLimitIsNeverDownloaded()
    {
        _account.WithBytes("huge.mp4", new byte[1024 * 1024 + 1]);
        var item = await ItemAsync("huge.mp4");

        var refused = await Assert.ThrowsExactlyAsync<ConnectionCacheException>(() =>
            Cache.OpenAsync(new ContentAsk(_id, item, true, "alice"), Cancel)
        );

        Assert.AreEqual(CacheRefusal.TooLarge, refused.Reason);
        Assert.IsFalse(_account.Calls.Contains("read huge.mp4"));
    }

    [TestMethod]
    public async Task AProviderSendingMoreThanItToldIsCutOff()
    {
        // The item says 10 bytes; the provider then serves the larger file now at its path.
        _account.WithFile("grows.mp4", "0123456789");
        var told = await ItemAsync("grows.mp4");
        _account.WithFile("grows.mp4", string.Concat(Enumerable.Repeat(Kilobyte, 64)));

        var refused = await Assert.ThrowsExactlyAsync<ConnectionCacheException>(() =>
            Cache.OpenAsync(new ContentAsk(_id, told, true, "alice"), Cancel)
        );

        Assert.AreEqual(CacheRefusal.TooLarge, refused.Reason);
        Assert.IsFalse(Directory.EnumerateFiles(Store.Folder, "*.bin", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task AFileChangedAtTheProviderIsFetchedAgainAndItsOldVersionGoes()
    {
        _account.WithFile("note.mp4", "first version");
        var first = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("note.mp4"), true, "alice"), Cancel);
        first.Dispose();
        _account.WithFile("note.mp4", "second version, longer");

        using var second = await Cache.OpenAsync(
            new ContentAsk(_id, await ItemAsync("note.mp4"), true, "alice"),
            Cancel
        );

        Assert.AreEqual(2, _account.Calls.Count(c => c == "read note.mp4"));
        Assert.AreNotEqual(first.FullPath, second.FullPath);
        Assert.AreEqual("second version, longer", await File.ReadAllTextAsync(second.FullPath, Cancel));
        Assert.IsFalse(File.Exists(first.FullPath));
    }

    [TestMethod]
    public async Task TheFilesUsedLongestAgoGoFirstButNotOneBeingRead()
    {
        var part = string.Concat(Enumerable.Repeat(Kilobyte, 6 * 1024)); // 384 KB
        foreach (var name in new[] { "a.mp4", "b.mp4", "c.mp4" })
            _account.WithFile(name, part);
        using var held = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("a.mp4"), true, "x"), Cancel);
        File.SetLastWriteTimeUtc(held.FullPath, DateTime.UtcNow.AddHours(-2));
        var b = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("b.mp4"), true, "x"), Cancel);
        b.Dispose();
        File.SetLastWriteTimeUtc(b.FullPath, DateTime.UtcNow.AddHours(-1));

        using var c = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("c.mp4"), true, "x"), Cancel);

        Assert.IsTrue(File.Exists(held.FullPath), "Held: never evicted");
        Assert.IsFalse(File.Exists(b.FullPath), "The oldest not held went");
        Assert.IsTrue(File.Exists(c.FullPath));
    }

    [TestMethod]
    public async Task OldFilesAndRemovedConnectionsAreSweptAndARemovedConnectionLeavesNothing()
    {
        var old = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("clip.mp4"), true, "x"), Cancel);
        old.Dispose();
        var orphan = Path.Combine(Store.Folder, "99999", "ab", "ab-cd.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        await File.WriteAllTextAsync(orphan, "left by a connection removed", Cancel);
        File.SetLastWriteTimeUtc(old.FullPath, DateTime.UtcNow.AddDays(-31));

        Store.Sweep(new HashSet<int> { _id });

        Assert.IsFalse(File.Exists(old.FullPath), "Unused for longer than the age limit");
        Assert.IsFalse(Directory.Exists(Path.Combine(Store.Folder, "99999")));

        using var again = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("clip.mp4"), true, "x"), Cancel);
        Store.Forget(_id);
        Assert.IsFalse(Directory.Exists(Path.Combine(Store.Folder, _id.ToString())));
    }

    [TestMethod]
    public async Task AFirstSweepAfterAStartKeepsWhatWasUsedRecently()
    {
        var recent = Store.PathOf(_id, new RemoteCacheKey("ab12", "cd34"), ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(recent)!);
        await File.WriteAllTextAsync(recent, "kept from before the restart", Cancel);

        Store.Sweep(new HashSet<int> { _id });

        Assert.IsTrue(File.Exists(recent));
    }

    [TestMethod]
    public async Task AScriptNamedAsAVideoIsCachedButNeverGivenToFfmpeg()
    {
        using var script = await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("list.mp4"), true, "x"), Cancel);
        var ffmpeg = new Ffmpeg(new StaticOptions(new SfeOptions()), NullLogger<Ffmpeg>.Instance);

        Assert.IsNull(script.Media);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ffmpeg.ConnectionInputAsync(script));
    }

    [TestMethod]
    public async Task TheCacheTakesNothingWhenItIsOffOrInAFolderTheAppServes()
    {
        Assert.IsTrue(Store.IsServed([Path.GetDirectoryName(Store.Folder)!]));
        Assert.IsTrue(Store.IsServed([Store.Folder]));
        Assert.IsFalse(Store.IsServed([_library.Root, Path.Combine(Store.Folder, "sub")]));

        Store.Disable("a test");
        var refused = await Assert.ThrowsExactlyAsync<ConnectionCacheException>(async () =>
            await Cache.OpenAsync(new ContentAsk(_id, await ItemAsync("clip.mp4"), true, "x"), Cancel)
        );
        Assert.AreEqual(CacheRefusal.Off, refused.Reason);
    }

    private async Task<RemoteItem> ItemAsync(string path)
    {
        var opened = await _world.Connections.OpenAsync(_id, Cancel);
        await using var storage = opened.Value!;
        return (await storage.GetAsync(path, Cancel))!;
    }

    private sealed class StaticOptions(SfeOptions value) : IOptionsMonitor<SfeOptions>
    {
        public SfeOptions CurrentValue => value;

        public SfeOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SfeOptions, string?> listener) => null;
    }
}
