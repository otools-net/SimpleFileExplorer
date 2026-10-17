using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Moves and copies as background jobs, in Core (<see cref="FileJobs"/>).</summary>
[TestClass]
public sealed class FileJobsTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly WarningLog _log = new();

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileJobs Jobs => _services.Get<FileJobs>();
    private TagService Tags => _services.Get<TagService>();

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
    public async Task AMovedFileKeepsItsTagsAndIndexRow()
    {
        await ScanAsync();
        var tag = await TagAsync("Beach", "Media/photo.jpg");

        var job = await RunAsync(new FileJobRequest(FileJobKind.Move, ["Media/photo.jpg"], "ChildDir"));

        Assert.AreEqual(FileJobState.Completed, job.State);
        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("ChildDir/photo.jpg", result.NewPath);
        Assert.IsFalse(File.Exists(_library.FullPath("Media/photo.jpg")));
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/photo.jpg")));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("ChildDir/photo.jpg"));
    }

    [TestMethod]
    public async Task AMovedFolderTakesTheTagsBelowIt()
    {
        await ScanAsync();
        var tag = await TagAsync("Deep", "RepeatedDir/ChildDir/AFile1.txt");

        var job = await RunAsync(new FileJobRequest(FileJobKind.Move, ["RepeatedDir"], "Media"));

        Assert.AreEqual(FileOperationStatus.Done, job.Results.Single().Status, job.Results.Single().Message);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/RepeatedDir/ChildDir/AFile1.txt")));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Media/RepeatedDir/ChildDir/AFile1.txt"));
    }

    [TestMethod]
    public async Task ACopyGetsTheTagsOfItsOriginalOnlyWhenAsked()
    {
        await ScanAsync();
        var tag = await TagAsync("Beach", "Media/photo.jpg");

        var tagged = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["Media/photo.jpg"], "ChildDir", KeepTags: true));
        var plain = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["Media/photo.jpg"], "RepeatedDir"));

        Assert.AreEqual(FileOperationStatus.Done, tagged.Results.Single().Status, tagged.Results.Single().Message);
        Assert.AreEqual(3 * 1024, tagged.BytesDone);
        Assert.AreEqual(tagged.BytesTotal, tagged.BytesDone);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")), "The original stays");
        Assert.AreEqual(File.GetLastWriteTimeUtc(_library.FullPath("Media/photo.jpg")), File.GetLastWriteTimeUtc(_library.FullPath("ChildDir/photo.jpg")));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("ChildDir/photo.jpg"));
        CollectionAssert.AreEqual(new[] { tag }, await TagIdsAsync("Media/photo.jpg"));
        Assert.IsEmpty(await TagIdsAsync("RepeatedDir/photo.jpg"));
        Assert.AreEqual(FileOperationStatus.Done, plain.Results.Single().Status);
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("RepeatedDir/photo.jpg"), Token), "A copied file is indexed at once");
    }

    [TestMethod]
    public async Task ACopiedFolderLeavesOutWhatThePersonMayNotSeeAndIsScanned()
    {
        await ScanAsync();
        var access = new FakeAccess { Refuse = (path, right) => right == Rights.ViewFiles && path == "RepeatedDir/ChildDir" };

        var job = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RepeatedDir"], "Media"), access);
        await _services.Get<ScanCoordinator>().Completion!;

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        StringAssert.Contains(result.Message, "RepeatedDir/ChildDir");
        Assert.IsTrue(File.Exists(_library.FullPath("Media/RepeatedDir/RepeatedDir/RDFile2.txt")));
        Assert.IsFalse(Directory.Exists(_library.FullPath("Media/RepeatedDir/ChildDir")));
        Assert.IsTrue(File.Exists(_library.FullPath("RepeatedDir/ChildDir/AFile1.txt")));
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/RepeatedDir/RDFile1.txt"), Token), "A scan of the copy indexes it");
    }

    [TestMethod]
    public async Task ANameClashWaitsForTheAnswerWhichCanHoldForTheRest()
    {
        _library.AddFile("ChildDir/RFile1.txt", 5);
        _library.AddFile("ChildDir/RFile2", 5);

        var view = Jobs.Start(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt", "RFile2"], "ChildDir"), Me, new FakeAccess());
        var waiting = await WaitForAsync(view.Id, j => j.State == FileJobState.Waiting);

        Assert.AreEqual("RFile1.txt", waiting.Conflict!.Source.Name);
        Assert.AreEqual(10, waiting.Conflict.Source.Size);
        Assert.AreEqual(5, waiting.Conflict.Existing.Size);
        Assert.IsTrue(waiting.Conflict.CanReplace);
        Assert.IsFalse(Jobs.Answer(view.Id, ConflictChoice.Ask, forAll: false));
        Assert.IsTrue(Jobs.Answer(view.Id, ConflictChoice.KeepBoth, forAll: true));
        await Jobs.Completion(view.Id)!;

        var done = Jobs.Find(view.Id)!;
        Assert.AreEqual(FileJobState.Completed, done.State);
        CollectionAssert.AreEqual(new[] { "ChildDir/RFile1 (2).txt", "ChildDir/RFile2 (2)" }, done.Results.Select(r => r.NewPath).ToArray());
        Assert.IsFalse(Jobs.Answer(view.Id, ConflictChoice.Skip, forAll: false), "Nothing is asked any more");
    }

    [TestMethod]
    public async Task ReplacingSendsWhatWasThereToTheTrashAndNeedsTheRightToDelete()
    {
        _library.AddFile("ChildDir/RFile1.txt", 5);
        _library.AddFile("ChildDir/RFile2", 5);

        var replaced = await RunAsync(new FileJobRequest(FileJobKind.Move, ["RFile1.txt"], "ChildDir", ConflictChoice.Replace));
        // Without the right to delete there, "replace" can't hold: the job asks instead.
        var started = Jobs.Start(new FileJobRequest(FileJobKind.Move, ["RFile2"], "ChildDir", ConflictChoice.Replace), Me,
            new FakeAccess { Refuse = (_, right) => right == Rights.DeleteFiles });
        Assert.IsFalse((await WaitForAsync(started.Id, j => j.State == FileJobState.Waiting)).Conflict!.CanReplace);
        Jobs.Answer(started.Id, ConflictChoice.Replace, forAll: false);
        await Jobs.Completion(started.Id)!;
        var refused = Jobs.Find(started.Id)!;

        Assert.AreEqual(FileOperationStatus.Done, replaced.Results.Single().Status, replaced.Results.Single().Message);
        Assert.AreEqual(10, new FileInfo(_library.FullPath("ChildDir/RFile1.txt")).Length);
        Assert.AreEqual("ChildDir/RFile1.txt", (await _services.Get<Trash>().ListAsync(null, null, _ => true, Token)).Items.Single().Path);
        Assert.AreEqual(FileOperationStatus.Forbidden, refused.Results.Single().Status);
        Assert.IsTrue(File.Exists(_library.FullPath("RFile2")));
        Assert.AreEqual(5, new FileInfo(_library.FullPath("ChildDir/RFile2")).Length);
    }

    [TestMethod]
    public async Task ACopyInItsOwnFolderGetsAFreeNameAndSkipLeavesItOut()
    {
        _library.AddFile("ChildDir/RFile1.txt", 5);

        var copy = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["Media/photo.jpg"], "Media"));
        var skipped = await RunAsync(new FileJobRequest(FileJobKind.Move, ["RFile1.txt"], "ChildDir", ConflictChoice.Skip));

        Assert.AreEqual("Media/photo (2).jpg", copy.Results.Single().NewPath);
        Assert.AreEqual(FileOperationStatus.Skipped, skipped.Results.Single().Status);
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
    }

    [TestMethod]
    public async Task CancellingAWaitingJobStopsItWithWhatIsDone()
    {
        _library.AddFile("ChildDir/RFile2", 5);

        var view = Jobs.Start(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt", "RFile2", "100%_done[1].txt"], "ChildDir"), Me, new FakeAccess());
        await WaitForAsync(view.Id, j => j.State == FileJobState.Waiting);
        Assert.IsTrue(Jobs.Cancel(view.Id));
        await Jobs.Completion(view.Id)!;

        var done = Jobs.Find(view.Id)!;
        Assert.AreEqual(FileJobState.Cancelled, done.State);
        Assert.AreEqual(1, done.Done);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/RFile1.txt")));
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/100%_done[1].txt")));
        Assert.IsFalse(Jobs.Cancel(view.Id));
    }

    [TestMethod]
    public async Task WhatThePersonMayNotDoIsRefusedItemByItem()
    {
        var access = new FakeAccess { Refuse = (path, right) => right == Rights.MoveFiles && path == "RepeatedDir/ChildDir" };

        var job = await RunAsync(new FileJobRequest(FileJobKind.Move, ["RepeatedDir", "RFile1.txt", "ChildDir", "Nowhere.txt"], ""), access);

        Assert.AreEqual(FileOperationStatus.Forbidden, job.Results[0].Status, "Something below the folder may not move");
        Assert.AreEqual(FileOperationStatus.Skipped, job.Results[1].Status, "Already there");
        Assert.AreEqual(FileOperationStatus.Skipped, job.Results[2].Status);
        Assert.AreEqual(FileOperationStatus.NotFound, job.Results[3].Status);

        var into = await RunAsync(new FileJobRequest(FileJobKind.Move, ["RepeatedDir"], "RepeatedDir/ChildDir"));
        Assert.AreEqual(FileOperationStatus.Failed, into.Results.Single().Status);
        StringAssert.Contains(into.Results.Single().Message, "into itself");

        var hidden = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "Media"), new FakeAccess { Refuse = (_, right) => right == Rights.Browse });
        Assert.AreEqual(FileOperationStatus.NotFound, hidden.Results.Single().Status, "Nothing tells what one may not see");

        var closed = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["ChildDir"], "Media"), new FakeAccess { Refuse = (path, right) => right == Rights.ViewFiles && path == "ChildDir" });
        Assert.AreEqual(FileOperationStatus.Forbidden, closed.Results.Single().Status, "A folder whose files may not be opened");

        var target = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "Media"), new FakeAccess { Refuse = (path, _) => path == "Media" });
        Assert.AreEqual(FileOperationStatus.Forbidden, target.Results.Single().Status);
        Assert.IsTrue(File.Exists(_library.FullPath("RepeatedDir/ChildDir/AFile1.txt")));
    }

    [TestMethod]
    public async Task NothingGoesIntoTheWorkFolderNorAMissingOne()
    {
        var work = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], ".sfe"));
        var missing = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "Nowhere"));

        Assert.AreEqual(FileOperationStatus.NotFound, work.Results.Single().Status);
        Assert.AreEqual(FileOperationStatus.NotFound, missing.Results.Single().Status);
    }

    [TestMethod]
    public async Task ACopyKeepsTheFreeSpaceItIsToldTo()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:Files:MinimumFreeSpaceMegabytes"] = int.MaxValue.ToString(), _log.AddTo);

        var job = await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "Media"));

        Assert.AreEqual(FileOperationStatus.Failed, job.Results.Single().Status);
        StringAssert.Contains(job.Results.Single().Message, "MB free");
        Assert.IsFalse(File.Exists(_library.FullPath("Media/RFile1.txt")));
    }

    [TestMethod]
    public async Task JobsAreListedForTheirOwner()
    {
        await RunAsync(new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "Media"));
        var other = Jobs.Start(new FileJobRequest(FileJobKind.Copy, ["RFile2"], "Media"), new FileActor(8, "other"), new FakeAccess());
        await Jobs.Completion(other.Id)!;

        Assert.HasCount(1, Jobs.List(7));
        Assert.HasCount(2, Jobs.List(null));
        Assert.AreEqual(other.Id, Jobs.List(null)[0].Id, "Newest first");
    }

    private async Task<FileJobView> RunAsync(FileJobRequest request, IFileJobAccess? access = null)
    {
        var view = Jobs.Start(request, Me, access ?? new FakeAccess());
        await Jobs.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return Jobs.Find(view.Id)!;
    }

    private async Task<FileJobView> WaitForAsync(int id, Func<FileJobView, bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            if (Jobs.Find(id) is { } view && condition(view))
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

    private async Task<int> TagAsync(string name, params string[] paths)
    {
        var tag = (await Tags.CreateAsync(new TagInput(name), Token)).Tag!.Id;
        var (result, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: [tag]), Token);
        Assert.IsNotNull(result, errors is null ? null : string.Join(" ", errors.SelectMany(e => e.Value)));
        return tag;
    }

    private async Task<int[]> TagIdsAsync(string path) =>
        (await Tags.GetFileTagsAsync(path, Token))?.Tags.Select(t => t.Id).ToArray() ?? [];

    private Task<SfeDbContext> DbAsync() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    /// <summary>Everything allowed but what <see cref="Refuse"/> says.</summary>
    private sealed class FakeAccess : IFileJobAccess
    {
        public Func<string, string, bool> Refuse { get; init; } = (_, _) => false;

        public Task<bool> MayAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public Task<bool> MayThroughoutAsync(string path, string right) =>
            Task.FromResult(!Refuse(path, right) && !Probe.Any(p => p.StartsWith(path + "/", StringComparison.Ordinal) && Refuse(p, right)));

        public bool IsProtected(string fullPath) => fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);

        /// <summary>Paths below a folder the fake knows of (the test library's folders).</summary>
        private static readonly string[] Probe = ["RepeatedDir/ChildDir", "RepeatedDir/RepeatedDir", "ChildDir", "Media"];
    }
}
