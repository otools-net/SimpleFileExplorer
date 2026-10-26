using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sfe.Core.Files.Compression;
using Sfe.Core.Options;

namespace Sfe.Tests;

/// <summary>
/// A library's archive catalogs (<see cref="ArchiveCatalogs"/>): built once however many ask, made again for a changed
/// archive, dropped when nobody waits, and the least recently used deleted past the cap.
/// </summary>
[TestClass]
public sealed class ArchiveCatalogsTests
{
    private string _folder = "";
    private Settings _settings = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _settings = new Settings();
        _settings.Value.Thumbnails.CacheDirectory = Path.Combine(_folder, "cache");
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public async Task OneBuildServesEveryoneAndAChangedArchiveIsReadAgain()
    {
        var catalogs = new ArchiveCatalogs(_settings, new ArchiveBuildGate(), NullLogger<ArchiveCatalogs>.Instance);
        var zip = TestArchives.Zip(_folder, "a.zip", ("one.txt", "1"));

        Assert.AreEqual(ArchiveCatalogStatus.Building, catalogs.Get(zip, "a.zip", ArchiveFormat.Zip).Status);
        var ready = await ReadyAsync(catalogs, zip);
        Assert.AreEqual(1L, ready.Summary().Entries);
        Assert.HasCount(1, Directory.GetFiles(catalogs.Folder, "*.db"), "one catalog");
        Assert.AreEqual(ready.Path, (await ReadyAsync(catalogs, zip)).Path, "asked again: the same catalog");

        TestArchives.Zip(_folder, "a.zip", ("one.txt", "1"), ("two.txt", "2"));
        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(1));
        var changed = await ReadyAsync(catalogs, zip);
        Assert.AreNotEqual(ready.Path, changed.Path);
        Assert.AreEqual(2L, changed.Summary().Entries);
    }

    [TestMethod]
    public async Task ABuildNobodyWaitsForIsDropped()
    {
        var gate = new ArchiveBuildGate();
        var catalogs = new ArchiveCatalogs(_settings, gate, NullLogger<ArchiveCatalogs>.Instance) { Abandoned = TimeSpan.FromMilliseconds(200) };
        var zip = TestArchives.Zip(_folder, "a.zip", ("one.txt", "1"));
        var busy = gate.Builds(1);
        await busy.WaitAsync(TestContext.CancellationToken);
        try
        {
            Assert.AreEqual(ArchiveCatalogStatus.Building, catalogs.Get(zip, "a.zip", ArchiveFormat.Zip).Status, "queued behind another build");
            await Task.Delay(800, TestContext.CancellationToken);
        }
        finally
        {
            busy.Release();
        }
        await Task.Delay(300, TestContext.CancellationToken);
        Assert.IsFalse(Directory.Exists(catalogs.Folder) && Directory.GetFiles(catalogs.Folder, "*.db").Length > 0, "given up: nothing built");

        Assert.AreEqual(ArchiveCatalogStatus.Building, catalogs.Get(zip, "a.zip", ArchiveFormat.Zip).Status, "asked again: started again");
        Assert.AreEqual(1L, (await ReadyAsync(catalogs, zip)).Summary().Entries);
    }

    [TestMethod]
    public void TheLeastRecentlyUsedGoPastTheCap()
    {
        _settings.Value.Files.Archives.CatalogMegabytes = 1;
        var catalogs = new ArchiveCatalogs(_settings, new ArchiveBuildGate(), NullLogger<ArchiveCatalogs>.Instance);
        Directory.CreateDirectory(catalogs.Folder);
        var files = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var file = Path.Combine(catalogs.Folder, $"{i}.db");
            File.WriteAllBytes(file, new byte[300 * 1024]);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(i - 10));
            files.Add(file);
        }

        catalogs.Evict(keep: files[0]);

        Assert.IsTrue(File.Exists(files[0]), "the one just built stays, however old");
        Assert.IsFalse(File.Exists(files[1]), "the oldest of the others");
        Assert.IsTrue(File.Exists(files[2]) && File.Exists(files[3]), "under the cap again");
    }

    private async Task<ArchiveCatalog> ReadyAsync(ArchiveCatalogs catalogs, string zip)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var state = catalogs.Get(zip, "a.zip", ArchiveFormat.Zip);
            if (state.Catalog is { } catalog)
                return catalog;
            Assert.AreNotEqual(ArchiveCatalogStatus.Failed, state.Status, state.Error);
            await Task.Delay(25, TestContext.CancellationToken);
        }
        Assert.Fail("never ready");
        return null!;
    }

    private sealed class Settings : IOptionsMonitor<SfeOptions>
    {
        public SfeOptions Value { get; } = new();
        public SfeOptions CurrentValue => Value;
        public SfeOptions Get(string? name) => Value;
        public IDisposable? OnChange(Action<SfeOptions, string?> listener) => null;
    }
}
