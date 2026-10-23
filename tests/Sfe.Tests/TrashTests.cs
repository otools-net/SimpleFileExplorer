using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Deleting to the trash, restoring, deleting for good and the purge, in Core (<see cref="Trash"/>).</summary>
[TestClass]
public sealed class TrashTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly List<string> _links = [];
    private WarningLog _log = new();

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileOperations Files => _services.Get<FileOperations>();
    private Trash Trash => _services.Get<Trash>();
    private TagService Tags => _services.Get<TagService>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, InTheLibrary, _log.AddTo);
    }

    /// <summary>The work folder at the library's root (the server's by default): the tests look into it.</summary>
    private static void InTheLibrary(IDictionary<string, string?> settings) => settings["Sfe:Files:WorkFolder"] = "Library";

    [TestCleanup]
    public async Task CleanupAsync()
    {
        foreach (var link in _links.Where(Directory.Exists))
            Directory.Delete(link);
        await _services.DisposeAsync();
        _library.Dispose();
    }

    private async Task UseServicesAsync(Action<IDictionary<string, string?>> configure)
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, settings =>
        {
            InTheLibrary(settings);
            configure(settings);
        }, _log.AddTo);
    }

    [TestMethod]
    public async Task AFileGoesToTheTrashAndComesBackWithItsTags()
    {
        await ScanAsync();
        var tag = await TagAsync("Beach", "Media/photo.jpg");

        var deleted = await Files.DeleteAsync("Media/photo.jpg", permanent: false, Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, deleted.Status, deleted.Message);
        Assert.IsNotNull(deleted.TrashId);
        Assert.IsFalse(File.Exists(_library.FullPath("Media/photo.jpg")));
        var page = await Trash.ListAsync(null, null, _ => true, Token);
        var entry = page.Items.Single();
        Assert.AreEqual("Media/photo.jpg", entry.Path);
        Assert.AreEqual(3 * 1024, entry.Size);
        Assert.AreEqual("tester", entry.DeletedBy);
        var holder = Path.Combine(_library.Root, ".sfe", "trash", await HolderAsync(entry.Id));
        Assert.IsTrue(File.Exists(Path.Combine(holder, "photo.jpg")));
        await using (var db = await DbAsync())
        {
            Assert.IsFalse(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/photo.jpg"), Token), _log.ToString());
            Assert.AreEqual(0, await db.MissingFiles.CountAsync(Token), "Deleted, not missing");
        }

        var restored = await Trash.RestoreAsync(entry.Id, Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, restored.Status, restored.Message);
        Assert.AreEqual("Media/photo.jpg", restored.Item!.Path);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Media/photo.jpg"));
        Assert.IsEmpty((await Trash.ListAsync(null, null, _ => true, Token)).Items);
        Assert.IsFalse(Directory.Exists(holder));
    }

    [TestMethod]
    public async Task AFolderComesBackWhereItWasOrBesideWhatTookItsName()
    {
        await ScanAsync();
        var tag = await TagAsync("Kept", "RepeatedDir/ChildDir/AFile1.txt");

        var deleted = await Files.DeleteAsync("RepeatedDir", permanent: false, Me, Token);
        Directory.CreateDirectory(_library.FullPath("RepeatedDir"));
        var restored = await Trash.RestoreAsync(deleted.TrashId!.Value, Me, Token);
        await _services.Get<ScanCoordinator>().Completion!;

        Assert.AreEqual(FileOperationStatus.Done, restored.Status, restored.Message);
        Assert.AreEqual("RepeatedDir (2)", restored.Item!.Path);
        Assert.IsTrue(File.Exists(_library.FullPath("RepeatedDir (2)/RepeatedDir/RDFile2.txt")));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("RepeatedDir (2)/ChildDir/AFile1.txt"));
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("RepeatedDir (2)/RDFile1.txt"), Token),
            "A scan of the restored folder indexes the rest");
    }

    [TestMethod]
    public async Task ARestoreMakesTheFolderItWasInAgain()
    {
        var file = await Files.DeleteAsync("ChildDir/AFile1.txt", permanent: false, Me, Token);
        await Files.DeleteAsync("ChildDir", permanent: true, Me, Token);

        var restored = await Trash.RestoreAsync(file.TrashId!.Value, Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, restored.Status, restored.Message);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/AFile1.txt")));
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("ChildDir/AFile1.txt"), Token), "A restored file is indexed at once");
    }

    [TestMethod]
    public async Task DeletingForGoodLeavesNothingBehind()
    {
        await ScanAsync();
        await TagAsync("Gone", "Media/photo.jpg");
        var thumbnail = _services.Get<ThumbnailService>().GetCachePath(_library.FullPath("Media/photo.jpg"));
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        await File.WriteAllBytesAsync(thumbnail, [1], Token);

        var file = await Files.DeleteAsync("Media/photo.jpg", permanent: true, Me, Token);
        var folder = await Files.DeleteAsync("RepeatedDir", permanent: true, Me, Token);
        await Trash.CacheDeletes;

        Assert.AreEqual(FileOperationStatus.Done, file.Status, file.Message);
        Assert.IsNull(file.TrashId);
        Assert.AreEqual(FileOperationStatus.Done, folder.Status, folder.Message);
        Assert.IsFalse(File.Exists(_library.FullPath("Media/photo.jpg")));
        Assert.IsFalse(Directory.Exists(_library.FullPath("RepeatedDir")));
        Assert.IsFalse(File.Exists(thumbnail));
        Assert.IsEmpty((await Trash.ListAsync(null, null, _ => true, Token)).Items);
        await using var db = await DbAsync();
        Assert.IsFalse(await db.Directories.AnyAsync(d => d.FullName.StartsWith(_library.FullPath("RepeatedDir")), Token));
        Assert.AreEqual(0, await db.MissingFiles.CountAsync(Token));
    }

    [TestMethod]
    public async Task LinksAreDeletedAsLinksNeverWhatTheyLeadTo()
    {
        if (!TryLinkFolder("ChildDir/Into", "SpecialDir") || !TryLinkFolder("Shortcut", "Media"))
            Assert.Inconclusive("Folder links can't be made here.");

        var inside = await Files.DeleteAsync("ChildDir", permanent: true, Me, Token);
        var link = await Files.DeleteAsync("Shortcut", permanent: false, Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, inside.Status, inside.Message);
        Assert.AreEqual(FileOperationStatus.Done, link.Status, link.Message);
        Assert.IsTrue(File.Exists(_library.FullPath("SpecialDir/SDEspañol")), "What the link inside led to stays");
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")), "What the trashed link led to stays");
        Assert.IsFalse(Directory.Exists(_library.FullPath("Shortcut")));

        var restored = await Trash.RestoreAsync(link.TrashId!.Value, Me, Token);
        Assert.AreEqual(FileOperationStatus.Done, restored.Status, restored.Message);
        Assert.IsTrue(FileTree.IsLink(_library.FullPath("Shortcut")));
        Assert.IsTrue(File.Exists(_library.FullPath("Shortcut/photo.jpg")));
    }

    [TestMethod]
    public async Task AFileInUseStaysWhereItIs()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Only Windows keeps open files from being moved.");

        FileOperationResult result;
        using (File.Open(_library.FullPath("RFile1.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await Files.DeleteAsync("RFile1.txt", permanent: false, Me, Token);

        Assert.AreEqual(FileOperationStatus.Failed, result.Status);
        StringAssert.Contains(result.Message, "in use");
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
        Assert.IsEmpty((await Trash.ListAsync(null, null, _ => true, Token)).Items, "No entry left for what didn't go");
    }

    [TestMethod]
    public async Task NothingIsDeletedWhileAScanRunsOverIt()
    {
        var scans = _services.Get<ScanCoordinator>();
        var hold = scans.TryBeginOperation(_library.FullPath("Media"))!;
        Assert.IsTrue(scans.TryStart(""));

        var result = await Files.DeleteAsync("ChildDir", permanent: false, Me, Token);
        hold.Dispose();
        await scans.Completion!;

        Assert.AreEqual(FileOperationStatus.ScanRunning, result.Status);
        Assert.IsTrue(Directory.Exists(_library.FullPath("ChildDir")));
        Assert.AreEqual(FileOperationStatus.NotFound, (await Files.DeleteAsync("", permanent: true, Me, Token)).Status, "Never the root");
    }

    [TestMethod]
    public async Task ThePurgeDeletesWhatStayedTooLong()
    {
        var old = await Files.DeleteAsync("RFile1.txt", permanent: false, Me, Token);
        var recent = await Files.DeleteAsync("ChildDir", permanent: false, Me, Token);
        await Trash.Completion;
        var holder = Path.Combine(_library.Root, ".sfe", "trash", await HolderAsync(old.TrashId!.Value));
        await using (var db = await DbAsync())
            await db.TrashEntries.Where(t => t.Id == old.TrashId).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTime.UtcNow.AddDays(-31)), Token);

        Assert.IsTrue(Trash.TryStartPurge());
        await Trash.Completion;

        CollectionAssert.AreEqual(new[] { recent.TrashId!.Value }, (await Trash.ListAsync(null, null, _ => true, Token)).Items.Select(i => i.Id).ToArray());
        Assert.IsFalse(Directory.Exists(holder));
        var runs = await _services.Get<TaskHistory>().ListAsync(BackgroundTaskKind.TrashPurge, null, 10, Token);
        Assert.IsTrue(runs.Runs.Any(r => r.Outcome == TaskOutcome.Completed && r.Summary!.Contains("1 deleted for good")), string.Join(" | ", runs.Runs.Select(r => r.Summary)));
    }

    [TestMethod]
    public async Task WithoutRetentionNothingIsPurged()
    {
        await UseServicesAsync(settings => settings["Sfe:Files:Trash:RetentionDays"] = "0");
        var old = await Files.DeleteAsync("RFile1.txt", permanent: false, Me, Token);
        await using (var db = await DbAsync())
            await db.TrashEntries.ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTime.UtcNow.AddYears(-5)), Token);

        Assert.IsTrue(Trash.TryStartPurge());
        await Trash.Completion;

        Assert.AreEqual(old.TrashId, (await Trash.ListAsync(null, null, _ => true, Token)).Items.Single().Id);
    }

    [TestMethod]
    public async Task TheTrashCanBeInTheServersDataFolder()
    {
        await UseServicesAsync(settings => settings["Sfe:Files:WorkFolder"] = "Server");

        var deleted = await Files.DeleteAsync("ChildDir", permanent: false, Me, Token);
        var stored = Path.Combine(_library.ThumbnailPath, WorkFolder.ServerFolderName, Trash.FolderName, await HolderAsync(deleted.TrashId!.Value), "ChildDir");

        Assert.IsTrue(Directory.Exists(stored));
        Assert.IsFalse(Directory.Exists(Path.Combine(_library.Root, ".sfe")));
        // The setting changing meanwhile: the entry knows where it is.
        await UseServicesAsync(settings => settings["Sfe:Files:WorkFolder"] = "Library");
        Assert.AreEqual(FileOperationStatus.Done, (await Trash.RestoreAsync(deleted.TrashId!.Value, Me, Token)).Status);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/AFile2")));
    }

    [TestMethod]
    public async Task TheTrashPagesNewestFirstWithWhatTheAskerMaySee()
    {
        foreach (var name in new[] { "RFile1.txt", "RFile2", "ChildDir", "Media" })
            await Files.DeleteAsync(name, permanent: false, Me, Token);

        var first = await Trash.ListAsync(null, 2, item => item.Name != "ChildDir", Token);
        var second = await Trash.ListAsync(first.Next, 2, item => item.Name != "ChildDir", Token);

        CollectionAssert.AreEqual(new[] { "Media", "RFile2" }, first.Items.Select(i => i.Name).ToArray());
        Assert.IsTrue(first.HasMore);
        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, second.Items.Select(i => i.Name).ToArray());
        Assert.IsFalse(second.HasMore);
        Assert.AreEqual(30, first.RetentionDays);
    }

    [TestMethod]
    public void AFolderMovedToAnotherDriveIsCopiedWholeOrNotAtAll()
    {
        var hidden = _library.AddFile("ChildDir/.hidden", 10);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var copy = Path.Combine(_library.BasePath, "Other drive", "ChildDir");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);

        FileTree.MoveByCopy(_library.FullPath("ChildDir"), copy);

        Assert.IsFalse(Directory.Exists(_library.FullPath("ChildDir")));
        CollectionAssert.AreEquivalent(new[] { ".hidden", "AFile1.txt", "AFile2" }, Directory.GetFiles(copy).Select(Path.GetFileName).ToArray());

        if (!TryLinkFolder("RepeatedDir/Into", "SpecialDir"))
            Assert.Inconclusive("Folder links can't be made here.");
        var refused = Path.Combine(_library.BasePath, "Other drive", "RepeatedDir");
        Assert.ThrowsExactly<LinksInsideException>(() => FileTree.MoveByCopy(_library.FullPath("RepeatedDir"), refused));
        Assert.IsFalse(Directory.Exists(refused), "No half copy");
        Assert.IsTrue(File.Exists(_library.FullPath("RepeatedDir/RDFile1.txt")));
        Assert.IsTrue(File.Exists(_library.FullPath("SpecialDir/SDEspañol")));
    }

    [TestMethod]
    public async Task AnIndexFromBeforeTheTrashGetsItsTables()
    {
        await using (var db = await DbAsync())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE TrashTags", Token);
            await db.Database.ExecuteSqlRawAsync("DROP TABLE TrashEntries", Token);
        }

        await _services.Get<DatabaseInitializer>().InitializeAsync(Token);
        var deleted = await Files.DeleteAsync("RFile1.txt", permanent: false, Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, deleted.Status, deleted.Message);
        Assert.HasCount(1, (await Trash.ListAsync(null, null, _ => true, Token)).Items);
    }

    /// <summary>The trash's own folder of an entry.</summary>
    private async Task<string> HolderAsync(int id)
    {
        await using var db = await DbAsync();
        return await db.TrashEntries.Where(t => t.Id == id).Select(t => t.Folder).SingleAsync(Token);
    }

    private async Task<ScanStatus> ScanAsync()
    {
        var coordinator = _services.Get<ScanCoordinator>();
        Assert.IsTrue(coordinator.TryStart(""));
        await coordinator.Completion!;
        return coordinator.Status;
    }

    private async Task<int> TagAsync(string name, params string[] paths)
    {
        var tag = (await Tags.CreateAsync(new TagInput(name), Token)).Tag!.Id;
        var (result, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: [tag]), Token);
        Assert.IsNotNull(result, errors is null ? null : string.Join(" ", errors.SelectMany(e => e.Value)));
        return tag;
    }

    private async Task<int[]> TagIdsAsync(string path) =>
        (await Tags.GetFileTagsAsync(path, Token))!.Tags.Select(t => t.Id).ToArray();

    private Task<SfeDbContext> DbAsync() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    private bool TryLinkFolder(string link, string target)
    {
        var linkPath = _library.FullPath(link);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{_library.FullPath(target)}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                })!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    return false;
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, _library.FullPath(target));
            }
            _links.Add(linkPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
