using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Folders merged on a name clash (<see cref="ConflictChoice.Merge"/>) by moves and copies of the library
/// (<see cref="FileJobs"/>): each name they share asked, merged again below, the emptied folder of a move gone, the
/// "for all" answers kept apart for folders and the rest.
/// </summary>
[TestClass]
public sealed class FolderMergeTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly WarningLog _log = new();

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileJobs Jobs => _services.Get<FileJobs>();
    private JobBoard Board => _services.Get<JobBoard>();

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
    public async Task AMovedFolderMergesIntoTheOneThereAskingAboutEachFileTheyShare()
    {
        // The owner's case: Target/folder1/text1.txt is there; folder1 with text1.txt and text2.txt arrives.
        _library.AddFile("Target/folder1/text1.txt", 5);
        _library.AddFile("Source/folder1/text1.txt", 7);
        _library.AddFile("Source/folder1/text2.txt", 3);
        await ScanAsync();

        var view = Start(new FileJobRequest(FileJobKind.Move, ["Source/folder1"], "Target"));
        var folders = await WaitForAsync(view.Id, j => j.Conflict?.Source.Name == "folder1");
        Assert.IsTrue(folders.Conflict!.CanMerge);
        Assert.IsTrue(Board.Answer(view.Id, ConflictChoice.Merge, forAll: false));
        var files = await WaitForAsync(view.Id, j => j.Conflict?.Source.Name == "text1.txt");
        Assert.IsFalse(files.Conflict!.CanMerge);
        Assert.IsFalse(Board.Answer(view.Id, ConflictChoice.Merge, forAll: false), "Files are never merged");
        Assert.IsTrue(Board.Answer(view.Id, ConflictChoice.Replace, forAll: false));
        await Board.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var result = Board.Find(view.Id)!.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("Target/folder1", result.NewPath);
        Assert.IsTrue(result.Merged);
        Assert.AreEqual(7, Length("Target/folder1/text1.txt"));
        Assert.AreEqual(3, Length("Target/folder1/text2.txt"));
        Assert.IsFalse(Directory.Exists(_library.FullPath("Source/folder1")), "The emptied folder goes");
        Assert.IsTrue(Directory.Exists(_library.FullPath("Source")));
        Assert.AreEqual(
            "Target/folder1/text1.txt",
            (await _services.Get<Trash>().ListAsync(null, null, _ => true, Token)).Items.Single().Path
        );
        await using var db = await DbAsync();
        var gone = _library.FullPath("Source/folder1");
        var moved = _library.FullPath("Target/folder1/text2.txt");
        Assert.IsFalse(await db.Directories.AnyAsync(d => d.FullName == gone, Token), "Its index row is forgotten");
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == moved, Token), "What it held keeps its index row");
    }

    [TestMethod]
    public async Task ACopiedFolderMergesDeepAndForAllIsKeptApartForFoldersAndFiles()
    {
        _library.AddFile("Target/a.txt", 1);
        _library.AddFile("Target/Photos/2024/one.jpg", 1);
        _library.AddFile("Source/a.txt", 2);
        _library.AddFile("Source/Photos/2024/one.jpg", 2);
        _library.AddFile("Source/Photos/2024/two.jpg", 2);
        _library.AddFile("Source/Photos/2025/three.jpg", 2);

        var view = Start(new FileJobRequest(FileJobKind.Copy, ["Source/a.txt", "Source/Photos"], "Target"));
        await WaitForAsync(view.Id, j => j.Conflict?.Source.Name == "a.txt");
        Assert.IsTrue(Board.Answer(view.Id, ConflictChoice.Replace, forAll: true));
        // Replacing every file doesn't replace a folder that may be merged: that is asked.
        var folders = await WaitForAsync(view.Id, j => j.Conflict?.Source.Name == "Photos");
        Assert.IsTrue(folders.Conflict!.CanMerge);
        Assert.IsTrue(Board.Answer(view.Id, ConflictChoice.Merge, forAll: true));
        await Board.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var job = Board.Find(view.Id)!;
        foreach (var result in job.Results)
            Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.IsTrue(job.Results[1].Merged);
        Assert.AreEqual(2, Length("Target/a.txt"));
        Assert.AreEqual(2, Length("Target/Photos/2024/one.jpg"), "Merged again below, the file replaced for all");
        Assert.AreEqual(2, Length("Target/Photos/2024/two.jpg"));
        Assert.AreEqual(2, Length("Target/Photos/2025/three.jpg"));
        Assert.AreEqual(2, Length("Source/Photos/2024/one.jpg"), "A copy keeps its source");
        Assert.IsEmpty(
            Directory.GetFileSystemEntries(_library.FullPath("Target"), "*.sfe-partial", SearchOption.AllDirectories)
        );
    }

    [TestMethod]
    public async Task MergeAskedInTheRequestStillAsksAboutFilesAndKeepsWhatWasLeftOut()
    {
        _library.AddFile("Target/folder1/text1.txt", 5);
        _library.AddFile("Source/folder1/text1.txt", 7);
        _library.AddFile("Source/folder1/text2.txt", 3);

        var view = Start(new FileJobRequest(FileJobKind.Move, ["Source/folder1"], "Target", ConflictChoice.Merge));
        await WaitForAsync(view.Id, j => j.Conflict?.Source.Name == "text1.txt");
        Assert.IsTrue(Board.Answer(view.Id, ConflictChoice.Skip, forAll: false));
        await Board.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var result = Board.Find(view.Id)!.Results.Single();
        Assert.AreEqual(FileOperationStatus.Skipped, result.Status);
        Assert.IsTrue(result.Merged);
        StringAssert.Contains(result.Message, "1 item was left out");
        Assert.AreEqual(5, Length("Target/folder1/text1.txt"));
        Assert.AreEqual(3, Length("Target/folder1/text2.txt"));
        Assert.AreEqual(7, Length("Source/folder1/text1.txt"), "What was left out stays, with its folder");
    }

    [TestMethod]
    public async Task MergeIsNotOfferedWhereThePersonMayNotWriteNorIntoTheFolderHoldingIt()
    {
        _library.AddFile("Target/folder1/text1.txt", 5);
        _library.AddFile("Source/folder1/text2.txt", 3);
        _library.AddFile("Holder/Holder/inner.txt", 3);
        var access = new FakeAccess { Refuse = (path, right) => path == "Target/folder1" && right == Rights.CopyFiles };

        var refused = Jobs.Start(
            new FileJobRequest(FileJobKind.Copy, ["Source/folder1"], "Target", ConflictChoice.Merge),
            Me,
            access
        );
        var asked = await WaitForAsync(refused.Id, j => j.State == FileJobState.Waiting);
        Assert.IsFalse(asked.Conflict!.CanMerge, "No right to copy into the folder there");
        Assert.IsFalse(Board.Answer(refused.Id, ConflictChoice.Merge, forAll: false));
        Assert.IsTrue(Board.Answer(refused.Id, ConflictChoice.Skip, forAll: false));
        var holding = Start(new FileJobRequest(FileJobKind.Move, ["Holder/Holder"], ""));
        var holder = await WaitForAsync(holding.Id, j => j.State == FileJobState.Waiting);
        Assert.IsFalse(holder.Conflict!.CanMerge, "The folder there holds the one moved");
        Assert.IsTrue(Board.Answer(holding.Id, ConflictChoice.Skip, forAll: false));
        await Board.Completion(holding.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.IsFalse(File.Exists(_library.FullPath("Target/folder1/text2.txt")));
        Assert.IsTrue(File.Exists(_library.FullPath("Holder/Holder/inner.txt")));
    }

    private FileJobView Start(FileJobRequest request) => Jobs.Start(request, Me, new FakeAccess());

    private long Length(string path) => new FileInfo(_library.FullPath(path)).Length;

    private async Task<FileJobView> WaitForAsync(int id, Func<FileJobView, bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            if (Board.Find(id) is { } view && condition(view))
                return view;
            await Task.Delay(20, Token);
        }
        throw new TimeoutException(_log.ToString());
    }

    private async Task ScanAsync()
    {
        var coordinator = _services.Get<ScanCoordinator>();
        Assert.IsTrue(coordinator.TryStart(""));
        await coordinator.Completion!;
    }

    private Task<SfeDbContext> DbAsync() =>
        _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    /// <summary>Everything allowed but what <see cref="Refuse"/> says; the work folder protected.</summary>
    private sealed class FakeAccess : IFileJobAccess
    {
        public Func<string, string, bool> Refuse { get; init; } = (_, _) => false;

        public Task<bool> MayAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public Task<bool> MayThroughoutAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public bool IsProtected(string fullPath) =>
            fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);

        public bool IsReadOnly(string fullPath) => IsProtected(fullPath);
    }
}
