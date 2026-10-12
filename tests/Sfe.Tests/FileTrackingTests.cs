using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.FileTracking;
using Sfe.Core.Indexing;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Duplicate files, and tags following files that are moved, renamed or whose drive goes missing.</summary>
[TestClass]
public sealed class FileTrackingTests
{
    private const int Kb = 1024;

    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private DuplicateFinder Duplicates => _services.Get<DuplicateFinder>();
    private DuplicateRemover Remover => _services.Get<DuplicateRemover>();
    private RelinkService Relink => _services.Get<RelinkService>();
    private TagService Tags => _services.Get<TagService>();

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

    private async Task UseServicesAsync(Action<IDictionary<string, string?>> configure)
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, configure);
    }

    /* ---------- Duplicates ---------- */

    [TestMethod]
    public async Task DuplicatesAreGroupedBySizeAndFingerprintBiggestFirst()
    {
        Copy("Media/Holiday 2020.mp4", "Copies/Holiday copy.mp4");
        Copy("Media/Holiday 2020.mp4", "Copies/Sub/holiday.mp4");
        Copy("Media/photo.jpg", "Copies/photo (1).jpg");
        await ScanAsync();

        var results = (await Duplicates.FindAsync("", null, null, TestContext.CancellationToken))!;

        Assert.HasCount(2, results.Groups);
        var video = results.Groups[0];
        Assert.AreEqual(5L * 1024 * 1024, video.Size);
        Assert.AreEqual(3, video.Count);
        CollectionAssert.AreEqual(
            new[] { "Copies/Holiday copy.mp4", "Copies/Sub/holiday.mp4", "Media/Holiday 2020.mp4" },
            video.Files.Select(f => f.Path).ToArray());
        Assert.AreEqual(2 * video.Size, video.WastedBytes);
        CollectionAssert.AreEquivalent(new[] { "Copies/photo (1).jpg", "Media/photo.jpg" }, results.Groups[1].Files.Select(f => f.Path).ToArray());

        Assert.IsNotNull(results.Summary);
        Assert.AreEqual(2, results.Summary.Groups);
        Assert.AreEqual(5, results.Summary.Files);
        Assert.AreEqual(video.WastedBytes + 3 * Kb, results.Summary.WastedBytes);
        Assert.IsFalse(results.HasMore);
    }

    [TestMethod]
    public async Task DuplicatePagesFollowEachOther()
    {
        Copy("Media/Holiday 2020.mp4", "Copies/Holiday copy.mp4");
        Copy("Media/Episode 2.mkv", "Copies/Episode 2.mkv");
        Copy("Media/photo.jpg", "Copies/photo.jpg");
        await ScanAsync();

        var seen = new List<long>();
        string? after = null;
        var pages = 0;
        do
        {
            var page = (await Duplicates.FindAsync("", after, 1, TestContext.CancellationToken))!;
            Assert.AreEqual(after is null, page.Summary is not null, "Only the first page carries the totals.");
            seen.AddRange(page.Groups.Select(g => g.Size));
            after = page.Next;
            pages++;
        } while (after is not null && pages < 10);

        CollectionAssert.AreEqual(new[] { 5L * 1024 * 1024, 200L * Kb, 3L * Kb }, seen);
    }

    [TestMethod]
    public async Task DuplicatesOfAFolderOnlyCompareItsFiles()
    {
        Copy("Media/Holiday 2020.mp4", "Copies/Holiday copy.mp4");
        Copy("Media/Holiday 2020.mp4", "Copies/Sub/holiday.mp4");
        Copy("Media/photo.jpg", "Copies/photo.jpg");
        await ScanAsync();

        var results = (await Duplicates.FindAsync("Copies", null, null, TestContext.CancellationToken))!;

        Assert.AreEqual("Copies", results.Path);
        var group = Assert.ContainsSingle(results.Groups);
        CollectionAssert.AreEqual(new[] { "Copies/Holiday copy.mp4", "Copies/Sub/holiday.mp4" }, group.Files.Select(f => f.Path).ToArray());
        Assert.IsNull(await Duplicates.FindAsync("../outside", null, null, TestContext.CancellationToken));
        Assert.IsNull(await Duplicates.FindAsync("", "not a cursor", null, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SmallFilesAreLeftOutOfDuplicates()
    {
        await UseServicesAsync(s => s["Sfe:FileTracking:Duplicates:MinimumKilobytes"] = "10");
        Copy("Media/Holiday 2020.mp4", "Copies/Holiday copy.mp4");
        Copy("Media/photo.jpg", "Copies/photo.jpg");
        await ScanAsync();

        var results = (await Duplicates.FindAsync("", null, null, TestContext.CancellationToken))!;

        Assert.AreEqual(5L * 1024 * 1024, Assert.ContainsSingle(results.Groups).Size);
        Assert.AreEqual(10 * Kb, results.MinimumSize);
    }

    /* ---------- Removing duplicates ---------- */

    [TestMethod]
    public async Task RemovingACopyDeletesItAndMergesItsTagsIntoTheKeptOne()
    {
        var cat = await CreateTagAsync("Cat");
        var beach = await CreateTagAsync("Beach");
        Copy("Media/photo.jpg", "Copies/photo (1).jpg");
        await ScanAsync();
        await TagAsync(cat, "Media/photo.jpg");
        await TagAsync(beach, "Copies/photo (1).jpg");

        var result = await Remover.RemoveAsync("Copies/photo (1).jpg", "Media/photo.jpg", TestContext.CancellationToken);

        Assert.AreEqual(DuplicateRemovalStatus.Removed, result.Status, result.Error);
        Assert.IsFalse(File.Exists(_library.FullPath("Copies/photo (1).jpg")));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));
        CollectionAssert.AreEquivalent(new[] { cat, beach }, result.Kept!.Tags.Select(t => t.Id).ToArray());
        CollectionAssert.AreEquivalent(new[] { cat, beach }, await TagIdsAsync("Media/photo.jpg"));
        Assert.IsEmpty((await Duplicates.FindAsync("", null, null, TestContext.CancellationToken))!.Groups);
        // Its tags went to the kept copy: it is not waiting to be relinked, now or after the next scan.
        Assert.AreEqual(0, (await ScanAsync()).Missing);
    }

    [TestMethod]
    public async Task FilesThatDifferPastTheFingerprintAreNotRemoved()
    {
        // Same size and same first megabyte (what the fingerprint reads): listed as duplicates, yet different.
        var content = new byte[FileFingerprint.ChunkSize + 4096];
        await File.WriteAllBytesAsync(_library.FullPath("same-start-a.bin"), content, TestContext.CancellationToken);
        content[^1] = 1;
        await File.WriteAllBytesAsync(_library.FullPath("same-start-b.bin"), content, TestContext.CancellationToken);
        await ScanAsync();
        Assert.ContainsSingle((await Duplicates.FindAsync("", null, null, TestContext.CancellationToken))!.Groups);

        var result = await Remover.RemoveAsync("same-start-b.bin", "same-start-a.bin", TestContext.CancellationToken);

        Assert.AreEqual(DuplicateRemovalStatus.Invalid, result.Status);
        Assert.AreEqual("keep", result.Field);
        Assert.IsTrue(File.Exists(_library.FullPath("same-start-a.bin")));
        Assert.IsTrue(File.Exists(_library.FullPath("same-start-b.bin")));
    }

    [TestMethod]
    public async Task TheSameFileSeenThroughAFolderLinkIsNotRemoved()
    {
        // The indexer follows folder links, so the photo shows up twice; both paths are one file.
        if (!TryLinkFolder("Linked", "Media"))
            Assert.Inconclusive("Folder links cannot be created here.");
        try
        {
            await ScanAsync();
            var groups = (await Duplicates.FindAsync("", null, null, TestContext.CancellationToken))!.Groups;
            Assert.IsTrue(groups.Any(g => g.Files.Select(f => f.Path).Order().SequenceEqual(["Linked/photo.jpg", "Media/photo.jpg"])),
                "The photo is listed through both paths.");

            var result = await Remover.RemoveAsync("Linked/photo.jpg", "Media/photo.jpg", TestContext.CancellationToken);

            Assert.AreEqual(DuplicateRemovalStatus.Invalid, result.Status);
            Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));
        }
        finally
        {
            // The link itself, not what it leads to (a recursive delete stops at it).
            Directory.Delete(_library.FullPath("Linked"));
        }
    }

    [TestMethod]
    public async Task HardLinksAreNotRemovedOnWindows()
    {
        if (!OperatingSystem.IsWindows() || !RunCmd($"mklink /H \"{_library.FullPath("photo link.jpg")}\" \"{_library.FullPath("Media/photo.jpg")}\""))
            Assert.Inconclusive("Hard links are only checked on Windows.");

        var result = await Remover.RemoveAsync("photo link.jpg", "Media/photo.jpg", TestContext.CancellationToken);

        Assert.AreEqual(DuplicateRemovalStatus.Invalid, result.Status);
        Assert.IsTrue(File.Exists(_library.FullPath("photo link.jpg")));
    }

    [TestMethod]
    public async Task RemovingCanBeTurnedOff()
    {
        await UseServicesAsync(s => s["Sfe:FileTracking:Duplicates:AllowRemove"] = "false");
        Copy("Media/photo.jpg", "Copies/photo.jpg");
        await ScanAsync();

        Assert.IsFalse((await Duplicates.FindAsync("", null, null, TestContext.CancellationToken))!.CanRemove);
        var result = await Remover.RemoveAsync("Copies/photo.jpg", "Media/photo.jpg", TestContext.CancellationToken);

        Assert.AreEqual(DuplicateRemovalStatus.Disabled, result.Status);
        Assert.IsTrue(File.Exists(_library.FullPath("Copies/photo.jpg")));
        Assert.IsFalse(_services.Get<SettingsService>().Get().Values.FileTracking.Duplicates.AllowRemove);
    }

    /* ---------- Relinking ---------- */

    [TestMethod]
    public async Task TagsFollowAMovedAndRenamedFile()
    {
        var tag = await CreateTagAsync("Beach");
        await ScanAsync();
        await TagAsync(tag, "Media/photo.jpg");
        // A cached thumbnail at the old place follows the file too.
        var thumbnails = _services.Get<ThumbnailService>();
        var oldThumbnail = thumbnails.GetCachePath(_library.FullPath("Media/photo.jpg"));
        Directory.CreateDirectory(Path.GetDirectoryName(oldThumbnail)!);
        await File.WriteAllBytesAsync(oldThumbnail, [1, 2, 3], TestContext.CancellationToken);

        Move("Media/photo.jpg", "Albums/2020/beach day.jpg");
        var status = await ScanAsync();

        Assert.AreEqual(1, status.Relinked);
        Assert.AreEqual(0, status.Missing);
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Albums/2020/beach day.jpg"));
        Assert.IsTrue(File.Exists(thumbnails.GetCachePath(_library.FullPath("Albums/2020/beach day.jpg"))));
        Assert.IsFalse(File.Exists(oldThumbnail));
        Assert.AreEqual(0, await Relink.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task TagsFollowARenamedFolder()
    {
        var tag = await CreateTagAsync("Work");
        _library.AddFile("Projects/Alpha/report.pdf", 8 * Kb);
        _library.AddFile("Projects/Alpha/notes/plan.txt", 4 * Kb);
        await ScanAsync();
        await TagAsync(tag, "Projects/Alpha/report.pdf", "Projects/Alpha/notes/plan.txt");

        Directory.Move(_library.FullPath("Projects/Alpha"), _library.FullPath("Projects/Alpha (done)"));
        var status = await ScanAsync();

        Assert.AreEqual(2, status.Relinked);
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Projects/Alpha (done)/report.pdf"));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Projects/Alpha (done)/notes/plan.txt"));
    }

    [TestMethod]
    public async Task TagsComeBackWhenAMissingFolderReturns()
    {
        var tag = await CreateTagAsync("Favourite");
        await ScanAsync();
        await TagAsync(tag, "Media/Episode 2.mkv");

        // The folder disappears (an unplugged drive), then comes back at the same place.
        var away = Path.Combine(_library.BasePath, "away");
        Directory.Move(_library.FullPath("Media"), away);
        var gone = await ScanAsync();
        Assert.AreEqual(1, gone.Missing);
        var missing = await Relink.ListAsync(null, null, TestContext.CancellationToken);
        var item = Assert.ContainsSingle(missing.Items);
        Assert.AreEqual("Media/Episode 2.mkv", item.Path);
        Assert.AreEqual(tag, Assert.ContainsSingle(item.Tags).Id);

        Directory.Move(away, _library.FullPath("Media"));
        var back = await ScanAsync();

        Assert.AreEqual(1, back.Relinked);
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Media/Episode 2.mkv"));
    }

    [TestMethod]
    public async Task AmbiguousMovesWaitForTheUser()
    {
        var tag = await CreateTagAsync("Holiday");
        await ScanAsync();
        await TagAsync(tag, "Media/photo.jpg");

        // Gone, and two new identical files with other names: either could be it.
        Copy("Media/photo.jpg", "A/one.jpg");
        Move("Media/photo.jpg", "B/two.jpg");
        var status = await ScanAsync();

        Assert.AreEqual(0, status.Relinked);
        Assert.AreEqual(1, status.Missing);
        var item = Assert.ContainsSingle((await Relink.ListAsync(null, null, TestContext.CancellationToken)).Items);
        CollectionAssert.AreEqual(new[] { "A/one.jpg", "B/two.jpg" }, item.Candidates.Select(c => c.Path).ToArray());

        Assert.AreEqual(RelinkStatus.Relinked, await Relink.RelinkAsync(item.Id, "B/two.jpg", TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("B/two.jpg"));
        Assert.IsEmpty(await TagIdsAsync("A/one.jpg"));
        Assert.AreEqual(RelinkStatus.NotFound, await Relink.RelinkAsync(item.Id, "A/one.jpg", TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task OlderIdenticalFilesAreNotTakenForTheMovedOne()
    {
        var tag = await CreateTagAsync("Original");
        // The backup is indexed first, by a scan of its own: the photo is indexed later, by the next scan. (Indexed by
        // the same scan, either could come first: folders are scanned in parallel.)
        Move("Media/photo.jpg", "Elsewhere/photo.jpg");
        Copy("Elsewhere/photo.jpg", "Backup/photo.jpg");
        await ScanAsync();
        Move("Elsewhere/photo.jpg", "Media/photo.jpg");
        await ScanAsync();
        await TagAsync(tag, "Media/photo.jpg");

        // Deleted, not moved: the backup copy was indexed before the photo, so it is another copy, not the file.
        File.Delete(_library.FullPath("Media/photo.jpg"));
        var status = await ScanAsync();

        Assert.AreEqual(0, status.Relinked);
        Assert.AreEqual(1, status.Missing);
        Assert.IsEmpty(await TagIdsAsync("Backup/photo.jpg"));
    }

    [TestMethod]
    public async Task FilesWithoutFingerprintAreMatchedByNameSizeAndDate()
    {
        await UseServicesAsync(s => s["Sfe:Scan:ComputeHashes"] = "false");
        var tag = await CreateTagAsync("Music");
        await ScanAsync();
        await TagAsync(tag, "Media/Episode 10.mkv");

        Move("Media/Episode 10.mkv", "Series/Episode 10.mkv");
        Assert.AreEqual(1, (await ScanAsync()).Relinked);
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Series/Episode 10.mkv"));

        // Renamed as well: nothing to go by without a fingerprint.
        Move("Series/Episode 10.mkv", "Series/S01E10.mkv");
        var status = await ScanAsync();
        Assert.AreEqual(0, status.Relinked);
        Assert.AreEqual(1, status.Missing);
    }

    [TestMethod]
    public async Task WithRelinkingOffTagsGoWithTheFile()
    {
        await UseServicesAsync(s => s["Sfe:FileTracking:Relink:Enabled"] = "false");
        var tag = await CreateTagAsync("Lost");
        await ScanAsync();
        await TagAsync(tag, "Media/photo.jpg");

        Move("Media/photo.jpg", "Other/photo.jpg");
        var status = await ScanAsync();

        Assert.AreEqual(0, status.Relinked);
        Assert.IsEmpty(await TagIdsAsync("Other/photo.jpg"));
        Assert.AreEqual(0, await Relink.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task MissingFilesCanBeForgottenAndExpire()
    {
        var tag = await CreateTagAsync("Temp");
        await ScanAsync();
        await TagAsync(tag, "Media/photo.jpg", "Media/Episode 2.mkv");
        File.Delete(_library.FullPath("Media/photo.jpg"));
        File.Delete(_library.FullPath("Media/Episode 2.mkv"));
        await ScanAsync();
        var items = (await Relink.ListAsync(null, null, TestContext.CancellationToken)).Items;
        Assert.HasCount(2, items);

        Assert.AreEqual(1, await Relink.ForgetAsync(items[0].Id, TestContext.CancellationToken));
        Assert.AreEqual(1, await Relink.CountAsync(TestContext.CancellationToken));

        // Kept longer than the retention: forgotten by the next pass.
        await using (var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken))
        {
            await db.MissingFiles.ExecuteUpdateAsync(s => s.SetProperty(m => m.MissingSince, DateTime.Now.AddDays(-31)), TestContext.CancellationToken);
        }
        var pass = await Relink.RunAsync(TestContext.CancellationToken);
        Assert.AreEqual(1, pass.Forgotten);
        Assert.AreEqual(0, pass.Remaining);
    }

    [TestMethod]
    public async Task DeletingATagRemovesItFromMissingFiles()
    {
        var tag = await CreateTagAsync("Doomed");
        await ScanAsync();
        await TagAsync(tag, "Media/photo.jpg");
        File.Delete(_library.FullPath("Media/photo.jpg"));
        await ScanAsync();

        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(tag, TestContext.CancellationToken));

        Assert.IsEmpty(Assert.ContainsSingle((await Relink.ListAsync(null, null, TestContext.CancellationToken)).Items).Tags);
    }

    [TestMethod]
    public async Task FileTrackingSettingsAreValidated()
    {
        var settings = _services.Get<SettingsService>();
        var values = settings.Get().Values;
        Assert.AreEqual(1, values.FileTracking.Duplicates.MinimumKilobytes);
        Assert.IsTrue(values.FileTracking.Relink.Enabled);
        values.FileTracking.Duplicates.GroupsPerPage = 0;
        values.FileTracking.Relink.RetentionDays = -1;

        var (view, errors) = await settings.SaveAsync(values, TestContext.CancellationToken);

        Assert.IsNull(view);
        Assert.IsTrue(errors!.ContainsKey("fileTracking.duplicates.groupsPerPage"));
        Assert.IsTrue(errors.ContainsKey("fileTracking.relink.retentionDays"));

        values.FileTracking.Duplicates.GroupsPerPage = 20;
        values.FileTracking.Relink.RetentionDays = 0;
        values.FileTracking.Relink.MatchByName = false;
        (view, _) = await settings.SaveAsync(values, TestContext.CancellationToken);
        Assert.AreEqual(20, view!.Values.FileTracking.Duplicates.GroupsPerPage);
        Assert.IsFalse(settings.Get().Values.FileTracking.Relink.MatchByName);
    }

    /* ---------- Helpers ---------- */

    private void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_library.FullPath(to))!);
        File.Copy(_library.FullPath(from), _library.FullPath(to));
    }

    private void Move(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_library.FullPath(to))!);
        File.Move(_library.FullPath(from), _library.FullPath(to));
    }

    /// <summary>Links <paramref name="link"/> to the folder <paramref name="target"/>: a junction on Windows (no rights needed).</summary>
    private bool TryLinkFolder(string link, string target)
    {
        if (OperatingSystem.IsWindows())
            return RunCmd($"mklink /J \"{_library.FullPath(link)}\" \"{_library.FullPath(target)}\"");
        try
        {
            Directory.CreateSymbolicLink(_library.FullPath(link), _library.FullPath(target));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool RunCmd(string command)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    /// <summary>Scans the whole library the way the Scan button does, relinking pass included.</summary>
    private async Task<ScanStatus> ScanAsync()
    {
        var coordinator = _services.Get<ScanCoordinator>();
        Assert.IsTrue(coordinator.TryStart(""));
        await coordinator.Completion!;
        var status = coordinator.Status;
        Assert.AreEqual(ScanState.Completed, status.State, status.Message);
        return status;
    }

    private async Task<int> CreateTagAsync(string name) =>
        (await Tags.CreateAsync(new TagInput(name), TestContext.CancellationToken)).Tag!.Id;

    private async Task TagAsync(int tag, params string[] paths)
    {
        var (result, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: [tag]), TestContext.CancellationToken);
        Assert.IsNotNull(result, errors is null ? null : string.Join(" ", errors.SelectMany(e => e.Value)));
    }

    private async Task<int[]> TagIdsAsync(string path) =>
        (await Tags.GetFileTagsAsync(path, TestContext.CancellationToken))!.Tags.Select(t => t.Id).ToArray();
}
