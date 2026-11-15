using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Client.Tests.Infrastructure;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Raw = Otools.Net.Sfe.Client.Generated.Models;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// Moves and copies between the library and a connection, or two connections, as <see cref="SfeJob"/>s on the real
/// server (the <c>FakeProvider</c>), retried after they ended; a library connection's folder rules through
/// <see cref="SfeClient.Raw"/>.
/// </summary>
[TestClass]
public sealed class ConnectionTransferTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task FilesAreCopiedIntoTheConnectionAndMovedBackToTheLibrary()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var connection = world.Connection;

        var up = await connection.CopyFromLibraryAsync(
            ["RFile1.txt", "ChildDir"],
            "Photos/Trips",
            cancellationToken: Token
        );
        var copied = await up.WaitAsync(cancellationToken: Token);
        var down = await connection.MoveToLibraryAsync(["Photos/b.jpg"], "ChildDir", cancellationToken: Token);
        var moved = await down.WaitAsync(cancellationToken: Token);

        Assert.AreEqual(
            (SfeJobKind.Copy, (int?)null, (int?)world.Id),
            (copied.Kind, up.Status.FromConnection, copied.ToConnection)
        );
        Assert.IsTrue(copied.Results.All(result => result.Status == SfeItemStatus.Done));
        Assert.IsTrue(world.Account.Exists("Photos/Trips/ChildDir/AFile1.txt"));
        Assert.IsTrue(File.Exists(world.Server.Library.FullPath("RFile1.txt")), "A copy leaves its source");
        Assert.AreEqual((SfeJobState.Completed, (int?)world.Id), (moved.State, moved.FromConnection));
        Assert.AreEqual(
            "picture b",
            await File.ReadAllTextAsync(world.Server.Library.FullPath("ChildDir/b.jpg"), Token)
        );
        Assert.IsFalse(world.Account.Exists("Photos/b.jpg"));
    }

    [TestMethod]
    public async Task FilesGoFromTheLibraryAndBetweenTwoConnections()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        world.Provider.Account("other");
        var other = world.Client.Connections[await world.Server.ConnectAsync("other", ConnectionLevel.Account, Token)];

        var moved = await world.Connection.MoveFromLibraryAsync(["RFile1.txt"], "", cancellationToken: Token);
        await moved.WaitAsync(cancellationToken: Token);
        var copied = await world.Connection.CopyToAsync(["Photos/a.mp4"], other, "", cancellationToken: Token);
        await copied.WaitAsync(cancellationToken: Token);
        var back = await other.MoveToAsync(["a.mp4"], world.Connection, "", cancellationToken: Token);
        var done = await back.WaitAsync(cancellationToken: Token);

        Assert.IsTrue(world.Account.Exists("RFile1.txt"));
        Assert.IsFalse(File.Exists(world.Server.Library.FullPath("RFile1.txt")), "Moved to the library's trash");
        Assert.AreEqual(SfeItemStatus.Done, done.Results.Single().Status);
        Assert.AreEqual("0123456789", world.Account.TextOf("a.mp4"));
        Assert.IsFalse(world.Provider.Account("other").Exists("a.mp4"));
    }

    [TestMethod]
    public async Task AnEndedTransferIsRetriedAsANewJob()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        world.Account.WithFile("RFile1.txt", "older");
        var job = await world.Connection.CopyFromLibraryAsync(["RFile1.txt", "ChildDir"], "", cancellationToken: Token);

        var clash = await Assert.ThrowsExactlyAsync<SfeJobConflictException>(() =>
            job.WaitAsync(cancellationToken: Token)
        );
        var running = await Assert.ThrowsExactlyAsync<SfeConflictException>(() => job.RetryAsync(Token));
        await job.CancelAsync(Token);
        var cancelled = await job.WaitAsync(cancellationToken: Token);
        var again = await job.RetryAsync(Token);
        var options = new SfeJobWaitOptions
        {
            OnConflict = (_, _) => Task.FromResult(new SfeConflictAnswer(SfeConflictChoice.Skip)),
        };
        var done = await again.WaitAsync(options, Token);
        var nothing = await Assert.ThrowsExactlyAsync<SfeConflictException>(() => job.RetryAsync(Token));

        Assert.AreEqual("RFile1.txt", clash.Conflict.Existing.Name);
        Assert.AreEqual("job-running", running.ProblemType);
        Assert.AreEqual(SfeJobState.Cancelled, cancelled.State);
        Assert.AreNotEqual(job.Id, again.Id);
        Assert.AreEqual((int?)world.Id, done.ToConnection);
        Assert.AreEqual(SfeItemStatus.Done, done.Results.Single(result => result.Path == "ChildDir").Status);
        Assert.IsTrue(world.Account.Exists("ChildDir/AFile2"));
        Assert.AreEqual("older", world.Account.TextOf("RFile1.txt"));
        Assert.AreEqual("nothing-to-retry", nothing.ProblemType);
    }

    [TestMethod]
    public async Task ALibraryConnectionsFolderRulesAreSetAndReadThroughRaw()
    {
        using var world = await ConnectedServer.StartAsync(Token, ConnectionLevel.Library);
        var raw = world.Client.Raw.Api.V1.Connections[world.Id];
        var accounts = world.Server.Factory.Services.GetRequiredService<AccessService>();
        var tenant = await world.Server.TenantAsync(Token);
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!.Id;
        var rule = new Raw.ConnectionRuleInput
        {
            GroupId = users,
            Path = "Photos/Trips",
            Level = Raw.FolderLevel.Read,
        };

        var set = await raw.FolderRules.PutAsync(
            new Raw.ConnectionFolderRulesInput { Rules = [rule] },
            cancellationToken: Token
        );
        var read = await raw.FolderRules.GetAsync(cancellationToken: Token);
        using var folders = await raw.Folders.GetAsync(query => query.QueryParameters.Path = "Photos", Token);
        using var json = await JsonDocument.ParseAsync(folders!, cancellationToken: Token);

        Assert.AreEqual("Photos/Trips", set!.Single().Path);
        var kept = read!.Single();
        Assert.AreEqual(((int?)users, (Raw.FolderLevel?)Raw.FolderLevel.Read), (kept.GroupId, kept.Level));
        StringAssert.Contains(json.RootElement.GetRawText(), "Trips");
    }
}
