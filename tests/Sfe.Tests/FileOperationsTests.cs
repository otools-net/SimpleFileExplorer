using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>New folders and renames in Core (<see cref="FileOperations"/>, <see cref="PathMoves"/>) and the work folder.</summary>
[TestClass]
public sealed class FileOperationsTests
{
    private static readonly FileActor Me = new(null, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileOperations Files => _services.Get<FileOperations>();
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

    /* ---------- New folder ---------- */

    [TestMethod]
    public async Task ANewFolderIsIndexedWhereItsParentIs()
    {
        await ScanAsync();

        var result = await Files.CreateFolderAsync("ChildDir", "New folder", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("ChildDir/New folder", result.Item!.Path);
        Assert.AreEqual(ItemKind.Directory, result.Item.Kind);
        Assert.IsTrue(Directory.Exists(_library.FullPath("ChildDir/New folder")));
        await using var db = await DbAsync();
        var parent = await db.Directories.SingleAsync(d => d.FullName == _library.FullPath("ChildDir"), Token);
        var created = await db.Directories.SingleAsync(d => d.FullName == _library.FullPath("ChildDir/New folder"), Token);
        Assert.AreEqual(parent.Id, created.ParentId);
        Assert.AreEqual("New folder", created.Name);
    }

    [TestMethod]
    public async Task ANewFolderWhereNothingIsIndexedStaysOutOfTheIndex()
    {
        var result = await Files.CreateFolderAsync("", "Fresh", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        await using var db = await DbAsync();
        Assert.IsFalse(await db.Directories.AnyAsync(Token));
    }

    /* ---------- New file ---------- */

    [TestMethod]
    public async Task ANewFileIsEmptyIndexedAndNeverOverAnother()
    {
        await ScanAsync();
        var changed = new List<string>();
        _services.Get<LibraryBrowser>().FolderChanged += changed.Add;

        var result = await Files.CreateFileAsync("ChildDir", "notes.txt", Me, Token);
        var again = await Files.CreateFileAsync("ChildDir", "notes.txt", Me, Token);
        var overFolder = await Files.CreateFileAsync("", "ChildDir", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("ChildDir/notes.txt", result.Item!.Path);
        Assert.AreEqual(0, new FileInfo(_library.FullPath("ChildDir/notes.txt")).Length);
        Assert.AreEqual(FileOperationStatus.Exists, again.Status);
        Assert.AreEqual(FileOperationStatus.Exists, overFolder.Status);
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.CreateFileAsync("", "a/b.txt", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, (await Files.CreateFileAsync("Nothing", "x.txt", Me, Token)).Status);
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("ChildDir/notes.txt"), Token), "Indexed at once");
        CollectionAssert.Contains(changed, "ChildDir", "The change feed hears of it");
    }

    [TestMethod]
    public async Task NamesThatCantBeUsedAreRefused()
    {
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.CreateFolderAsync("", "a/b", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.CreateFolderAsync("", "..", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.CreateFolderAsync("", "CON", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.CreateFolderAsync("", "", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.CreateFolderAsync("", LibraryPaths.WorkFolderName, Me, Token)).Status,
            "SFE's work folder at the root");
        Assert.AreEqual(FileOperationStatus.Done, (await Files.CreateFolderAsync("ChildDir", LibraryPaths.WorkFolderName, Me, Token)).Status,
            "Only the root's is reserved");
        Assert.AreEqual(FileOperationStatus.NotFound, (await Files.CreateFolderAsync("Nowhere", "x", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, (await Files.CreateFolderAsync("RFile1.txt", "x", Me, Token)).Status, "Not a folder");
        Assert.AreEqual(FileOperationStatus.NotFound, (await Files.CreateFolderAsync("../outside", "x", Me, Token)).Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(_library.BasePath, "outside")));
        Assert.AreEqual(FileOperationStatus.Invalid, (await Files.RenameAsync("RFile1.txt", "x:y", Me, Token)).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, (await Files.RenameAsync("", "Root2", Me, Token)).Status, "The root is not renamed");
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
    }

    [TestMethod]
    public async Task AnExistingNameIsNeverReplaced()
    {
        var folder = await Files.CreateFolderAsync("", "ChildDir", Me, Token);
        var file = await Files.CreateFolderAsync("", "RFile1.txt", Me, Token);
        var renamedFile = await Files.RenameAsync("RFile1.txt", "RFile2", Me, Token);
        var renamedFolder = await Files.RenameAsync("ChildDir", "RepeatedDir", Me, Token);
        var otherCase = await Files.CreateFolderAsync("", "childdir", Me, Token);

        Assert.AreEqual(FileOperationStatus.Exists, folder.Status);
        Assert.AreEqual(FileOperationStatus.Exists, file.Status);
        Assert.AreEqual(FileOperationStatus.Exists, renamedFile.Status);
        Assert.AreEqual(FileOperationStatus.Exists, renamedFolder.Status);
        Assert.AreEqual(10, new FileInfo(_library.FullPath("RFile1.txt")).Length);
        Assert.AreEqual(2048, new FileInfo(_library.FullPath("RFile2")).Length);
        // One folder for Windows, two for Linux.
        Assert.AreEqual(LibraryPaths.IsCaseInsensitiveFileSystem ? FileOperationStatus.Exists : FileOperationStatus.Done, otherCase.Status);
    }

    /* ---------- Rename ---------- */

    [TestMethod]
    public async Task ARenamedFileKeepsItsTagsAndPreviews()
    {
        await ScanAsync();
        var tag = (await Tags.CreateAsync(new TagInput("Beach"), Token)).Tag!.Id;
        await TagAsync(tag, "Media/photo.jpg");
        var thumbnails = _services.Get<ThumbnailService>();
        var oldThumbnail = WriteCache(thumbnails.GetCachePath(_library.FullPath("Media/photo.jpg")));
        await using var before = await DbAsync();
        var id = (await before.Files.SingleAsync(f => f.FullName == _library.FullPath("Media/photo.jpg"), Token)).Id;

        var result = await Files.RenameAsync("Media/photo.jpg", "beach day.jpeg", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("Media/beach day.jpeg", result.Item!.Path);
        Assert.IsFalse(File.Exists(_library.FullPath("Media/photo.jpg")));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/beach day.jpeg")));
        CollectionAssert.AreEqual(new[] { tag }, (await Tags.GetFileTagsAsync("Media/beach day.jpeg", Token))!.Tags.Select(t => t.Id).ToArray());
        await using var db = await DbAsync();
        var row = await db.Files.SingleAsync(f => f.Id == id, Token);
        Assert.AreEqual(_library.FullPath("Media/beach day.jpeg"), row.FullName);
        Assert.AreEqual("beach day", row.Name);
        Assert.AreEqual(".jpeg", row.Extension);
        Assert.IsFalse(File.Exists(oldThumbnail));
        Assert.IsTrue(File.Exists(thumbnails.GetCachePath(_library.FullPath("Media/beach day.jpeg"))));

        var rescan = await ScanAsync();
        Assert.AreEqual(0, rescan.Removed, "The index already had the file at its new place");
        Assert.AreEqual(0, await db.MissingFiles.CountAsync(Token));
    }

    [TestMethod]
    public async Task ARenamedFolderTakesEverythingKeptByPathWithIt()
    {
        await UseServicesAsync(settings =>
        {
            settings["Sfe:Scan:ExcludedFolders:0"] = "RepeatedDir/RepeatedDir";
            settings["Sfe:Scan:ExcludedFolders:1"] = "SpecialDir";
            settings["Sfe:Trickplay:Folders:0"] = "RepeatedDir";
            settings["Sfe:Trickplay:ExcludedFolders:0"] = "RepeatedDir/ChildDir";
            settings["Sfe:Thumbnails:ExcludedFolders:0"] = "RepeatedDir";
            settings["Sfe:Editor:OutputFolder"] = "RepeatedDir/Edited";
        });
        await ScanAsync();
        var tag = (await Tags.CreateAsync(new TagInput("Kept"), Token)).Tag!.Id;
        await TagAsync(tag, "RepeatedDir/ChildDir/AFile1.txt");
        var trickplay = _services.Get<TrickplayService>();
        var oldTrickplay = trickplay.GetCacheFolder(_library.FullPath("RepeatedDir/RDFile1.txt"));
        WriteCache(Path.Combine(oldTrickplay, "info.json"));
        await using (var setup = await DbAsync())
        {
            setup.TaskSchedules.Add(new TaskSchedule { Task = BackgroundTaskKind.Scan, Path = "RepeatedDir/ChildDir", Kind = ScheduleKind.Once, Start = DateTime.UtcNow.AddDays(1) });
            setup.TaskSchedules.Add(new TaskSchedule { Task = BackgroundTaskKind.Scan, Path = "RepeatedDirectory", Kind = ScheduleKind.Once, Start = DateTime.UtcNow.AddDays(1) });
            await setup.SaveChangesAsync(Token);
        }
        await using var before = await DbAsync();
        var folderId = (await before.Directories.SingleAsync(d => d.FullName == _library.FullPath("RepeatedDir"), Token)).Id;
        var childId = (await before.Directories.SingleAsync(d => d.FullName == _library.FullPath("RepeatedDir/ChildDir"), Token)).Id;
        var rows = await before.Files.CountAsync(Token);

        var result = await Files.RenameAsync("RepeatedDir", "Renamed", Me, Token);
        await _services.Get<PathMoves>().CacheMoves;

        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual(ItemKind.Directory, result.Item!.Kind);
        await using var db = await DbAsync();
        var folder = await db.Directories.SingleAsync(d => d.Id == folderId, Token);
        Assert.AreEqual(_library.FullPath("Renamed"), folder.FullName);
        Assert.AreEqual("Renamed", folder.Name);
        var child = await db.Directories.SingleAsync(d => d.Id == childId, Token);
        Assert.AreEqual(_library.FullPath("Renamed/ChildDir"), child.FullName);
        Assert.AreEqual(folderId, child.ParentId);
        Assert.IsFalse(await db.Files.AnyAsync(f => f.FullName.StartsWith(_library.FullPath("RepeatedDir")), Token));
        Assert.AreEqual(rows, await db.Files.CountAsync(Token));
        CollectionAssert.AreEqual(new[] { tag }, (await Tags.GetFileTagsAsync("Renamed/ChildDir/AFile1.txt", Token))!.Tags.Select(t => t.Id).ToArray());

        var options = _services.Get<IOptionsMonitor<SfeOptions>>().CurrentValue;
        CollectionAssert.AreEqual(new[] { "Renamed/RepeatedDir", "SpecialDir" }, options.Scan.ExcludedFolders);
        CollectionAssert.AreEqual(new[] { "Renamed" }, options.Trickplay.Folders);
        CollectionAssert.AreEqual(new[] { "Renamed/ChildDir" }, options.Trickplay.ExcludedFolders);
        CollectionAssert.AreEqual(new[] { "Renamed" }, options.Thumbnails.ExcludedFolders);
        Assert.AreEqual("Renamed/Edited", options.Editor.OutputFolder);
        CollectionAssert.AreEquivalent(new[] { "Renamed/ChildDir", "RepeatedDirectory" }, await db.TaskSchedules.Select(s => s.Path).ToListAsync(Token),
            "RepeatedDirectory is not inside RepeatedDir");
        Assert.IsFalse(Directory.Exists(oldTrickplay));
        Assert.IsTrue(File.Exists(Path.Combine(trickplay.GetCacheFolder(_library.FullPath("Renamed/RDFile1.txt")), "info.json")));

        var rescan = await ScanAsync();
        Assert.AreEqual(0, rescan.Removed);
        Assert.AreEqual(0, await db.MissingFiles.CountAsync(Token));
        Assert.IsFalse(await db.Directories.AnyAsync(d => d.FullName == _library.FullPath("Renamed/RepeatedDir"), Token),
            "The excluded folder is still left out of the index");
    }

    [TestMethod]
    public async Task ACaseOnlyRenameChangesTheSpelling()
    {
        await ScanAsync();

        var result = await Files.RenameAsync("ChildDir", "childDIR", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("childDIR", result.Item!.Name);
        CollectionAssert.Contains(Directory.GetDirectories(_library.Root).Select(Path.GetFileName).ToList(), "childDIR");
        CollectionAssert.DoesNotContain(Directory.GetDirectories(_library.Root).Select(Path.GetFileName).ToList(), "ChildDir");
        await using var db = await DbAsync();
        var names = await db.Files.Select(f => f.FullName).ToListAsync(Token);
        Assert.IsTrue(names.Any(n => n.StartsWith(_library.FullPath("childDIR") + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
        Assert.IsFalse(names.Any(n => n.StartsWith(_library.FullPath("ChildDir") + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ScansAndOperationsOnTheSameTreeWaitForEachOther()
    {
        var scans = _services.Get<ScanCoordinator>();
        var operation = scans.TryBeginOperation(_library.FullPath("Media"));
        Assert.IsNotNull(operation);

        Assert.IsTrue(scans.TryStart(""));
        var refused = await Files.RenameAsync("ChildDir", "Other", Me, Token);
        var created = await Files.CreateFolderAsync("", "New", Me, Token);
        await Task.Delay(100, Token);
        var waiting = scans.Completion!.IsCompleted;
        operation.Dispose();
        await scans.Completion;
        var afterwards = await Files.RenameAsync("ChildDir", "Other", Me, Token);

        Assert.AreEqual(FileOperationStatus.ScanRunning, refused.Status);
        Assert.AreEqual(FileOperationStatus.ScanRunning, created.Status);
        Assert.IsFalse(waiting, "The scan waits for the operation running on its tree");
        Assert.AreEqual(ScanState.Completed, scans.Status.State);
        Assert.AreEqual(FileOperationStatus.Done, afterwards.Status, afterwards.Message);
    }

    [TestMethod]
    public async Task AFileInUseIsNotRenamed()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Only Windows keeps open files from being renamed.");
        await ScanAsync();

        FileOperationResult file, folder;
        using (File.Open(_library.FullPath("RFile1.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            file = await Files.RenameAsync("RFile1.txt", "Other.txt", Me, Token);
        using (File.Open(_library.FullPath("ChildDir/AFile1.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            folder = await Files.RenameAsync("ChildDir", "Other", Me, Token);

        Assert.AreEqual(FileOperationStatus.Failed, file.Status);
        StringAssert.Contains(file.Message, "in use");
        Assert.IsFalse(file.Message!.Contains(_library.Root, StringComparison.OrdinalIgnoreCase), "No server path in the answer");
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
        Assert.AreEqual(FileOperationStatus.Failed, folder.Status);
        Assert.IsTrue(Directory.Exists(_library.FullPath("ChildDir")));
        Assert.IsFalse(Directory.Exists(_library.FullPath("Other")));
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("RFile1.txt"), Token), "The index is unchanged");
    }

    /* ---------- Work folder ---------- */

    [TestMethod]
    public async Task TheWorkFolderIsNeverListedScannedOrReached()
    {
        await UseServicesAsync(settings => settings["Sfe:ShowHiddenItems"] = "true");
        var work = _services.Get<WorkFolder>();
        var trash = work.Get("trash");
        await File.WriteAllTextAsync(Path.Combine(trash, "deleted.txt"), "x", Token);
        var paths = _services.Get<LibraryPaths>();

        Assert.AreEqual(Path.Combine(_library.Root, ".sfe", "trash"), trash);
        if (OperatingSystem.IsWindows())
            Assert.IsTrue(new DirectoryInfo(work.LibraryPath).Attributes.HasFlag(FileAttributes.Hidden));
        Assert.IsFalse(paths.TryResolve(".sfe", out _));
        Assert.IsFalse(paths.TryResolve(".sfe/trash/deleted.txt", out _));
        Assert.IsFalse(paths.TryResolve("ChildDir/../.sfe/trash", out _));
        if (OperatingSystem.IsWindows())
        {
            Assert.IsFalse(paths.TryResolve(".SFE/trash", out _));
            Assert.IsFalse(paths.TryResolve(".sfe./trash", out _), "Windows opens “.sfe.” as .sfe");
        }
        Assert.IsTrue(paths.TryResolve(".sfe-not/x", out _));
        var listing = _services.Get<LibraryBrowser>().List("", 0, 1000)!;
        CollectionAssert.DoesNotContain(listing.Items.Select(i => i.Name).ToList(), ".sfe");

        await ScanAsync();
        await using var db = await DbAsync();
        Assert.IsFalse(await db.Files.AnyAsync(f => f.Name == "deleted", Token));
        Assert.IsFalse(await db.Directories.AnyAsync(d => d.Name == ".sfe", Token));
    }

    [TestMethod]
    public async Task TheWorkFolderCanBeInTheServersDataFolder()
    {
        await UseServicesAsync(settings => settings["Sfe:Files:WorkFolder"] = "Server");
        var work = _services.Get<WorkFolder>();

        var uploads = work.Get("uploads");

        Assert.AreEqual(Path.Combine(_library.ThumbnailPath, WorkFolder.ServerFolderName, "uploads"), uploads);
        Assert.IsFalse(Directory.Exists(work.LibraryPath));
        Assert.IsTrue(work.Contains(Path.Combine(uploads, "x.part")));
        Assert.IsTrue(work.Contains(Path.Combine(_library.Root, ".sfe")), "The root's name stays reserved");
    }

    private async Task<ScanStatus> ScanAsync()
    {
        var coordinator = _services.Get<ScanCoordinator>();
        Assert.IsTrue(coordinator.TryStart(""));
        await coordinator.Completion!;
        var status = coordinator.Status;
        Assert.AreEqual(ScanState.Completed, status.State, status.Message);
        return status;
    }

    private async Task TagAsync(int tag, params string[] paths)
    {
        var (result, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: [tag]), Token);
        Assert.IsNotNull(result, errors is null ? null : string.Join(" ", errors.SelectMany(e => e.Value)));
    }

    private Task<SfeDbContext> DbAsync() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    private static string WriteCache(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
