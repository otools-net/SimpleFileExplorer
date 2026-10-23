using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Default tags of folders (<see cref="FolderDefaultTags"/>): which files get them, and how they follow folders.</summary>
[TestClass]
public sealed class FolderDefaultTagsTests
{
    private static readonly FileActor Me = new(7, "tester");

    private readonly WarningLog _log = new();
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FolderDefaultTags Defaults => _services.Get<FolderDefaultTags>();
    private TagService Tags => _services.Get<TagService>();
    private FileOperations Files => _services.Get<FileOperations>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, logging: _log.AddTo);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AScanGivesTheDefaultTagsToTheFilesOfTheFolderAndItsSubFolders()
    {
        var holiday = await CreateTagAsync("Holiday");
        await SetAsync("RepeatedDir", holiday);

        await ScanAsync();

        CollectionAssert.AreEquivalent(
            new[] { "RepeatedDir/RDFile1.txt", "RepeatedDir/RDFile2.txt", "RepeatedDir/ChildDir/AFile1.txt", "RepeatedDir/ChildDir/AFile2",
                "RepeatedDir/RepeatedDir/RDFile1.txt", "RepeatedDir/RepeatedDir/RDFile2.txt" },
            await TaggedAsync(holiday));
    }

    [TestMethod]
    public async Task OnlyFilesEnteringTheIndexGetThemAndATagTakenOffStaysOff()
    {
        await ScanAsync();
        var holiday = await CreateTagAsync("Holiday");
        await SetAsync("Media", holiday);

        await ScanAsync();
        Assert.IsEmpty(await TaggedAsync(holiday), "Files indexed before keep their tags as they are.");

        _library.AddFile("Media/new.jpg", 10);
        await ScanAsync();
        CollectionAssert.AreEqual(new[] { "Media/new.jpg" }, await TaggedAsync(holiday));

        await Tags.ChangeFileTagsAsync(new FileTagChange(["Media/new.jpg"], Remove: [holiday]), Token);
        File.SetLastWriteTime(_library.FullPath("Media/new.jpg"), DateTime.Now.AddMinutes(-5));
        await ScanAsync();
        Assert.IsEmpty(await TaggedAsync(holiday), "A scan reading the file again gives nothing back.");
    }

    [TestMethod]
    public async Task NewFilesGetTheDefaultTagsOfTheirFolderAndItsParents()
    {
        await ScanAsync();
        var holiday = await CreateTagAsync("Holiday");
        var family = await CreateTagAsync("Family");
        await SetAsync("RepeatedDir", holiday);
        await SetAsync("RepeatedDir/ChildDir", family, holiday);

        var created = await Files.CreateFileAsync("RepeatedDir/ChildDir", "notes.txt", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, created.Status, created.Message);
        CollectionAssert.AreEquivalent(new[] { holiday, family }, await TagIdsAsync("RepeatedDir/ChildDir/notes.txt"));
    }

    [TestMethod]
    public async Task AFolderTellsItsOwnDefaultTagsAndThoseOfItsParents()
    {
        var holiday = await CreateTagAsync("Holiday");
        var family = await CreateTagAsync("Family");
        await SetAsync("", family);
        await SetAsync("RepeatedDir", holiday);
        await SetAsync("RepeatedDir/ChildDir", family);

        var child = await Defaults.GetAsync("RepeatedDir/ChildDir", Token);
        var listing = await Defaults.MarkAsync(_services.Get<LibraryBrowser>().List("RepeatedDir", 0, 100)!, Token);

        CollectionAssert.AreEqual(new[] { family }, child.Own.Select(t => t.Id).ToArray());
        CollectionAssert.AreEqual(new[] { ("RepeatedDir", holiday) }, child.Inherited.Select(t => (t.Path, t.Tag.Id)).ToArray(),
            "Family is the folder's own: not listed again from the root.");
        CollectionAssert.AreEqual(new[] { holiday }, listing.DefaultTags!.Own.Select(t => t.Id).ToArray());
        CollectionAssert.AreEqual(new[] { ("", family) }, listing.DefaultTags.Inherited.Select(t => (t.Path, t.Tag.Id)).ToArray());
        CollectionAssert.AreEqual(new[] { family }, listing.Items.Single(i => i.Name == "ChildDir").DefaultTags!.Select(t => t.Id).ToArray());
        Assert.IsNull(listing.Items.Single(i => i.Name == "RepeatedDir").DefaultTags);
        Assert.IsNull(listing.Items.Single(i => i.Name == "RDFile1.txt").DefaultTags);
    }

    [TestMethod]
    public async Task ARenamedFolderKeepsItsDefaultTagsAndItsFilesKeepTheirs()
    {
        await ScanAsync();
        var holiday = await CreateTagAsync("Holiday");
        await SetAsync("RepeatedDir/ChildDir", holiday);
        _library.AddFile("RepeatedDir/ChildDir/new.jpg", 10);
        await ScanAsync();
        await Tags.ChangeFileTagsAsync(new FileTagChange(["RepeatedDir/ChildDir/new.jpg"], Remove: [holiday]), Token);

        var renamed = await Files.RenameAsync("RepeatedDir", "Renamed", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, renamed.Status, renamed.Message);
        CollectionAssert.AreEqual(new[] { holiday }, (await Defaults.GetAsync("Renamed/ChildDir", Token)).Own.Select(t => t.Id).ToArray());
        Assert.IsTrue((await Defaults.GetAsync("RepeatedDir/ChildDir", Token)).IsEmpty);
        Assert.IsEmpty(await TaggedAsync(holiday), "Not given back by the rename.");
        _library.AddFile("Renamed/ChildDir/later.jpg", 10);
        await ScanAsync();
        CollectionAssert.AreEqual(new[] { "Renamed/ChildDir/later.jpg" }, await TaggedAsync(holiday));
    }

    [TestMethod]
    public async Task FilesAndFoldersMovedIntoAFolderGetItsDefaultTags()
    {
        await ScanAsync();
        var holiday = await CreateTagAsync("Holiday");
        await SetAsync("ChildDir", holiday);

        await RunAsync(new FileJobRequest(FileJobKind.Move, ["Media/photo.jpg", "RepeatedDir/RepeatedDir"], "ChildDir"));

        CollectionAssert.AreEquivalent(new[] { "ChildDir/photo.jpg", "ChildDir/RepeatedDir/RDFile1.txt", "ChildDir/RepeatedDir/RDFile2.txt" },
            await TaggedAsync(holiday));

        // Taken off, then moved inside the folder: the tag stays off.
        await Tags.ChangeFileTagsAsync(new FileTagChange(["ChildDir/photo.jpg"], Remove: [holiday]), Token);
        await RunAsync(new FileJobRequest(FileJobKind.Move, ["ChildDir/photo.jpg"], "ChildDir/RepeatedDir"));
        Assert.IsEmpty(await TagIdsAsync("ChildDir/RepeatedDir/photo.jpg"));
    }

    [TestMethod]
    public async Task ACopyKeepingItsTagsAlsoGetsTheDefaultTagsOfItsFolder()
    {
        await ScanAsync();
        var kept = await CreateTagAsync("Kept");
        var holiday = await CreateTagAsync("Holiday");
        await Tags.ChangeFileTagsAsync(new FileTagChange(["Media/photo.jpg"], Add: [kept, holiday]), Token);
        await SetAsync("ChildDir", holiday);

        var job = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["Media/photo.jpg", "RepeatedDir/RepeatedDir"], "ChildDir", KeepTags: true));
        await _services.Get<ScanCoordinator>().Completion!;

        Assert.IsTrue(job.Results.All(i => i.Status == FileOperationStatus.Done), _log.ToString());
        CollectionAssert.AreEquivalent(new[] { kept, holiday }, await TagIdsAsync("ChildDir/photo.jpg"));
        CollectionAssert.AreEqual(new[] { holiday }, await TagIdsAsync("ChildDir/RepeatedDir/RDFile1.txt"));
        CollectionAssert.AreEquivalent(new[] { kept, holiday }, await TagIdsAsync("Media/photo.jpg"), "The original keeps its own.");
    }

    [TestMethod]
    public async Task AFolderDeletedToTheTrashTakesItsDefaultTagsAndARestoreGivesThemBack()
    {
        await ScanAsync();
        var holiday = await CreateTagAsync("Holiday");
        var family = await CreateTagAsync("Family");
        await SetAsync("RepeatedDir", holiday);
        await SetAsync("RepeatedDir/ChildDir", family);
        await SetAsync("ChildDir", family);

        var deleted = await Files.DeleteAsync("RepeatedDir", permanent: false, Me, Token);
        Directory.CreateDirectory(_library.FullPath("RepeatedDir"));
        Assert.IsTrue((await Defaults.GetAsync("RepeatedDir", Token)).IsEmpty, "A new folder of that name doesn't get them.");

        var restored = await _services.Get<Trash>().RestoreAsync(deleted.TrashId!.Value, Me, Token);
        await _services.Get<ScanCoordinator>().Completion!;

        Assert.AreEqual("RepeatedDir (2)", restored.Item!.Path);
        CollectionAssert.AreEqual(new[] { holiday }, (await Defaults.GetAsync("RepeatedDir (2)", Token)).Own.Select(t => t.Id).ToArray());
        CollectionAssert.AreEqual(new[] { family }, (await Defaults.GetAsync("RepeatedDir (2)/ChildDir", Token)).Own.Select(t => t.Id).ToArray());
        CollectionAssert.AreEqual(new[] { family }, (await Defaults.GetAsync("ChildDir", Token)).Own.Select(t => t.Id).ToArray(), "Other folders keep theirs.");

        await Files.DeleteAsync("RepeatedDir (2)", permanent: true, Me, Token);
        await using var db = await DbAsync();
        CollectionAssert.AreEqual(new[] { "ChildDir" }, await db.FolderDefaultTags.Select(d => d.Path).ToArrayAsync(Token), "Deleted for good: forgotten.");
    }

    [TestMethod]
    public async Task DeletingATagTakesItOffTheFolders()
    {
        var holiday = await CreateTagAsync("Holiday");
        await SetAsync("Media", holiday);
        Assert.IsFalse((await Defaults.GetAsync("Media", Token)).IsEmpty);

        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(holiday, Token));

        Assert.IsTrue((await Defaults.GetAsync("Media", Token)).IsEmpty);
        // A tag id left in memory would fail the scan's writes.
        await ScanAsync();
    }

    [TestMethod]
    public async Task ChangesAreValidated()
    {
        Assert.AreEqual(DefaultTagChangeStatus.NotFound, (await Defaults.ChangeAsync("Nowhere", [BuiltInTags.Favorite], [], Token)).Status);
        Assert.AreEqual(DefaultTagChangeStatus.NotFound, (await Defaults.ChangeAsync("RFile1.txt", [BuiltInTags.Favorite], [], Token)).Status);
        Assert.AreEqual(DefaultTagChangeStatus.Invalid, (await Defaults.ChangeAsync("Media", [9999], [], Token)).Status);

        await SetAsync("Media", BuiltInTags.Favorite);
        await SetAsync("Media", BuiltInTags.Favorite);
        Assert.HasCount(1, (await Defaults.GetAsync("Media", Token)).Own, "Set twice, kept once.");
        Assert.AreEqual(DefaultTagChangeStatus.Ok, (await Defaults.ChangeAsync("Media", [], [BuiltInTags.Favorite], Token)).Status);
        Assert.IsTrue((await Defaults.GetAsync("Media", Token)).IsEmpty);
    }

    [TestMethod]
    public async Task AddsTheDefaultTagsTableToAnExistingSqliteIndex()
    {
        // The index as 1.80 created it.
        await using (var db = await DbAsync())
        {
            await db.Database.ExecuteSqlAsync($"DROP TABLE FolderDefaultTags", Token);
            await db.Database.ExecuteSqlAsync($"ALTER TABLE TrashEntries DROP COLUMN DefaultTags", Token);
        }

        await _services.Get<DatabaseInitializer>().InitializeAsync(Token);

        await SetAsync("RepeatedDir", BuiltInTags.Favorite);
        await ScanAsync();
        Assert.HasCount(6, await TaggedAsync(BuiltInTags.Favorite));
        var deleted = await Files.DeleteAsync("RepeatedDir", permanent: false, Me, Token);
        await _services.Get<Trash>().RestoreAsync(deleted.TrashId!.Value, Me, Token);
        Assert.IsFalse((await Defaults.GetAsync("RepeatedDir", Token)).IsEmpty);
    }

    private async Task SetAsync(string folder, params int[] tagIds)
    {
        var (status, error) = await Defaults.ChangeAsync(folder, tagIds, [], Token);
        Assert.AreEqual(DefaultTagChangeStatus.Ok, status, error);
    }

    private async Task<int> CreateTagAsync(string name) => (await Tags.CreateAsync(new TagInput(name), Token)).Tag!.Id;

    private async Task ScanAsync()
    {
        var progress = new ScanProgress();
        await _services.Get<DirectoryIndexer>().IndexAsync(_library.Root, true, progress, Token);
        Assert.AreEqual(0, progress.Errors, _log.ToString());
    }

    private async Task<FileJobView> RunAsync(FileJobRequest request)
    {
        var jobs = _services.Get<FileJobs>();
        var view = jobs.Start(request, Me, new AllowAll());
        await jobs.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return jobs.Find(view.Id)!;
    }

    /// <summary>Root-relative paths of the indexed files having the tag.</summary>
    private async Task<string[]> TaggedAsync(int tagId)
    {
        await using var db = await DbAsync();
        var root = _library.Root.Length + 1;
        return [.. (await db.FileTags.Where(l => l.TagId == tagId).Select(l => l.File!.FullName).ToListAsync(Token))
            .Select(f => f[root..].Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private async Task<int[]> TagIdsAsync(string path) =>
        (await Tags.GetFileTagsAsync(path, Token))?.Tags.Select(t => t.Id).ToArray() ?? [];

    private Task<SfeDbContext> DbAsync() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    private sealed class AllowAll : IFileJobAccess
    {
        public Task<bool> MayAsync(string path, string right) => Task.FromResult(true);

        public Task<bool> MayThroughoutAsync(string path, string right) => Task.FromResult(true);

        public bool IsProtected(string fullPath) => false;
    }
}
