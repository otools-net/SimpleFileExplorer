using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;
using Sfe.Core.Transfers;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>The library's side of transfers with connections (<see cref="LibraryTransferEndpoint"/>).</summary>
[TestClass]
public sealed class LibraryTransferEndpointTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly ConcurrentBag<string> _partials = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

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
    public async Task ReadsAFileFromAnOffset()
    {
        await File.WriteAllTextAsync(_library.FullPath("ChildDir/AFile1.txt"), "0123456789", Token);
        await using var side = Open();

        var entry = (await side.GetAsync("ChildDir/AFile1.txt", Token))!;
        await using var stream = await side.OpenReadAsync(entry, 4, Token);
        using var reader = new StreamReader(stream);

        Assert.AreEqual(10, entry.Size);
        Assert.IsFalse(entry.IsFolder);
        Assert.AreEqual("456789", await reader.ReadToEndAsync(Token));
    }

    [TestMethod]
    public async Task ListsAFolderWithoutWhatThePersonMayNotOpen()
    {
        await using var side = Open(new FakeAccess { Refuse = (path, _) => path == "RepeatedDir/ChildDir" });

        var names = new List<string>();
        await foreach (var entry in side.ListAsync("RepeatedDir", Token))
            names.Add(entry.Path);

        CollectionAssert.AreEquivalent(
            new[] { "RepeatedDir/RDFile1.txt", "RepeatedDir/RDFile2.txt", "RepeatedDir/RepeatedDir" },
            names
        );
    }

    [TestMethod]
    public async Task AFileWrittenInPartsTakesItsNameDateAndDefaultTagsAtCommit()
    {
        var tags = _services.Get<TagService>();
        var holiday = (await tags.CreateAsync(new TagInput("Holiday"), Token)).Tag!.Id;
        var (status, error) = await _services.Get<FolderDefaultTags>().ChangeAsync("ChildDir", [holiday], [], Token);
        Assert.AreEqual(DefaultTagChangeStatus.Ok, status, error);
        var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await using var side = Open();

        await using var write = await side.BeginWriteAsync("ChildDir", "new.bin", 10, date, Token);
        await write.WriteAsync(new byte[] { 1, 2, 3, 4 }, Token);
        var partial = _library.FullPath("ChildDir/new.bin" + FileJobs.PartialSuffix);
        Assert.IsTrue(File.Exists(partial), "Written into its partial file");
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/new.bin")));
        Assert.AreEqual(4, write.Offset);
        Assert.Contains(partial, _partials);
        await write.WriteAsync(new byte[] { 5, 6, 7, 8, 9, 10 }, Token);
        var written = await write.CommitAsync(Token);
        await side.AfterWriteAsync(written, Token);

        Assert.IsFalse(File.Exists(partial));
        Assert.AreEqual("ChildDir/new.bin", written.Path);
        Assert.AreEqual(10, written.Size);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            await File.ReadAllBytesAsync(_library.FullPath("ChildDir/new.bin"), Token)
        );
        Assert.AreEqual(date, File.GetLastWriteTimeUtc(_library.FullPath("ChildDir/new.bin")));
        var fileTags = await tags.GetFileTagsAsync("ChildDir/new.bin", Token);
        CollectionAssert.AreEqual(new[] { holiday }, fileTags!.Tags.Select(t => t.Id).ToArray());
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("ChildDir/new.bin"), Token));
    }

    [TestMethod]
    public async Task ACommitAnswersExistsWhenTheNameWasTakenMeanwhileAndTheDropLeavesNothing()
    {
        await using var side = Open();
        var write = await side.BeginWriteAsync("ChildDir", "late.txt", 3, null, Token);
        await write.WriteAsync(new byte[] { 1, 2, 3 }, Token);
        _library.AddFile("ChildDir/late.txt", 5);

        var error = await Assert.ThrowsAsync<RemoteStorageException>(() => write.CommitAsync(Token));
        await write.AbortAsync(Token);

        Assert.AreEqual(RemoteErrorKind.Exists, error.Kind);
        Assert.AreEqual(5, new FileInfo(_library.FullPath("ChildDir/late.txt")).Length);
        Assert.IsEmpty(Directory.GetFiles(_library.FullPath("ChildDir"), "*" + FileJobs.PartialSuffix));
        await AssertFailsAsync(RemoteErrorKind.Exists, () => side.BeginWriteAsync("", "RFile1.txt", 1, null, Token));
    }

    [TestMethod]
    public async Task AFreeNameIsTheNameWhenFreeElseTheLibrarysKeepBothName()
    {
        await using var side = Open();

        Assert.AreEqual("free.txt", await side.FreeNameAsync("", "free.txt", false, Token));
        Assert.AreEqual("RFile1 (2).txt", await side.FreeNameAsync("", "RFile1.txt", false, Token));
        Assert.AreEqual("ChildDir (2)", await side.FreeNameAsync("", "ChildDir", true, Token));
        Assert.AreEqual(".sfe (2)", await side.FreeNameAsync("", ".sfe", true, Token), "The work folder's name");
    }

    [TestMethod]
    public async Task ReplacedAndMovedAwayItemsGoToTheTrash()
    {
        await using var side = Open(writeRight: Rights.MoveFiles);

        await side.RemoveForReplaceAsync((await side.GetAsync("ChildDir/AFile1.txt", Token))!, Token);
        await side.DeleteSourceAsync((await side.GetAsync("RepeatedDir/ChildDir", Token))!, Token);

        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/AFile1.txt")));
        Assert.IsFalse(Directory.Exists(_library.FullPath("RepeatedDir/ChildDir")));
        var trashed = await _services.Get<Trash>().ListAsync(null, null, _ => true, Token);
        CollectionAssert.AreEquivalent(
            new[] { "ChildDir/AFile1.txt", "RepeatedDir/ChildDir" },
            trashed.Items.Select(i => i.Path).ToArray()
        );
    }

    [TestMethod]
    public async Task ReplacingNeedsTheRightToDeleteAndMovingAwayTheRightToMove()
    {
        await using var noDelete = Open(
            new FakeAccess { Refuse = (_, right) => right == Rights.DeleteFiles },
            Rights.MoveFiles
        );
        await using var noMove = Open(
            new FakeAccess { Refuse = (_, right) => right == Rights.MoveFiles },
            Rights.MoveFiles
        );
        var file = (await noDelete.GetAsync("ChildDir/AFile1.txt", Token))!;

        await AssertFailsAsync(RemoteErrorKind.Refused, () => noDelete.RemoveForReplaceAsync(file, Token));
        await AssertFailsAsync(RemoteErrorKind.Refused, () => noMove.DeleteSourceAsync(file, Token));
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/AFile1.txt")));
    }

    [TestMethod]
    public async Task PathsOutOfTheLibraryOrInTheWorkFolderAreNotFound()
    {
        _services.Get<WorkFolder>().Get("trash");
        await using var side = Open();

        Assert.IsNull(await side.GetAsync("../Root/RFile1.txt", Token));
        Assert.IsNull(await side.GetAsync(".sfe", Token));
        Assert.IsNull(await side.GetAsync("Nowhere.txt", Token));
        await AssertFailsAsync(RemoteErrorKind.NotFound, () => side.BeginWriteAsync("..", "x.txt", 1, null, Token));
        await AssertFailsAsync(RemoteErrorKind.NotFound, () => side.BeginWriteAsync(".sfe", "x.txt", 1, null, Token));
        await AssertFailsAsync(RemoteErrorKind.InvalidName, () => side.BeginWriteAsync("", ".sfe", 1, null, Token));
        await AssertFailsAsync(RemoteErrorKind.InvalidName, () => side.BeginWriteAsync("", "a|b", 1, null, Token));
        await AssertFailsAsync(RemoteErrorKind.NotFound, () => side.CreateFolderAsync("..", "x", Token));
        var names = new List<string>();
        await foreach (var entry in side.ListAsync("", Token))
            names.Add(entry.Name);
        CollectionAssert.DoesNotContain(names, ".sfe");
    }

    [TestMethod]
    public async Task WritingNeedsTheJobsRightAtTheTarget()
    {
        await using var side = Open(
            new FakeAccess { Refuse = (path, right) => path == "Media" && right == Rights.CopyFiles }
        );

        await AssertFailsAsync(RemoteErrorKind.Refused, () => side.BeginWriteAsync("Media", "x.txt", 1, null, Token));
        await AssertFailsAsync(RemoteErrorKind.Refused, () => side.CreateFolderAsync("Media", "x", Token));
        var made = await side.CreateFolderAsync("ChildDir", "Made", Token);
        Assert.IsTrue(made.IsFolder);
        Assert.AreEqual("ChildDir/Made", made.Path);
        Assert.AreEqual(
            "ChildDir/Made",
            (await side.CreateFolderAsync("ChildDir", "Made", Token)).Path,
            "Already there"
        );
    }

    [TestMethod]
    public async Task ALinkLeavingTheLibraryIsNotListedNorWrittenThrough()
    {
        var outside = Path.Combine(_library.BasePath, "Outside");
        Directory.CreateDirectory(outside);
        if (!TryLinkFolder(_library.FullPath("Media/outside"), outside))
            Assert.Inconclusive("Cannot create folder links here.");
        var paths = _services.Get<LibraryPaths>();
        await using var side = Open(new FakeAccess { ReadOnly = full => paths.RealRelative(full) is null });

        var names = new List<string>();
        await foreach (var entry in side.ListAsync("Media", Token))
            names.Add(entry.Name);

        CollectionAssert.DoesNotContain(names, "outside");
        await AssertFailsAsync(RemoteErrorKind.Refused, () =>
            side.BeginWriteAsync("Media/outside", "x.txt", 1, null, Token)
        );
        Assert.IsFalse(File.Exists(Path.Combine(outside, "x.txt")));
        Directory.Delete(_library.FullPath("Media/outside"));
    }

    [TestMethod]
    public async Task TheFreeSpaceKeepsTheLibrarysMinimum()
    {
        await using (var side = Open())
        {
            var space = await side.FreeSpaceAsync("Media", Token);
            var drive = new DriveInfo(Path.GetPathRoot(_library.Root)!).AvailableFreeSpace;
            var expected = Math.Max(0, drive - 1024L * 1024 * 1024);
            Assert.IsLessThan(64L << 20, Math.Abs(expected - space.FreeBytes!.Value), $"{space.FreeBytes} for {drive}");
        }

        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s =>
            s["Sfe:Files:MinimumFreeSpaceMegabytes"] = int.MaxValue.ToString()
        );
        await using var full = Open();

        Assert.AreEqual(0, (await full.FreeSpaceAsync("Media", Token)).FreeBytes);
        await AssertFailsAsync(RemoteErrorKind.TooLarge, () => full.BeginWriteAsync("Media", "x.txt", 1, null, Token));
    }

    /// <summary>A junction on Windows (no privilege needed), a symbolic link elsewhere.</summary>
    private static bool TryLinkFolder(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            )!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private LibraryTransferEndpoint Open(IFileJobAccess? access = null, string writeRight = Rights.CopyFiles) =>
        _services.Get<LibraryTransfers>().Open(Me, access ?? new FakeAccess(), writeRight, _partials);

    private static async Task AssertFailsAsync(RemoteErrorKind kind, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<RemoteStorageException>(action);
        Assert.AreEqual(kind, error.Kind, error.Message);
    }

    private Task<SfeDbContext> DbAsync() =>
        _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    /// <summary>Everything allowed but what <see cref="Refuse"/> says; the work folder protected.</summary>
    private sealed class FakeAccess : IFileJobAccess
    {
        public Func<string, string, bool> Refuse { get; init; } = (_, _) => false;

        public Func<string, bool> ReadOnly { get; init; } = _ => false;

        public Task<bool> MayAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public Task<bool> MayThroughoutAsync(string path, string right) => MayAsync(path, right);

        public bool IsProtected(string fullPath) =>
            fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);

        public bool IsReadOnly(string fullPath) => IsProtected(fullPath) || ReadOnly(fullPath);
    }
}
