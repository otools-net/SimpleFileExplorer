using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Resumable uploads in Core (<see cref="Uploads"/>).</summary>
[TestClass]
public sealed class UploadsTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly WarningLog _log = new();

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private Uploads Uploads => _services.Get<Uploads>();

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
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFileSentInChunksLandsInItsFolderIndexedWithItsDate()
    {
        var content = Bytes(300_000);
        var modified = new DateTimeOffset(2020, 5, 17, 10, 30, 0, TimeSpan.Zero);
        var upload = await StartAsync(new UploadStart("Media", "new clip.mp4", content.Length, modified));

        Assert.AreEqual(FileOperationStatus.Done, (await SendAsync(upload.Id, content, 0, 100_000)).Status);
        var second = await SendAsync(upload.Id, content, 100_000, 200_000);
        Assert.AreEqual(300_000, second.Upload!.Received);
        Assert.IsTrue(File.Exists(Path.Combine(_library.Root, ".sfe", "uploads", upload.Id + ".part")), "Kept in the work folder until finished");
        var done = await Uploads.FinishAsync(upload.Id, ConflictChoice.Ask, Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.Done, done.Status, done.Message);
        Assert.AreEqual("Media/new clip.mp4", done.Item!.Path);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(_library.FullPath("Media/new clip.mp4"), Token));
        Assert.AreEqual(modified.UtcDateTime, File.GetLastWriteTimeUtc(_library.FullPath("Media/new clip.mp4")));
        Assert.IsEmpty(Uploads.List(null, all: true));
        Assert.IsFalse(File.Exists(Path.Combine(_library.Root, ".sfe", "uploads", upload.Id + ".part")));
        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/new clip.mp4"), Token), "Indexed at once");
    }

    [TestMethod]
    public async Task AChunkAtAnotherOffsetIsRefusedWithWhatTheServerHas()
    {
        var content = Bytes(1000);
        var upload = await StartAsync(new UploadStart("", "a.bin", content.Length));
        await SendAsync(upload.Id, content, 0, 400);

        var again = await SendAsync(upload.Id, content, 0, 400);
        var gap = await SendAsync(upload.Id, content, 600, 400);
        var tooMuch = await Uploads.AppendAsync(upload.Id, 400, new MemoryStream(new byte[700]), Token);
        var early = await Uploads.FinishAsync(upload.Id, ConflictChoice.Ask, Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.Conflict, again.Status, "A reply lost: resend from the server's offset");
        Assert.AreEqual(400, again.Upload!.Received);
        Assert.AreEqual(FileOperationStatus.Conflict, gap.Status);
        Assert.AreEqual(FileOperationStatus.Invalid, tooMuch.Status);
        Assert.AreEqual(400, new FileInfo(Path.Combine(_library.Root, ".sfe", "uploads", upload.Id + ".part")).Length, "Nothing of a refused chunk stays");
        Assert.AreEqual(FileOperationStatus.Invalid, early.Status);
    }

    [TestMethod]
    public async Task UnfinishedUploadsSurviveARestartAndGoOn()
    {
        var content = Bytes(5000);
        var upload = await StartAsync(new UploadStart("ChildDir", "resumed.bin", content.Length));
        await SendAsync(upload.Id, content, 0, 2000);

        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, InTheLibrary, _log.AddTo);
        var listed = Uploads.List(Me.AccountId).Single();
        await SendAsync(upload.Id, content, 2000, 3000);
        var done = await Uploads.FinishAsync(upload.Id, ConflictChoice.Ask, Me, new FakeAccess(), Token);

        Assert.AreEqual(2000, listed.Received);
        Assert.AreEqual("resumed.bin", listed.Name);
        Assert.AreEqual(FileOperationStatus.Done, done.Status, done.Message);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(_library.FullPath("ChildDir/resumed.bin"), Token));
    }

    [TestMethod]
    public async Task ANameTakenIsAskedThenReplacedSkippedOrKeptBoth()
    {
        _library.AddFile("ChildDir/x.txt", 5);
        async Task<FileOperationResult> UploadAsync(ConflictChoice choice, IUploadAccess? access = null)
        {
            var upload = await StartAsync(new UploadStart("ChildDir", "x.txt", 3));
            await SendAsync(upload.Id, [1, 2, 3], 0, 3);
            return await Uploads.FinishAsync(upload.Id, choice, Me, access ?? new FakeAccess(), Token);
        }

        var asked = await UploadAsync(ConflictChoice.Ask);
        var kept = await UploadAsync(ConflictChoice.KeepBoth);
        var refused = await UploadAsync(ConflictChoice.Replace, new FakeAccess { Refuse = (_, right) => right == Rights.DeleteFiles });
        var replaced = await UploadAsync(ConflictChoice.Replace);
        var skipped = await UploadAsync(ConflictChoice.Skip);

        Assert.AreEqual(FileOperationStatus.Exists, asked.Status);
        Assert.AreEqual("ChildDir/x (2).txt", kept.Item!.Path);
        Assert.AreEqual(FileOperationStatus.Forbidden, refused.Status);
        Assert.AreEqual(FileOperationStatus.Done, replaced.Status, replaced.Message);
        Assert.AreEqual(3, new FileInfo(_library.FullPath("ChildDir/x.txt")).Length);
        Assert.AreEqual("ChildDir/x.txt", (await _services.Get<Trash>().ListAsync(null, null, _ => true, Token)).Items.Single().Path, "The file replaced is in the trash");
        Assert.AreEqual(FileOperationStatus.Skipped, skipped.Status);
        Assert.HasCount(2, Uploads.List(null, all: true), "Asked and refused ones wait for another answer; skipped is removed");
    }

    [TestMethod]
    public async Task AFolderUploadMakesItsFoldersWhereThePersonMay()
    {
        var upload = await StartAsync(new UploadStart("Media", "Trip/Day 1/a.jpg", 2));
        await SendAsync(upload.Id, [1, 2], 0, 2);
        var done = await Uploads.FinishAsync(upload.Id, ConflictChoice.Ask, Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.Done, done.Status, done.Message);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/Trip/Day 1/a.jpg")));

        var refused = await Uploads.StartAsync(new UploadStart("Media", "Private/b.jpg", 2), Me,
            new FakeAccess { Refuse = (path, right) => right == Rights.UploadFiles && path == "Media/Private" }, Token);
        Assert.AreEqual(FileOperationStatus.Forbidden, refused.Status);
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("Trip/../../escape.txt")]
    [DataRow("..")]
    [DataRow("a/./b.txt")]
    [DataRow("")]
    [DataRow("bad|name.txt")]
    public async Task NamesThatWouldEscapeOrCantBeUsedAreRefused(string name)
    {
        var result = await Uploads.StartAsync(new UploadStart("Media", name, 1), Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.Invalid, result.Status, name);
        Assert.IsFalse(File.Exists(Path.Combine(_library.BasePath, "escape.txt")));
    }

    [TestMethod]
    public async Task TheWorkFolderHiddenFoldersAndMissingFoldersAreRefused()
    {
        var work = await Uploads.StartAsync(new UploadStart(".sfe", "a.txt", 1), Me, new FakeAccess(), Token);
        var reserved = await Uploads.StartAsync(new UploadStart("", ".sfe/a.txt", 1), Me, new FakeAccess(), Token);
        var hidden = await Uploads.StartAsync(new UploadStart("SpecialDir", "a.txt", 1), Me, new FakeAccess { Refuse = (path, _) => path == "SpecialDir" }, Token);
        var missing = await Uploads.StartAsync(new UploadStart("Nowhere", "a.txt", 1), Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.NotFound, work.Status);
        Assert.AreEqual(FileOperationStatus.Invalid, reserved.Status);
        Assert.AreEqual(FileOperationStatus.NotFound, hidden.Status, "Nothing tells what one may not see");
        Assert.AreEqual(FileOperationStatus.NotFound, missing.Status);
    }

    [TestMethod]
    public async Task TheLargestSizeAndTheFreeSpaceAreKept()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s =>
        {
            InTheLibrary(s);
            s["Sfe:Files:Uploads:MaxFileSizeMegabytes"] = "1";
        }, _log.AddTo);

        var tooLarge = await Uploads.StartAsync(new UploadStart("Media", "big.bin", 2 * 1024 * 1024), Me, new FakeAccess(), Token);
        var fits = await Uploads.StartAsync(new UploadStart("Media", "small.bin", 1024), Me, new FakeAccess(), Token);
        var noRoom = await Uploads.StartAsync(new UploadStart("Media", "huge.bin", long.MaxValue / 2), Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.Invalid, tooLarge.Status);
        StringAssert.Contains(tooLarge.Message, "1 MB");
        Assert.AreEqual(FileOperationStatus.Done, fits.Status);
        Assert.AreEqual(FileOperationStatus.Invalid, noRoom.Status, "Larger than the limit too");
    }

    [TestMethod]
    public async Task AnUploadLargerThanTheFreeSpaceIsRefused()
    {
        var noRoom = await Uploads.StartAsync(new UploadStart("Media", "huge.bin", long.MaxValue / 2), Me, new FakeAccess(), Token);

        Assert.AreEqual(FileOperationStatus.Failed, noRoom.Status);
        StringAssert.Contains(noRoom.Message, "MB free");
    }

    [TestMethod]
    public async Task CancelledAndAbandonedUploadsLeaveNothing()
    {
        var cancelled = await StartAsync(new UploadStart("", "a.bin", 10));
        var abandoned = await StartAsync(new UploadStart("", "b.bin", 10));
        await SendAsync(abandoned.Id, Bytes(10), 0, 5);

        Assert.IsTrue(Uploads.Cancel(cancelled.Id, Me));
        Assert.AreEqual(0, Uploads.RemoveAbandoned(DateTimeOffset.UtcNow.AddHours(47)));
        Assert.AreEqual(1, Uploads.RemoveAbandoned(DateTimeOffset.UtcNow.AddHours(49)));

        Assert.IsEmpty(Uploads.List(null, all: true));
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(_library.Root, ".sfe", "uploads")));
    }

    private async Task<UploadInfo> StartAsync(UploadStart start)
    {
        var started = await Uploads.StartAsync(start, Me, new FakeAccess(), Token);
        Assert.AreEqual(FileOperationStatus.Done, started.Status, started.Message);
        return started.Upload!;
    }

    private Task<UploadChunkResult> SendAsync(string id, byte[] content, int offset, int count) =>
        Uploads.AppendAsync(id, offset, new MemoryStream(content, offset, count), Token);

    private static byte[] Bytes(int count) => [.. Enumerable.Range(0, count).Select(i => (byte)(i * 7))];

    /// <summary>Everything allowed but what <see cref="Refuse"/> says.</summary>
    private sealed class FakeAccess : IUploadAccess
    {
        public Func<string, string, bool> Refuse { get; init; } = (_, _) => false;

        public Task<bool> MayAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public bool IsProtected(string fullPath) => fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);
    }
}
