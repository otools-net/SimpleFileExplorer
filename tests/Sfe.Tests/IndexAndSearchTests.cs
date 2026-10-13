using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Indexing and searching against SQLite.</summary>
[TestClass]
public sealed class IndexAndSearchTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ScanIndexesEveryFolderAndFile()
    {
        await ScanAsync(_library.Root);

        await using var db = await CreateDbAsync();
        Assert.AreEqual(_library.DirectoryCount, await db.Directories.CountAsync());
        Assert.AreEqual(21, await db.Files.CountAsync());

        var root = await db.Directories.Include(d => d.Directories).SingleAsync(d => d.ParentId == null);
        Assert.AreEqual("Root", root.Name);
        CollectionAssert.AreEquivalent(
            new[] { "ChildDir", "Media", "RepeatedDir", "SpecialDir" },
            root.Directories.Select(d => d.Name).ToArray());

        var file = await db.Files.SingleAsync(f => f.FullName == _library.FullPath("Media/Holiday 2020.mp4"));
        Assert.AreEqual("Holiday 2020", file.Name);
        Assert.AreEqual(".mp4", file.Extension);
        Assert.AreEqual(5L * 1024 * 1024, file.Size);
        Assert.IsNotNull(file.Sha1);
    }

    [TestMethod]
    public async Task FindsFileWithNameAndSizeCondition()
    {
        await ScanAsync(_library.Root);

        CollectionAssert.AreEqual(new[] { "RFile2" }, await SearchNamesAsync("RFile2 s:<1mb"));
        Assert.IsEmpty(await SearchNamesAsync("RFile2 s:<1kb"));
    }

    [TestMethod]
    public async Task FindsAlternatives()
    {
        await ScanAsync(_library.Root);

        CollectionAssert.AreEquivalent(new[] { "RFile1.txt", "RFile2" }, await SearchNamesAsync("RFile1,RFile2"));
    }

    [TestMethod]
    public async Task ExclusionsApplyPerAlternative()
    {
        await ScanAsync(_library.FullPath("RepeatedDir"), recursive: false);

        CollectionAssert.AreEquivalent(new[] { "RDFile1.txt", "RDFile2.txt" }, await SearchNamesAsync("RDFile -1, RDFile -2"));
    }

    [TestMethod]
    public async Task MatchingIsCaseInsensitive()
    {
        await ScanAsync(_library.FullPath("RepeatedDir"), recursive: false);

        // "-df" excludes every "RDFile" in the second alternative, so only the first one matches.
        CollectionAssert.AreEqual(new[] { "RDFile2.txt" }, await SearchNamesAsync("RDFile -1, RDFile -2 -df"));
        Assert.HasCount(2, await SearchNamesAsync("rdfile"));
    }

    [TestMethod]
    public async Task FindsNonAsciiNames()
    {
        await ScanAsync(_library.Root);

        Assert.HasCount(2, await SearchNamesAsync("Русский"));
        Assert.HasCount(2, await SearchNamesAsync("Español"));
        CollectionAssert.AreEqual(new[] { "SFEnglish.con" }, await SearchNamesAsync("SFEnglish"));
    }

    [TestMethod]
    public async Task FiltersBySizeAndExtension()
    {
        await ScanAsync(_library.Root);

        CollectionAssert.AreEqual(new[] { "Holiday 2020.mp4" }, await SearchNamesAsync("s:>1mb"));
        CollectionAssert.AreEqual(new[] { "Holiday 2020.mp4" }, await SearchNamesAsync("s:5mb"));
        CollectionAssert.AreEquivalent(new[] { "Episode 10.mkv", "Episode 2.mkv" }, await SearchNamesAsync("e:MKV"));
        CollectionAssert.AreEqual(new[] { "Episode 10.mkv" }, await SearchNamesAsync("episode e:mkv s:>250kb"));
    }

    [TestMethod]
    public async Task LeadingStarMatchesTheWholePath()
    {
        await ScanAsync(_library.Root);

        Assert.IsEmpty(await SearchNamesAsync("RepeatedDir AFile1"));
        CollectionAssert.AreEqual(new[] { "AFile1.txt" }, await SearchNamesAsync("*RepeatedDir AFile1"));
    }

    [TestMethod]
    public async Task WildcardCharactersAreMatchedLiterally()
    {
        await ScanAsync(_library.Root);

        CollectionAssert.AreEqual(new[] { "100%_done[1].txt" }, await SearchNamesAsync("%_"));
        CollectionAssert.AreEqual(new[] { "100%_done[1].txt" }, await SearchNamesAsync("[1]"));
    }

    [TestMethod]
    public async Task SearchIsScopedToTheRequestedFolder()
    {
        await ScanAsync(_library.Root);

        var results = await SearchAsync("AFile1", "ChildDir");
        Assert.HasCount(1, results.Items);
        Assert.AreEqual("ChildDir/AFile1.txt", results.Items[0].Path);
        Assert.AreEqual("ChildDir", results.Items[0].Directory);

        Assert.HasCount(2, (await SearchAsync("AFile1", "")).Items);
    }

    [TestMethod]
    public async Task SearchResultsArePaged()
    {
        await ScanAsync(_library.Root);

        var first = await SearchAsync("File", "", limit: 5);
        var second = await SearchAsync("File", "", after: first.Next, limit: 50);
        var all = await SearchAsync("File", "", limit: 100);

        Assert.HasCount(5, first.Items);
        Assert.IsTrue(first.HasMore);
        Assert.AreEqual(first.Items[^1].Path, first.Next);
        Assert.IsFalse(second.HasMore);
        Assert.IsNull(second.Next);
        CollectionAssert.AreEqual(
            all.Items.Select(i => i.Path).ToArray(),
            first.Items.Concat(second.Items).Select(i => i.Path).ToArray());
    }

    [TestMethod]
    public async Task FirstSearchPageCountsTheMatches()
    {
        await ScanAsync(_library.Root);

        var all = await SearchAsync("File", "", limit: 1000);
        var first = await SearchAsync("File", "", limit: 2);
        var second = await SearchAsync("File", "", after: first.Next, limit: 2);

        Assert.IsGreaterThan(2, all.Items.Count);
        Assert.AreEqual(all.Items.Count, first.Total);
        Assert.IsFalse(first.TotalCapped);
        Assert.IsNull(second.Total);
    }

    [TestMethod]
    public async Task CountingTheMatchesLogsNoWarning()
    {
        var recorder = new WarningRecorder();
        await using var services = await CoreServices.CreateAsync(_library, logging: builder => builder.AddProvider(recorder));
        var results = await services.Get<FileSearchService>().SearchAsync("r:>100x100", "", null, 10, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(results?.Total);
        Assert.IsEmpty(recorder.Warnings);
    }

    [TestMethod]
    [DataRow(SortField.Path, false)]
    [DataRow(SortField.Path, true)]
    [DataRow(SortField.Name, false)]
    [DataRow(SortField.Name, true)]
    [DataRow(SortField.Modified, false)]
    [DataRow(SortField.Modified, true)]
    [DataRow(SortField.Size, false)]
    [DataRow(SortField.Size, true)]
    [DataRow(SortField.Type, false)]
    [DataRow(SortField.Type, true)]
    [DataRow(SortField.Duration, false)]
    [DataRow(SortField.Duration, true)]
    [DataRow(SortField.Resolution, false)]
    [DataRow(SortField.Resolution, true)]
    public async Task SortedSearchPagesFollowTheSortWithoutGapsOrRepeats(SortField sort, bool descending)
    {
        // Sizes and dates shared by several files, so pages also break inside ties.
        var stamp = new DateTime(2020, 5, 1, 12, 0, 0);
        var files = Directory.GetFiles(_library.Root, "*", SearchOption.AllDirectories);
        for (var i = 0; i < files.Length; i++)
        {
            File.WriteAllBytes(files[i], new byte[i % 3 * 10]);
            File.SetLastWriteTime(files[i], stamp.AddDays(i % 4));
        }
        await ScanAsync(_library.Root);

        var search = _services.Get<FileSearchService>();
        var all = (await search.SearchAsync("", "", null, 1000, sort, descending, TestContext.CancellationToken))!.Items;
        var paged = new List<LibraryItem>();
        string? after = null;
        do
        {
            var page = (await search.SearchAsync("", "", after, 2, sort, descending, TestContext.CancellationToken))!;
            paged.AddRange(page.Items);
            after = page.Next;
        }
        while (after is not null);

        Assert.IsGreaterThan(10, all.Count);
        CollectionAssert.AreEqual(all.Select(i => i.Path).ToArray(), paged.Select(i => i.Path).ToArray());
        Func<LibraryItem, long>? key = sort switch
        {
            SortField.Size => i => i.Size!.Value,
            SortField.Modified => i => i.Modified!.Value.Ticks,
            _ => null,
        };
        if (key is not null)
        {
            var values = all.Select(key).ToArray();
            CollectionAssert.AreEqual((descending ? values.OrderDescending() : values.Order()).ToArray(), values);
        }
    }

    [TestMethod]
    public async Task SearchRejectsCursorOfAnotherSort()
    {
        await ScanAsync(_library.Root);

        var first = await SearchAsync("File", "", limit: 2);
        var search = _services.Get<FileSearchService>();
        Assert.IsNull(await search.SearchAsync("File", "", first.Next, 2, SortField.Size, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SearchRejectsCursorOutsideTheLibrary()
    {
        await ScanAsync(_library.Root);

        Assert.IsNull(await _services.Get<FileSearchService>().SearchAsync("File", "", "../outside", 10, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task RescanOfHugeFolderSavesInBatches()
    {
        for (var i = 0; i < 2500; i++)
            _library.AddFile($"Huge/file{i:D4}.txt", 1);
        await ScanAsync(_library.Root);

        await using (var db = await CreateDbAsync())
            Assert.AreEqual(2500, await db.Files.CountAsync(f => f.Name.StartsWith("file")));

        for (var i = 0; i < 1200; i++)
            File.Delete(_library.FullPath($"Huge/file{i:D4}.txt"));
        File.WriteAllText(_library.FullPath("Huge/file2000.txt"), "changed content");
        var progress = await ScanAsync(_library.Root);

        await using var check = await CreateDbAsync();
        Assert.AreEqual(1300, await check.Files.CountAsync(f => f.Name.StartsWith("file")));
        Assert.AreEqual(15L, (await check.Files.SingleAsync(f => f.Name == "file2000")).Size);
        Assert.AreEqual(1200, progress.Removed);
    }

    [TestMethod]
    public async Task ClassifiesSearchResults()
    {
        await ScanAsync(_library.Root);

        var items = (await SearchAsync("", "Media")).Items.ToDictionary(i => i.Name, i => i.Kind);
        Assert.AreEqual(ItemKind.Video, items["Holiday 2020.mp4"]);
        Assert.AreEqual(ItemKind.Image, items["photo.jpg"]);
    }

    [TestMethod]
    public async Task RescanRemovesDeletedFilesAndFolders()
    {
        await ScanAsync(_library.Root);
        File.Delete(_library.FullPath("RFile1.txt"));
        Directory.Delete(_library.FullPath("RepeatedDir"), recursive: true);

        await ScanAsync(_library.Root);

        await using var db = await CreateDbAsync();
        Assert.AreEqual(_library.DirectoryCount, await db.Directories.CountAsync());
        Assert.IsFalse(await db.Files.AnyAsync(f => f.Name == "RFile1" || f.Name.StartsWith("RDFile")));
        Assert.IsEmpty(await SearchNamesAsync("RDFile"));
    }

    [TestMethod]
    public async Task RescanUpdatesChangedFiles()
    {
        await ScanAsync(_library.Root);
        await using (var db = await CreateDbAsync())
            Assert.AreEqual(10, (await db.Files.SingleAsync(f => f.Name == "RFile1")).Size);

        _library.AddFile("RFile1.txt", 4096);
        File.SetLastWriteTime(_library.FullPath("RFile1.txt"), DateTime.Now.AddMinutes(1));
        await ScanAsync(_library.Root);

        await using (var db = await CreateDbAsync())
            Assert.AreEqual(4096, (await db.Files.SingleAsync(f => f.Name == "RFile1")).Size);
    }

    [TestMethod]
    public async Task IgnoredFoldersAreNotIndexed()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, settings => settings["Sfe:Scan:IgnoredDirectories:0"] = "SpecialDir");

        await ScanAsync(_library.Root);

        Assert.IsEmpty(await SearchNamesAsync("Русский"));
        Assert.IsNotEmpty(await SearchNamesAsync("RFile"));
    }

    [TestMethod]
    public async Task RootGivenWithTrailingSeparatorKeepsItsChildrenLinked()
    {
        // The API resolves the library root with a trailing separator ("D:\Media\"). Stored as is, its
        // sub-folders could not find their parent, so deleted top-level folders were never removed.
        var root = _library.Root + Path.DirectorySeparatorChar;
        await ScanAsync(root);
        Directory.Delete(_library.FullPath("ChildDir"), recursive: true);

        await ScanAsync(root);

        await using var db = await CreateDbAsync();
        var indexedRoot = await db.Directories.Include(d => d.Directories).SingleAsync(d => d.ParentId == null);
        Assert.AreEqual(_library.Root, indexedRoot.FullName);
        CollectionAssert.AreEquivalent(new[] { "Media", "RepeatedDir", "SpecialDir" }, indexedRoot.Directories.Select(d => d.Name).ToArray());
        Assert.AreEqual(_library.DirectoryCount, await db.Directories.CountAsync());
    }

    [TestMethod]
    public async Task CaseVariantsAreDistinctOnCaseSensitiveFileSystems()
    {
        if (LibraryPaths.IsCaseInsensitiveFileSystem)
            Assert.Inconclusive("Case-sensitive file systems only.");

        _library.AddFile("Media/photo.JPG", 10);
        _library.AddFile("media/other.txt", 10);
        _library.AddFile("Media/Show: Part 1.mkv", 10);

        await ScanAsync(_library.Root);

        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "photo.JPG" }, await SearchNamesAsync("photo"));
        var media = (await SearchAsync("", "Media")).Items.Select(i => i.Path).ToArray();
        CollectionAssert.Contains(media, "Media/Show: Part 1.mkv");
        CollectionAssert.DoesNotContain(media, "media/other.txt");

        // Removing "media" must not take "Media" out of the index.
        Directory.Delete(_library.FullPath("media"), recursive: true);
        await ScanAsync(_library.Root);

        Assert.HasCount(2, await SearchNamesAsync("photo"));
        Assert.IsEmpty(await SearchNamesAsync("other"));

        var thumbnails = _services.Get<ThumbnailService>();
        Assert.AreNotEqual(thumbnails.GetCachePath(_library.FullPath("Media/photo.jpg")), thumbnails.GetCachePath(_library.FullPath("Media/photo.JPG")));
    }

    [TestMethod]
    public async Task FollowsFolderLinksButNotLinkCycles()
    {
        var expectedDirectories = _library.DirectoryCount;
        var outside = Path.Combine(_library.BasePath, "Outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "Linked.txt"), "linked", TestContext.CancellationToken);
        try
        {
            Directory.CreateSymbolicLink(_library.FullPath("ChildDir/up"), _library.Root);
            Directory.CreateSymbolicLink(_library.FullPath("RepeatedDir/self"), _library.FullPath("RepeatedDir"));
            Directory.CreateSymbolicLink(_library.FullPath("Media/outside"), outside);
            Directory.CreateSymbolicLink(Path.Combine(outside, "back"), _library.FullPath("Media"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive($"Cannot create symbolic links here: {ex.Message}");
        }

        await ScanAsync(_library.Root);

        await using var db = await CreateDbAsync();
        Assert.AreEqual(expectedDirectories + 1, await db.Directories.CountAsync(), "Only Media/outside is a new folder.");
        CollectionAssert.AreEqual(new[] { "Media/outside/Linked.txt" }, (await SearchAsync("Linked", "")).Items.Select(i => i.Path).ToArray());
    }

    [TestMethod]
    public async Task FingerprintIsTheSha1OfTheFirstMegabyte()
    {
        var path = _library.AddFile("hash.bin", 2 * FileFingerprint.ChunkSize);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken);
        var expected = Convert.ToHexString(SHA1.HashData(bytes.AsSpan(0, FileFingerprint.ChunkSize)));

        Assert.AreEqual(expected, await FileFingerprint.ComputeAsync(path, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SmallFilesGetFingerprintsOfTheirContent()
    {
        var a = _library.FullPath("small-a.txt");
        var b = _library.FullPath("small-b.txt");
        await File.WriteAllTextAsync(a, "a", TestContext.CancellationToken);
        await File.WriteAllTextAsync(b, "b", TestContext.CancellationToken);

        Assert.AreNotEqual(await FileFingerprint.ComputeAsync(a, TestContext.CancellationToken), await FileFingerprint.ComputeAsync(b, TestContext.CancellationToken));
        Assert.AreEqual(Convert.ToHexString(SHA1.HashData("a"u8)), await FileFingerprint.ComputeAsync(a, TestContext.CancellationToken));
    }

    private async Task<ScanProgress> ScanAsync(string path, bool recursive = true)
    {
        var progress = new ScanProgress();
        await _services.Get<DirectoryIndexer>().IndexAsync(path, recursive, progress, TestContext.CancellationToken);
        Assert.AreEqual(0, progress.Errors, "The scan reported errors (see the test log).");
        Assert.AreEqual(0, progress.Queued, "Every folder found should have been scanned.");
        return progress;
    }

    private async Task<SearchResults> SearchAsync(string query, string path, string? after = null, int limit = 100) =>
        await _services.Get<FileSearchService>().SearchAsync(query, path, after, limit, cancellationToken: TestContext.CancellationToken)
        ?? throw new AssertFailedException("Search scope not found.");

    private async Task<string[]> SearchNamesAsync(string query) =>
        (await SearchAsync(query, "")).Items.Select(i => i.Name).ToArray();

    private Task<SfeDbContext> CreateDbAsync() =>
        _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);

    /// <summary>Keeps the warnings logged by Sfe and EF, which a test run otherwise only prints.</summary>
    private sealed class WarningRecorder : ILoggerProvider, ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Warnings.Enqueue($"{eventId.Name}: {formatter(state, exception)}");
        }

        public void Dispose()
        {
        }
    }
}
