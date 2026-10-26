using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using Sfe.Core.Files.Compression;
using Sfe.Core.Options;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// An archive's list read once into a catalog (<see cref="ArchiveCatalog"/>): pages, totals and paths answered from it
/// at the cost of the page, for archives of many entries or deep trees, and lists that stop are said to have stopped.
/// </summary>
[TestClass]
public sealed class ArchiveCatalogTests
{
    private string _folder = "";

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void FoldersComeFirstThenFilesByNameWithTheirTotals()
    {
        var zip = TestArchives.Zip(_folder, "a.zip",
            ("readme.txt", "12345"), ("b.txt", "1"), ("Zeta/z.txt", "zz"), ("alpha/one.txt", "111"), ("alpha/Deep/two.txt", "2222"),
            ("Empty/", null), ("A.txt", "a"));
        var catalog = Build(zip, ArchiveFormat.Zip);

        var root = catalog.Children(ArchiveCatalog.RootId, null, 100);
        CollectionAssert.AreEqual(new[] { "alpha", "Empty", "Zeta" }, root.Folders.Select(folder => folder.Name).ToList(), "any case");
        CollectionAssert.AreEqual(new[] { "A.txt", "b.txt", "readme.txt" }, root.Files.Select(file => file.Name).ToList());
        Assert.IsNull(root.Next);

        var top = catalog.Folder(ArchiveCatalog.RootId)!;
        Assert.AreEqual(3, top.Folders);
        Assert.AreEqual(3, top.Files);
        Assert.AreEqual(6, top.AllFiles);
        Assert.AreEqual(5 + 1 + 2 + 3 + 4 + 1, top.Size);
        var alpha = root.Folders[0];
        Assert.AreEqual((1L, 1L, 2L, 7L), (alpha.Folders, alpha.Files, alpha.AllFiles, alpha.Size));
        Assert.AreEqual(0, root.Folders[1].AllFiles, "an empty folder entry is a folder");

        var summary = catalog.Summary();
        Assert.AreEqual((ArchiveFormat.Zip, 7L, 4L, 0L, true), (summary.Format, summary.Entries, summary.Folders, summary.LeftOut, summary.Complete));
        Assert.IsNull(summary.Stopped);
    }

    [TestMethod]
    public void FoldersAreFoundByPathAndEntriesOpenFromTheCatalog()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("Docs/Año/Nota.txt", "from the catalog"), ("docs/other.txt", "case"));
        var catalog = Build(zip, ArchiveFormat.Zip);

        var año = catalog.FindFolder("Docs/Año")!.Value;
        Assert.AreEqual(año, catalog.FindFolder("DOCS/año/"), "in any case when nothing matches as written");
        Assert.AreNotEqual(catalog.FindFolder("Docs"), catalog.FindFolder("docs"), "both spellings are folders of their own");
        Assert.IsNull(catalog.FindFolder("Docs/none"));
        Assert.AreEqual(ArchiveCatalog.RootId, catalog.FindFolder(""));
        Assert.AreEqual("Docs/Año", catalog.PathOf(año));

        var file = catalog.Children(año, null, 10).Files.Single();
        var again = catalog.Entry(file.Id)!;
        using var data = ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, again.Entry);
        Assert.AreEqual("from the catalog", new StreamReader(data).ReadToEnd());
    }

    [TestMethod]
    public void PagesFollowEachOtherByKeyWithoutGapsOrRepeats()
    {
        var entries = new List<(string, string?)>();
        for (var i = 0; i < 30; i++)
            entries.Add(($"folder {i:D2}/x.txt", "x"));
        for (var i = 0; i < 1000; i++)
            entries.Add(($"file {i:D4}.txt", ""));
        var catalog = Build(TestArchives.Zip(_folder, "many.zip", CompressionLevel.NoCompression, [.. entries]), ArchiveFormat.Zip);

        foreach (var limit in new[] { 1, 7, 30, 64, 1030, 5000 })
        {
            var names = new List<string>();
            ArchiveCursor? cursor = null;
            var pages = 0;
            do
            {
                var page = catalog.Children(ArchiveCatalog.RootId, cursor, limit);
                Assert.IsLessThanOrEqualTo(limit, page.Folders.Count + page.Files.Count);
                names.AddRange(page.Folders.Select(folder => folder.Name + "/"));
                names.AddRange(page.Files.Select(file => file.Name));
                cursor = page.Next;
                pages++;
            }
            while (cursor is not null);

            Assert.HasCount(1030, names, $"limit {limit}");
            CollectionAssert.AllItemsAreUnique(names);
            CollectionAssert.AreEqual(Enumerable.Range(0, 30).Select(i => $"folder {i:D2}/").ToList(), names.Take(30).ToList(), $"limit {limit}");
            CollectionAssert.AreEqual(Enumerable.Range(0, 1000).Select(i => $"file {i:D4}.txt").ToList(), names.Skip(30).ToList(), $"limit {limit}");
            Assert.IsLessThanOrEqualTo(1030 / limit + 2, pages);
        }
    }

    [TestMethod]
    public void NamesAreSearchedInAnyCaseWithTheFolderEachIsIn()
    {
        var zip = TestArchives.Zip(_folder, "s.zip",
            ("Photos/2024/Día uno.txt", "1"), ("Photos/2024/notes.txt", "2"), ("Días/readme.txt", "3"), ("other.txt", "4"));
        var catalog = Build(zip, ArchiveFormat.Zip);

        var page = catalog.Search("DÍA", null, 100, TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { ("Días", "") }, page.Folders.Select(found => (found.Folder.Name, found.Where)).ToList());
        CollectionAssert.AreEqual(new[] { ("Día uno.txt", "Photos/2024") }, page.Files.Select(found => (found.File.Name, found.Where)).ToList());
        Assert.IsNull(page.Next, "all looked through");
        Assert.IsEmpty(catalog.Search("nothing like it", null, 100, TimeSpan.FromSeconds(5)).Files);
    }

    [TestMethod]
    public void SearchPagesGoOnWhereTheyStoppedWithoutGapsOrRepeats()
    {
        var entries = new List<(string, string?)>();
        for (var i = 0; i < 40; i++)
            entries.Add(($"match folder {i:D2}/other {i:D2}.txt", "x"));
        for (var i = 0; i < 300; i++)
            entries.Add(($"{(i % 3 == 0 ? "match" : "skip")} {i:D3}.txt", ""));
        var catalog = Build(TestArchives.Zip(_folder, "pages.zip", CompressionLevel.NoCompression, [.. entries]), ArchiveFormat.Zip);

        // Small pages, small ranges, and no time at all: every page stops after one range, and the whole is found once.
        foreach (var (limit, budget) in new[] { (7, TimeSpan.FromSeconds(5)), (1000, TimeSpan.Zero), (3, TimeSpan.Zero) })
        {
            var names = new List<string>();
            ArchiveSearchCursor? cursor = null;
            var pages = 0;
            do
            {
                var page = catalog.Search("MATCH", cursor, limit, budget, chunk: 16);
                Assert.IsLessThanOrEqualTo(limit, page.Folders.Count + page.Files.Count);
                names.AddRange(page.Folders.Select(found => found.Folder.Name + "/"));
                names.AddRange(page.Files.Select(found => found.File.Name));
                cursor = page.Next;
                Assert.IsLessThan(1000, ++pages);
            }
            while (cursor is not null);

            Assert.HasCount(40 + 100, names, $"limit {limit}, budget {budget}");
            CollectionAssert.AllItemsAreUnique(names);
            Assert.IsTrue(names.Take(40).All(name => name.EndsWith('/')), "folders first");
        }
    }

    [TestMethod]
    public void ATreeThousandsOfFoldersDeepIsListedWithoutRecursion()
    {
        const int depth = 5000;
        var entries = new List<(string, string?)>();
        var path = "";
        for (var level = 1; level <= depth; level++)
        {
            path += "d/";
            if (level % 1000 == 0 || level == depth)
                entries.Add((path + $"at {level}.txt", "1"));
        }
        var catalog = Build(TestArchives.Zip(_folder, "deep.zip", CompressionLevel.NoCompression, [.. entries]), ArchiveFormat.Zip,
            new ArchiveOptions { MaxDepth = 10_000 });

        var deepest = catalog.FindFolder(string.Join('/', Enumerable.Repeat("d", depth)))!.Value;
        Assert.AreEqual(depth, catalog.Folder(deepest)!.Depth);
        Assert.AreEqual($"at {depth}.txt", catalog.Children(deepest, null, 10).Files.Single().Name);
        var trail = catalog.Trail(deepest, 3)!;
        Assert.AreEqual(depth - 3, trail.Skipped);
        Assert.HasCount(3, trail.Last);
        Assert.AreEqual(deepest, trail.Last[^1].Id);
        Assert.HasCount(depth, catalog.PathOf(deepest)!.Split('/'));

        var root = catalog.Folder(ArchiveCatalog.RootId)!;
        Assert.AreEqual(5, root.AllFiles, "totals rolled up level by level");
        Assert.AreEqual(1, catalog.Children(ArchiveCatalog.RootId, null, 10).Folders.Single().Folders);
        Assert.AreEqual((long)depth, catalog.Summary().Folders);
    }

    [TestMethod]
    public void EntriesTooDeepAreLeftOutAndListed()
    {
        var zip = TestArchives.Zip(_folder, "deep.zip", ("a/b/c/d/e/f.txt", "f"), ("a/ok.txt", "ok"));
        var catalog = Build(zip, ArchiveFormat.Zip, new ArchiveOptions { MaxDepth = 3 });

        Assert.AreEqual(1L, catalog.Summary().LeftOut);
        var (name, reason) = catalog.LeftOut().Single();
        Assert.AreEqual("a/b/c/d/e/f.txt", name);
        Assert.Contains("too deep", reason);
        Assert.AreEqual(1, catalog.Folder(ArchiveCatalog.RootId)!.AllFiles);
        Assert.IsTrue(catalog.Summary().Complete, "left out is not stopped");
    }

    [TestMethod]
    public void NamesLeavingTheArchiveAreLeftOutAndNeverListed()
    {
        var path = Path.Combine(_folder, "evil.tar");
        using (var file = File.Create(path))
        using (var tar = new TarWriter(file, TarEntryFormat.Pax))
        {
            foreach (var name in new[] { "../../evil.sh", "/etc/cron.d/evil", "ok/../../../evil", "good.txt" })
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream([1]) });
        }
        var catalog = Build(path, ArchiveFormat.Tar);

        Assert.AreEqual("good.txt", catalog.Children(ArchiveCatalog.RootId, null, 10).Files.Single().Name);
        Assert.IsEmpty(catalog.Children(ArchiveCatalog.RootId, null, 10).Folders);
        Assert.HasCount(3, catalog.LeftOut());
        Assert.AreEqual(3L, catalog.Summary().LeftOut);
    }

    [TestMethod]
    public void AListPastTheLimitStopsAndSaysSo()
    {
        var entries = Enumerable.Range(0, 50).Select(i => ($"{i}.txt", (string?)"x")).ToArray();
        var catalog = Build(TestArchives.Zip(_folder, "a.zip", entries), ArchiveFormat.Zip, new ArchiveOptions { MaxEntries = 10 });

        var summary = catalog.Summary();
        Assert.IsFalse(summary.Complete);
        Assert.AreEqual(10L, summary.Entries);
        Assert.Contains("more than 10", summary.Stopped!);
        Assert.HasCount(10, catalog.Children(ArchiveCatalog.RootId, null, 100).Files);
    }

    [TestMethod]
    public void ABrokenArchiveKeepsWhatWasReadAndSaysItStopped()
    {
        var path = Path.Combine(_folder, "cut.tar.gz");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
            for (var i = 0; i < 200; i++)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{i}.bin") { DataStream = new MemoryStream(new byte[2000].Select((_, n) => (byte)(n * i)).ToArray()) });
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        var summary = Build(path, ArchiveFormat.TarGzip).Summary();
        Assert.IsFalse(summary.Complete, "a gzip cut short just ends: the tar's end mark is missing");
        Assert.Contains("cut off", summary.Stopped!);
        Assert.IsGreaterThan(0L, summary.Entries);
        Assert.IsLessThan(200L, summary.Entries);
    }

    [TestMethod]
    public void ATarCutBetweenTwoEntriesIsNotWhole()
    {
        var whole = Path.Combine(_folder, "whole.tar");
        using (var file = File.Create(whole))
        using (var tar = new TarWriter(file, TarEntryFormat.Ustar))
            for (var i = 0; i < 5; i++)
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"{i}.bin") { DataStream = new MemoryStream(Enumerable.Repeat((byte)1, 512).ToArray()) });
        var compressed = Path.Combine(_folder, "whole.tar.gz");
        using (var source = File.OpenRead(whole))
        using (var target = new GZipStream(File.Create(compressed), CompressionLevel.Fastest))
            source.CopyTo(target);
        Assert.IsTrue(Build(whole, ArchiveFormat.Tar).Summary().Complete);
        Assert.IsTrue(Build(compressed, ArchiveFormat.TarGzip).Summary().Complete);

        // Cut right after the last entry's data: whole blocks, but no end mark.
        var bytes = File.ReadAllBytes(whole);
        var end = bytes.AsSpan().LastIndexOfAnyExcept((byte)0) + 1;
        var cut = Path.Combine(_folder, "cut.tar");
        File.WriteAllBytes(cut, bytes[..((end + 511) / 512 * 512)]);
        var summary = Build(cut, ArchiveFormat.Tar).Summary();
        Assert.IsFalse(summary.Complete);
        Assert.AreEqual(5L, summary.Entries);
    }

    [TestMethod]
    public void SevenZipFoldersAndFilesMakeOneTree()
    {
        var catalog = Build(TestSamples.ArchiveOf("tree-solid.7z"), ArchiveFormat.SevenZip);
        var photos = catalog.FindFolder("Photos/2024")!.Value;
        CollectionAssert.AreEqual(new[] { "día uno.txt", "notes.txt" }, catalog.Children(photos, null, 10).Files.Select(file => file.Name).ToList());
        Assert.AreEqual(4, catalog.Folder(ArchiveCatalog.RootId)!.AllFiles);
        Assert.AreEqual(8L, catalog.Summary().Folders);
        Assert.IsNotNull(catalog.Folder(photos)!.Time, "the folder entry's time");
    }

    [TestMethod]
    public void ACatalogIsCurrentOnlyWhenWhole()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("a.txt", "a"));
        var catalog = Build(zip, ArchiveFormat.Zip);
        Assert.IsTrue(ArchiveCatalog.IsCurrent(catalog.Path));
        Assert.IsFalse(ArchiveCatalog.IsCurrent(Path.Combine(_folder, "none.db")));
        var garbage = Path.Combine(_folder, "garbage.db");
        File.WriteAllText(garbage, "not a database");
        Assert.IsFalse(ArchiveCatalog.IsCurrent(garbage));
    }

    [TestMethod]
    public void ACancelledBuildLeavesNothing()
    {
        var entries = Enumerable.Range(0, 30_000).Select(i => ($"{i}.txt", (string?)null)).ToArray();
        var zip = TestArchives.Zip(_folder, "a.zip", entries);
        var target = Path.Combine(_folder, "catalogs", "a.db");
        using var cancel = new CancellationTokenSource();
        var progress = new Progress(() => cancel.Cancel());

        Assert.Throws<OperationCanceledException>(() => ArchiveCatalog.Build(zip, ArchiveFormat.Zip, target, new ArchiveOptions(), progress, cancel.Token));
        Assert.IsFalse(File.Exists(target));
        Assert.IsFalse(File.Exists(target + ".building"));
    }

    [TestMethod]
    public void TwoHundredThousandEntriesArePagedAtTheCostOfAPage()
    {
        var path = Path.Combine(_folder, "big.zip");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            for (var i = 0; i < 200_000; i++)
                zip.CreateEntry($"part {i % 100:D2}/entry {i:D6}.dat", CompressionLevel.NoCompression);

        var watch = Stopwatch.StartNew();
        var catalog = Build(path, ArchiveFormat.Zip);
        var built = watch.Elapsed;
        TestContext.WriteLine($"200 000 entries: catalog built in {built.TotalSeconds:F1} s, {new FileInfo(catalog.Path).Length / 1024} KB");

        var last = catalog.FindFolder("part 99")!.Value;
        Assert.AreEqual(2000, catalog.Folder(last)!.Files);
        watch.Restart();
        ArchiveCursor? cursor = null;
        var count = 0;
        do
        {
            var page = catalog.Children(last, cursor, 200);
            count += page.Files.Count;
            cursor = page.Next;
        }
        while (cursor is not null);
        TestContext.WriteLine($"10 pages of 200 in {watch.ElapsedMilliseconds} ms");
        Assert.AreEqual(2000, count);
        Assert.AreEqual(200_000, catalog.Folder(ArchiveCatalog.RootId)!.AllFiles);
    }

    private ArchiveCatalog Build(string archive, ArchiveFormat format, ArchiveOptions? options = null) =>
        ArchiveCatalog.Build(archive, format, Path.Combine(_folder, "catalogs", Path.GetFileName(archive) + ".db"), options ?? new ArchiveOptions(),
            cancel: TestContext.CancellationToken);

    private sealed class Progress(Action report) : IProgress<long>
    {
        public void Report(long value) => report();
    }
}
