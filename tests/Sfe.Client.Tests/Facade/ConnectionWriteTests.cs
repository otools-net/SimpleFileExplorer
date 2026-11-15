using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Client.Tests.Infrastructure;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// A connection's files changed through the facade on the real server (the <c>FakeProvider</c>): uploads in the
/// provider's parts, new folders, renames, deletes, moves and copies inside it.
/// </summary>
[TestClass]
public sealed class ConnectionWriteTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AFileIsUploadedInPartsTheProviderTakes()
    {
        using var world = await ConnectedServer.StartAsync(Token, capabilities: capabilities =>
            capabilities with
            {
                MaxUploadPart = 64 * 1024,
            }
        );
        var bytes = Enumerable.Range(0, 200 * 1024).Select(i => (byte)(i % 251)).ToArray();
        using var content = new MemoryStream(bytes);
        var reported = new List<long>();

        var item = await world.Connection.Files.UploadAsync(
            "Photos/Trips/data.bin",
            content,
            new SfeUploadOptions { Progress = new SyncProgress<long>(reported) },
            Token
        );

        Assert.AreEqual(("Photos/Trips/data.bin", (long?)bytes.Length), (item!.Path, item.Size));
        Assert.AreEqual(
            4,
            world.Account.Calls.Count(call => call.StartsWith("upload-append", StringComparison.Ordinal))
        );
        Assert.AreEqual((long)bytes.Length, reported[^1]);
        using var read = new MemoryStream();
        await world.Connection.Files.DownloadAsync("Photos/Trips/data.bin", read, cancellationToken: Token);
        CollectionAssert.AreEqual(bytes, read.ToArray());
    }

    [TestMethod]
    public async Task ATakenNameIsRefusedBeforeSendingOrSkipped()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var files = world.Connection.Files;
        using var content = new MemoryStream([1, 2, 3]);

        var refused = await Assert.ThrowsExactlyAsync<SfeConflictException>(() =>
            files.UploadAsync("Photos/b.jpg", content, cancellationToken: Token)
        );
        content.Position = 0;
        var skipped = await files.UploadAsync(
            "Photos/b.jpg",
            content,
            new SfeUploadOptions { Conflicts = SfeConflictChoice.Skip },
            Token
        );

        Assert.AreEqual("file-exists", refused.ProblemType);
        Assert.IsNull(skipped);
        Assert.AreEqual("picture b", world.Account.TextOf("Photos/b.jpg"));
    }

    [TestMethod]
    public async Task AFolderOfThisComputerIsUploadedWithItsFiles()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var local = Path.Combine(world.Server.Library.BasePath, "Local");
        Directory.CreateDirectory(Path.Combine(local, "Inner"));
        await File.WriteAllTextAsync(Path.Combine(local, "one.txt"), "one", Token);
        await File.WriteAllTextAsync(Path.Combine(local, "Inner", "two.txt"), "two", Token);

        var made = await world.Connection.Files.UploadFolderAsync(local, "Photos", cancellationToken: Token);

        CollectionAssert.AreEquivalent(
            new[] { "Photos/Local/one.txt", "Photos/Local/Inner/two.txt" },
            made.Select(item => item.Path).ToArray()
        );
        Assert.AreEqual("two", world.Account.TextOf("Photos/Local/Inner/two.txt"));
    }

    [TestMethod]
    public async Task AFolderIsMadeAnItemRenamedAndOthersDeletedToTheProvidersTrash()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var files = world.Connection.Files;

        var folder = await files.CreateFolderAsync("Photos/Made", Token);
        var taken = await Assert.ThrowsExactlyAsync<SfeConflictException>(() =>
            files.CreateFolderAsync("Photos/Made", Token)
        );
        var renamed = await files.RenameAsync("Photos/b.jpg", "c.jpg", Token);
        var deleted = await files.DeleteAsync(["Photos/a.mp4", "Photos/Nowhere"], Token);

        Assert.AreEqual(("Photos/Made", true), (folder.Path, folder.IsFolder));
        Assert.AreEqual("file-exists", taken.ProblemType);
        Assert.AreEqual("Photos/c.jpg", renamed.Path);
        Assert.AreEqual(SfeItemStatus.Done, deleted[0].Status);
        Assert.AreEqual((SfeItemStatus.NotFound, (int?)null), (deleted[1].Status, deleted[1].TrashId));
        CollectionAssert.Contains(world.Account.Trash, "Photos/a.mp4");
        Assert.IsFalse(world.Account.Exists("Photos/b.jpg"));
    }

    [TestMethod]
    public async Task ItemsAreMovedAndCopiedInsideWithAResultEach()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        world.Account.WithFile("Photos/Trips/b.jpg", "older b");
        var files = world.Connection.Files;
        var keepBoth = new SfeConnectionTransferOptions { Conflicts = SfeConflictChoice.KeepBoth };

        var copied = await files.CopyAsync(["Photos/b.jpg"], "Photos/Trips", keepBoth, Token);
        var left = await files.CopyAsync(["Photos/b.jpg"], "Photos/Trips", cancellationToken: Token);
        var moved = await files.MoveAsync(["Photos/a.mp4", "Photos/Trips"], "Photos/Trips", cancellationToken: Token);

        Assert.AreEqual((SfeItemStatus.Done, "Photos/Trips/b (1).jpg"), (copied[0].Status, copied[0].Item!.Path));
        Assert.AreEqual(SfeItemStatus.Exists, left.Single().Status);
        Assert.AreEqual(("Photos/a.mp4", SfeItemStatus.Done), (moved[0].Path, moved[0].Status));
        Assert.AreEqual("Photos/Trips/a.mp4", moved[0].Item!.Path);
        Assert.AreEqual(SfeItemStatus.Invalid, moved[1].Status, "A folder into itself");
        Assert.AreEqual("older b", world.Account.TextOf("Photos/Trips/b.jpg"));
        Assert.IsTrue(world.Account.Exists("Photos/Trips/a.mp4"));
    }

    [TestMethod]
    public async Task AReadOnlyConnectionRefusesChanges()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var connections = world.Server.Factory.Services.GetRequiredService<ConnectionService>();
        await connections.UpdateAsync(world.Id, new ConnectionChange(ReadOnly: true), world.Server.AdminId, Token);

        var refused = await Assert.ThrowsExactlyAsync<SfeRefusedException>(() =>
            world.Connection.Files.CreateFolderAsync("Photos/Made", Token)
        );
        var listed = (await world.Client.Connections.ListAsync(Token)).Single();

        Assert.AreEqual(403, refused.StatusCode);
        Assert.AreEqual(SfeConnectionAccess.Read, listed.Access);
    }
}
