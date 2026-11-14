using System.Collections.Concurrent;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Files;
using Sfe.Core.Tasks;
using Sfe.Core.Transfers;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// What a transfer job leaves when it ends (<see cref="TransferJobs"/>): its failed items started again, its run in
/// the task history; and the space's largest file into a connection.
/// </summary>
[TestClass]
public sealed class TransferRetryTests
{
    private const int Connection = 7;
    private static readonly FileActor Me = new(7, "tester");

    private readonly FakeProvider _provider = new();
    private readonly InstantClock _clock = new();
    private readonly ConcurrentBag<string> _partials = [];
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private FakeAccount _account = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private TransferJobs Jobs => _services.Get<TransferJobs>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:Files:Uploads:MaxFileSizeMegabytes"] = "1");
        _account = _provider.Account("alice");
        _provider.Capabilities = _provider.Capabilities with { MaxUploadPart = 4 };
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ARetryWritesWhatFailedAndGoesIntoWhatWasWritten()
    {
        FailNextFile();
        var failed = await RunAsync(Up(["RepeatedDir", "RFile1.txt"]));
        var retry = Jobs.FindRetry(failed.Id, Me.AccountId)!;
        _account.Calls.Clear();

        var again = await FinishedAsync(Jobs.Retry(failed.Id, Up(retry.Paths))!.Id);

        Assert.AreEqual(
            (FileOperationStatus.Failed, FileOperationStatus.Done),
            (failed.Results[0].Status, failed.Results[1].Status)
        );
        CollectionAssert.AreEqual(new[] { "RepeatedDir" }, retry.Paths.ToArray());
        Assert.AreNotEqual(failed.Id, again.Id, "A new job");
        var result = again.Results.Single();
        Assert.AreEqual((FileOperationStatus.Done, "RepeatedDir"), (result.Status, result.NewPath), result.Message);
        Assert.AreEqual(1, _account.Calls.Count(c => c.StartsWith("upload-begin", StringComparison.Ordinal)));
        foreach (var file in Directory.GetFiles(_library.FullPath("RepeatedDir"), "*", SearchOption.AllDirectories))
            Assert.IsTrue(
                _account.Exists(Path.GetRelativePath(_library.Root, file).Replace(Path.DirectorySeparatorChar, '/')),
                file
            );
        Assert.IsNull(Jobs.Retry(failed.Id, Up(retry.Paths)), "Started again once");
        Assert.IsNull(Jobs.FindRetry(again.Id, Me.AccountId), "Nothing left");
    }

    [TestMethod]
    public async Task AnotherAccountFindsNothingToRetry()
    {
        FailNextFile();
        var failed = await RunAsync(Up(["RFile1.txt"]));

        Assert.IsNotNull(Jobs.FindRetry(failed.Id, Me.AccountId));
        Assert.IsNull(Jobs.FindRetry(failed.Id, 8));
        Assert.IsNull(Jobs.Find(failed.Id, 8));
        Assert.IsNull(Jobs.FindRetry(failed.Id, null));
    }

    [TestMethod]
    public async Task EachJobIsARunOfTheTaskHistory()
    {
        FailNextFile();
        await RunAsync(Up(["RFile1.txt", "100%_done[1].txt"]) with { ToName = "connection 7 “Box” (fake, Account)" });

        var history = _services.Get<TaskHistory>();
        var run = (await history.ListAsync(BackgroundTaskKind.Transfer, null, 10, Token)).Runs.Single();
        var details = (await history.GetAsync(run.Id, Token))!;

        Assert.AreEqual(TaskOutcome.Completed, run.Outcome);
        Assert.AreEqual(
            "tester copied 2 items from the library to connection 7 “Box” (fake, Account): 1 done, 1 failed.",
            run.Summary
        );
        Assert.IsTrue(details.Log.Any(l => l.Message.Contains("RFile1.txt: Failed")), "The failed item's line");
        Assert.IsTrue(details.Log.Any(l => l.Message.Contains("copied 100%_done[1].txt")), "The done item's line");
    }

    [TestMethod]
    public async Task AFileLargerThanTheSpaceAllowsIsRefusedBeforeItIsRead()
    {
        await File.WriteAllBytesAsync(_library.FullPath("ChildDir/big.bin"), new byte[1024 * 1024 + 1], Token);

        var job = await RunAsync(Up(["ChildDir", "RFile1.txt"]));

        var folder = job.Results[0];
        Assert.AreEqual(FileOperationStatus.Failed, folder.Status);
        Assert.AreEqual("“big.bin” is larger than the 1 MB a file may have here.", folder.Message);
        Assert.AreEqual(FileOperationStatus.Done, job.Results[1].Status);
        Assert.IsTrue(_account.Exists("ChildDir/AFile2"), "The others go");
        Assert.IsFalse(_account.Exists("ChildDir/big.bin"));
        Assert.AreEqual(3 * 10, job.BytesTotal, "Not counted, not read");
    }

    [TestMethod]
    public async Task ARetriedMoveNeverTakesAFileOfTheSameSizeForItsOwn()
    {
        Directory.CreateDirectory(_library.FullPath("Moving"));
        await File.WriteAllTextAsync(_library.FullPath("Moving/Keep.txt"), "mine", Token);
        _account.WithFile("Keep.txt", "else");

        // The library gives no content hash: the same size proves nothing, so the file is a conflict (skipped here).
        var job = await RunAsync(
            Up(["Moving/Keep.txt"]) with
            {
                Kind = FileJobKind.Move,
                Retry = true,
                Conflicts = ConflictChoice.Skip,
            }
        );

        Assert.AreEqual(FileOperationStatus.Skipped, job.Results.Single().Status, job.Results.Single().Message);
        Assert.IsTrue(File.Exists(_library.FullPath("Moving/Keep.txt")), "The source of the move stays");
        Assert.AreEqual("else", _account.TextOf("Keep.txt"));
    }

    /// <summary>The next file's parts fail every time they are sent: that file fails.</summary>
    private void FailNextFile() =>
        _account.FailNext(
            "upload-append",
            RemoteErrorKind.Unavailable,
            (TransferCalls.MaxRetries + 1) * TransferStream.PartTries
        );

    /// <summary>A copy from the library into the connection's root.</summary>
    private TransferStart Up(IReadOnlyList<string> paths) =>
        new()
        {
            Kind = FileJobKind.Copy,
            Paths = paths,
            To = new TransferPlace(Connection, ""),
            Source = _services
                .Get<LibraryTransfers>()
                .Open(Me, new TransferJobsTests.FakeAccess(), Rights.CopyFiles, _partials),
            Target = new RemoteTransferEndpoint(
                Connection,
                _provider.Open("alice", "", new()),
                _provider.Capabilities,
                new TransferCalls(new ConnectionGates(_clock), _clock),
                new RemoteListings(_clock)
            ),
            Actor = Me,
            Access = new TransferJobsTests.FakeAccess(),
            Partials = _partials,
        };

    private async Task<FileJobView> RunAsync(TransferStart start) => await FinishedAsync(Jobs.Start(start).Id);

    private async Task<FileJobView> FinishedAsync(int id)
    {
        var board = _services.Get<JobBoard>();
        await board.Completion(id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return board.Find(id)!;
    }
}
