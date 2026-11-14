using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Transfers;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Transfers between the library and a connection as jobs (<see cref="TransferJobs"/>): both ways, folder trees
/// listed page by page, name clashes, cancel, parts sent again, the provider's offset, an ended sign-in, free space,
/// verification by hash, a move's source into the trash, files arriving indexed.
/// </summary>
[TestClass]
public sealed class TransferJobsTests
{
    private const int Connection = 7;
    private static readonly FileActor Me = new(7, "tester");

    private readonly FakeProvider _provider = new();
    private readonly InstantClock _clock = new();
    private readonly WarningLog _log = new();
    private readonly ConcurrentBag<string> _partials = [];
    private readonly List<int> _marked = [];
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private FakeAccount _account = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private JobBoard Board => _services.Get<JobBoard>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, logging: _log.AddTo);
        _account = _provider.Account("alice");
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFileGoesToAConnectionAndBackVerifiedAndIndexed()
    {
        _provider.Capabilities = _provider.Capabilities with { ContentHash = ContentHashes.Sha256 };
        _account.WithFolder("Docs");
        var bytes = await File.ReadAllBytesAsync(_library.FullPath("Media/photo.jpg"), Token);

        var up = await RunAsync(Up(FileJobKind.Copy, ["Media/photo.jpg"], "Docs"));
        var down = await RunAsync(Down(FileJobKind.Copy, ["Docs/photo.jpg"], "ChildDir"));

        AssertDone(up, "Docs/photo.jpg");
        Assert.AreEqual(((int?)null, (int?)Connection), (up.FromConnection, up.ToConnection));
        Assert.AreEqual(((long)bytes.Length, (long)bytes.Length), (up.BytesTotal, up.BytesDone));
        AssertDone(down, "ChildDir/photo.jpg");
        Assert.AreEqual(((int?)Connection, (int?)null), (down.FromConnection, down.ToConnection));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(_library.FullPath("ChildDir/photo.jpg"), Token));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")), "A copy keeps its source");
        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);
        var arrived = _library.FullPath("ChildDir/photo.jpg");
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == arrived, Token), "Indexed when it arrived");
    }

    [TestMethod]
    public async Task AFolderTreeIsWalkedPageByPageBothWays()
    {
        var up = await RunAsync(Up(FileJobKind.Copy, ["RepeatedDir"], ""));
        var down = await RunAsync(Down(FileJobKind.Copy, ["RepeatedDir"], "ChildDir"));

        AssertDone(up, "RepeatedDir");
        foreach (var file in new[] { "RDFile1.txt", "ChildDir/AFile2", "RepeatedDir/RDFile2.txt" })
            Assert.IsTrue(_account.Exists($"RepeatedDir/{file}"), file);
        Assert.IsGreaterThanOrEqualTo(2, _account.Calls.Count(c => c == "list RepeatedDir"), "Paged by 2");
        AssertDone(down, "ChildDir/RepeatedDir");
        var copied = Directory.GetFiles(_library.FullPath("ChildDir/RepeatedDir"), "*", SearchOption.AllDirectories);
        Assert.HasCount(6, copied);
        Assert.AreEqual(60, down.BytesTotal);
    }

    [TestMethod]
    public async Task ANameClashIsAskedThenKeptBothSkippedOrReplaced()
    {
        _account.WithFile("RFile1.txt", "old");

        var started = Start(Up(FileJobKind.Copy, ["RFile1.txt"], ""));
        var waiting = await WaitForAsync(started.Id, j => j.State == FileJobState.Waiting);
        Assert.AreEqual(("RFile1.txt", 3L), (waiting.Conflict!.Existing.Name, waiting.Conflict.Existing.Size));
        Assert.IsTrue(Board.Answer(started.Id, ConflictChoice.KeepBoth, forAll: false));
        var both = await FinishedAsync(started.Id);
        var skipped = await RunAsync(Up(FileJobKind.Copy, ["RFile1.txt"], "") with { Conflicts = ConflictChoice.Skip });
        var replaced = await RunAsync(
            Up(FileJobKind.Copy, ["RFile1.txt"], "") with
            {
                Conflicts = ConflictChoice.Replace,
            }
        );

        AssertDone(both, "RFile1 (2).txt");
        Assert.AreEqual(FileOperationStatus.Skipped, skipped.Results.Single().Status);
        AssertDone(replaced, "RFile1.txt");
        await AssertSameAsync("RFile1.txt");
        CollectionAssert.Contains(_account.Trash, "RFile1.txt", "Replaced into the provider's trash");
    }

    [TestMethod]
    public async Task ACancelledTransferLeavesNoPartialFile()
    {
        _account.WithFile("Docs/big.bin", new string('x', 1000));
        var start = Down(FileJobKind.Copy, ["Docs/big.bin"], "ChildDir");

        var started = Start(start with { Source = new StallingSource(start.Source) });
        await WaitUntilAsync(() => !_partials.IsEmpty && _partials.All(File.Exists));
        Assert.IsTrue(Board.Cancel(started.Id));
        var job = await FinishedAsync(started.Id);

        Assert.AreEqual(FileJobState.Cancelled, job.State);
        Assert.IsFalse(_partials.Any(File.Exists), "The partial file went");
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/big.bin")));
    }

    [TestMethod]
    public async Task AFailedPartIsSentAgainFromItsOffsetTwiceBeforeTheItemFails()
    {
        _provider.Capabilities = _provider.Capabilities with { MaxUploadPart = 4 };
        var callTries = TransferCalls.MaxRetries + 1;
        _account.FailNext("upload-append", RemoteErrorKind.Unavailable, callTries + 1);
        var sent = await RunAsync(Up(FileJobKind.Copy, ["RFile1.txt"], ""));
        _account.FailNext("upload-append", RemoteErrorKind.Unavailable, callTries * TransferStream.PartTries);
        var failed = await RunAsync(Up(FileJobKind.Copy, ["100%_done[1].txt"], ""));

        AssertDone(sent, "RFile1.txt");
        await AssertSameAsync("RFile1.txt");
        Assert.AreEqual(FileOperationStatus.Failed, failed.Results.Single().Status);
        Assert.IsFalse(_account.Exists("100%_done[1].txt"));
    }

    [TestMethod]
    public async Task TheProvidersOffsetIsWhereTheSourceIsReadAgain()
    {
        _provider.Capabilities = _provider.Capabilities with { MaxUploadPart = 4, ContentHash = ContentHashes.Sha256 };
        _account.WrongOffsetNext(2);

        var job = await RunAsync(Up(FileJobKind.Copy, ["RFile1.txt"], ""));

        AssertDone(job, "RFile1.txt");
        await AssertSameAsync("RFile1.txt");
        Assert.AreEqual(10, job.BytesDone);
    }

    [TestMethod]
    public async Task AnEndedSignInMarksTheConnectionAndFailsTheRest()
    {
        _account.FailNext("upload-begin", RemoteErrorKind.CredentialsEnded);

        var job = await RunAsync(Up(FileJobKind.Copy, ["RFile1.txt", "RFile2"], ""));

        CollectionAssert.AreEqual(new[] { Connection }, _marked);
        Assert.HasCount(2, job.Results);
        foreach (var result in job.Results)
            Assert.AreEqual((FileOperationStatus.Failed, TransferRun.SignInMessage), (result.Status, result.Message));
        Assert.AreEqual(1, _account.Calls.Count(c => c.StartsWith("upload-begin", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AFileLargerThanTheFreeSpaceIsRefused()
    {
        _account.TotalSpace = 100;

        var job = await RunAsync(Up(FileJobKind.Copy, ["RFile2"], ""));

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Failed, result.Status);
        StringAssert.Contains(result.Message, "not enough free space");
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("upload", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AHashMismatchKeepsAMovesSourceAndLeavesNothing()
    {
        _provider.Capabilities = _provider.Capabilities with { ContentHash = ContentHashes.Sha256 };
        _account.WithFile("Docs/a.txt", "content");
        _account.WrongHashes = true;

        var job = await RunAsync(Down(FileJobKind.Move, ["Docs/a.txt"], "ChildDir"));

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Failed, result.Status);
        StringAssert.Contains(result.Message, "was not kept");
        Assert.AreEqual("content", _account.TextOf("Docs/a.txt"));
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/a.txt")));
        Assert.IsEmpty(Directory.GetFiles(_library.FullPath("ChildDir"), "*" + FileJobs.PartialSuffix));
    }

    [TestMethod]
    public async Task AMoveTakesTheLibrarysSourceIntoTheTrashAndAConnectionsIntoItsOwn()
    {
        var up = await RunAsync(Up(FileJobKind.Move, ["RepeatedDir", "RFile1.txt"], ""));
        var down = await RunAsync(Down(FileJobKind.Move, ["RepeatedDir/ChildDir"], "Media"));

        AssertDone(up, "RepeatedDir");
        Assert.IsFalse(Directory.Exists(_library.FullPath("RepeatedDir")));
        Assert.IsFalse(File.Exists(_library.FullPath("RFile1.txt")));
        var trashed = (await _services.Get<Trash>().ListAsync(null, null, _ => true, Token)).Items;
        CollectionAssert.IsSubsetOf(
            new[] { "RepeatedDir", "RepeatedDir/RDFile1.txt", "RFile1.txt" },
            trashed.Select(i => i.Path).ToArray()
        );
        Assert.IsTrue(_account.Exists("RepeatedDir/RepeatedDir/RDFile2.txt"));
        AssertDone(down, "Media/ChildDir");
        Assert.IsTrue(File.Exists(_library.FullPath("Media/ChildDir/AFile2")));
        Assert.IsFalse(_account.Exists("RepeatedDir/ChildDir"));
        CollectionAssert.Contains(_account.Trash, "RepeatedDir/ChildDir");
    }

    [TestMethod]
    public async Task ARuleAddedWhileAJobRunsStopsItsNextItems()
    {
        _account.WithFile("Docs/a.txt", "a").WithFile("Docs/b.txt", "b");
        var access = new HidingAfterCall(_account, "read Docs/a.txt", "Docs");
        var start = Down(FileJobKind.Copy, ["Docs/a.txt", "Docs/b.txt"], "ChildDir") with { Source = Remote(access) };

        var job = await RunAsync(start);

        CollectionAssert.AreEqual(
            new[] { FileOperationStatus.Done, FileOperationStatus.NotFound },
            job.Results.Select(r => r.Status).ToArray()
        );
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/a.txt")));
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/b.txt")));
        Assert.IsFalse(_account.Calls.Any(c => c.EndsWith("Docs/b.txt", StringComparison.Ordinal)));
    }

    /// <summary>From the library into the connection.</summary>
    private TransferStart Up(FileJobKind kind, IReadOnlyList<string> paths, string target) =>
        Transfer(kind, paths, null, new TransferPlace(Connection, target));

    /// <summary>From the connection into the library.</summary>
    private TransferStart Down(FileJobKind kind, IReadOnlyList<string> paths, string target) =>
        Transfer(kind, paths, Connection, new TransferPlace(null, target));

    private TransferStart Transfer(FileJobKind kind, IReadOnlyList<string> paths, int? from, TransferPlace to)
    {
        var right = kind == FileJobKind.Move ? Rights.MoveFiles : Rights.CopyFiles;
        return new TransferStart
        {
            Kind = kind,
            FromConnection = from,
            Paths = paths,
            To = to,
            Source = from is null ? Library(right) : Remote(),
            Target = to.IsLibrary ? Library(right) : Remote(),
            Actor = Me,
            Access = new FakeAccess(),
            Partials = _partials,
            SignInEnded = id =>
            {
                lock (_marked)
                    _marked.Add(id);
                return Task.CompletedTask;
            },
        };
    }

    private LibraryTransferEndpoint Library(string right) =>
        _services.Get<LibraryTransfers>().Open(Me, new FakeAccess(), right, _partials);

    private RemoteTransferEndpoint Remote(IConnectionJobAccess? access = null) =>
        new(
            new RemoteTransferSide(
                Connection,
                _provider.Open("alice", "", new()),
                _provider.Capabilities,
                access ?? new FixedJobAccess(),
                Rights.CopyFiles
            ),
            new TransferCalls(new ConnectionGates(_clock), _clock),
            new RemoteListings(_clock)
        );

    private FileJobView Start(TransferStart start) => _services.Get<TransferJobs>().Start(start);

    private async Task<FileJobView> RunAsync(TransferStart start) => await FinishedAsync(Start(start).Id);

    private async Task<FileJobView> FinishedAsync(int id)
    {
        await Board.Completion(id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return Board.Find(id)!;
    }

    private async Task<FileJobView> WaitForAsync(int id, Func<FileJobView, bool> condition)
    {
        FileJobView? view = null;
        await WaitUntilAsync(() => Board.Find(id) is { } found && condition(view = found));
        return view!;
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException(_log.ToString());
            await Task.Delay(20, Token);
        }
    }

    /// <summary>The file at the connection has the library's bytes.</summary>
    private async Task AssertSameAsync(string path)
    {
        var expected = await File.ReadAllBytesAsync(_library.FullPath(path), Token);
        CollectionAssert.AreEqual(expected, _account.Locked(() => _account.Find(path)?.Content));
    }

    private static void AssertDone(FileJobView job, string placed)
    {
        Assert.AreEqual(FileJobState.Completed, job.State);
        foreach (var result in job.Results)
            Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual(placed, job.Results[0].NewPath);
    }

    /// <summary>Everything allowed; the work folder protected.</summary>
    /// <summary>
    /// Folder rules hiding <paramref name="hidden"/> once the provider was called with <paramref name="call"/>.
    /// </summary>
    private sealed class HidingAfterCall(FakeAccount account, string call, string hidden) : IConnectionJobAccess
    {
        public Task<ConnectionFolders?> FoldersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                account.Calls.Contains(call) ? FixedJobAccess.Hiding(hidden) : new FixedJobAccess().Folders
            );
    }

    internal sealed class FakeAccess : IFileJobAccess
    {
        public Task<bool> MayAsync(string path, string right) => Task.FromResult(true);

        public Task<bool> MayThroughoutAsync(string path, string right) => Task.FromResult(true);

        public bool IsProtected(string fullPath) =>
            fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);

        public bool IsReadOnly(string fullPath) => IsProtected(fullPath);
    }
}
