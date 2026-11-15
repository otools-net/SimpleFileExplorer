using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>Moves and copies on the real server, followed as <see cref="SfeJob"/>s: results, clashes, cancel.</summary>
[TestClass]
public sealed class JobTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AMoveIsFollowedToItsEndWithProgress()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var seen = new List<SfeJobStatus>();

        var job = await sfe.Files.MoveAsync(["RFile1.txt", "ChildDir"], "Media", cancellationToken: Token);
        var done = await job.WaitAsync(
            new SfeJobWaitOptions { Progress = new SyncProgress<SfeJobStatus>(seen) },
            Token
        );

        Assert.AreEqual(SfeJobKind.Move, done.Kind);
        Assert.AreEqual(SfeJobState.Completed, done.State);
        Assert.IsTrue(done.Results.All(result => result.Status == SfeItemStatus.Done));
        Assert.AreEqual("Media/RFile1.txt", done.Results.Single(result => result.Path == "RFile1.txt").NewPath);
        Assert.IsTrue(File.Exists(server.Library.FullPath("Media/ChildDir/AFile1.txt")));
        Assert.AreSame(done, seen[^1]);
        Assert.AreSame(done, job.Status);
    }

    [TestMethod]
    public async Task AClashIsAnsweredByTheCallback()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var asked = new List<SfeJobConflict>();
        var options = new SfeJobWaitOptions
        {
            OnConflict = (conflict, _) =>
            {
                asked.Add(conflict);
                return Task.FromResult(new SfeConflictAnswer(SfeConflictChoice.KeepBoth));
            },
        };

        var job = await sfe.Files.CopyAsync(["ChildDir/AFile1.txt"], "RepeatedDir/ChildDir", cancellationToken: Token);
        var done = await job.WaitAsync(options, Token);

        Assert.HasCount(1, asked);
        Assert.AreEqual("AFile1.txt", asked[0].Existing.Name);
        Assert.AreEqual("RepeatedDir/ChildDir/AFile1 (2).txt", done.Results.Single().NewPath);
        Assert.IsTrue(File.Exists(server.Library.FullPath("ChildDir/AFile1.txt")));
    }

    [TestMethod]
    public async Task AClashIsAnsweredByTheChoiceGivenAtTheStart()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeJobOptions { Conflicts = SfeConflictChoice.Skip, KeepTags = true };

        var job = await sfe.Files.CopyAsync(["ChildDir/AFile1.txt"], "RepeatedDir/ChildDir", options, Token);
        var done = await job.WaitAsync(cancellationToken: Token);

        Assert.AreEqual(SfeItemStatus.Skipped, done.Results.Single().Status);
    }

    [TestMethod]
    public async Task AClashWithoutAnswerFailsTheWaitAndTheJobKeepsWaiting()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var job = await sfe.Files.MoveAsync(["ChildDir/AFile1.txt"], "RepeatedDir/ChildDir", cancellationToken: Token);

        var unanswered = await Assert.ThrowsExactlyAsync<SfeJobConflictException>(() =>
            job.WaitAsync(cancellationToken: Token)
        );
        await unanswered.Job.AnswerAsync(new SfeConflictAnswer(SfeConflictChoice.Replace), Token);
        var done = await job.WaitAsync(cancellationToken: Token);

        Assert.AreEqual("AFile1.txt", unanswered.Conflict.Source.Name);
        Assert.AreEqual(SfeItemStatus.Done, done.Results.Single().Status);
        Assert.IsFalse(File.Exists(server.Library.FullPath("ChildDir/AFile1.txt")));
    }

    [TestMethod]
    public async Task AJobIsCancelledAndAskedWhereItStands()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var job = await sfe.Files.CopyAsync(["ChildDir/AFile1.txt"], "RepeatedDir/ChildDir", cancellationToken: Token);
        await Assert.ThrowsExactlyAsync<SfeJobConflictException>(() => job.WaitAsync(cancellationToken: Token));

        await job.CancelAsync(Token);
        var status = await job.RefreshAsync(Token);

        Assert.AreEqual(SfeJobState.Cancelled, status.State);
        Assert.IsTrue(status.IsFinished);
    }
}
