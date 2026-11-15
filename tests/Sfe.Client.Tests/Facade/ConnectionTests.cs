using System.IO.Compression;
using Otools.Net.Sfe.Client.Tests.Infrastructure;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// Connections on the real server, with the <c>FakeProvider</c> of the server's tests: listed, and a connection's
/// files listed, read whole and in part, and zipped.
/// </summary>
[TestClass]
public sealed class ConnectionTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task TheUsableConnectionsAreListedWithTheirProvidersCapabilities()
    {
        using var world = await ConnectedServer.StartAsync(Token);

        var listed = await world.Client.Connections.ListAsync(Token);
        var info = await world.Connection.GetInfoAsync(Token);
        var capabilities = await world.Connection.GetCapabilitiesAsync(Token);

        var connection = listed.Single();
        Assert.AreEqual((world.Id, "fake", "Files of acct"), (connection.Id, connection.Kind, connection.Name));
        Assert.AreEqual(SfeConnectionLevel.Account, connection.Level);
        Assert.AreEqual((SfeConnectionState.Ready, SfeConnectionAccess.Write), (connection.State, connection.Access));
        Assert.IsTrue(connection.Capabilities!.RangedReads);
        Assert.AreEqual(4L * 1024 * 1024, connection.Capabilities.MaxUploadPart);
        Assert.AreEqual(("fake", true), (capabilities.ContentHash, capabilities.OwnTrash));
        Assert.AreEqual(world.Id, info.Id);
        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(() =>
            world.Client.Connections[world.Id + 100].GetInfoAsync(Token)
        );
    }

    [TestMethod]
    public async Task AFolderIsListedPageByPageAndAnItemRead()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        world.Account.WithFile("Photos/c.txt", "c").WithFile("Photos/d.txt", "d");
        var files = world.Connection.Files;

        var items = new List<SfeItem>();
        await foreach (var item in files.ListAsync("Photos", new SfeListOptions { PageSize = 2 }, Token))
            items.Add(item);
        var video = await files.GetAsync("Photos/a.mp4", Token);
        var properties = await files.GetPropertiesAsync("Photos/a.mp4", Token);

        CollectionAssert.AreEqual(
            new[] { "Trips", "a.mp4", "b.jpg", "c.txt", "d.txt" },
            items.Select(item => item.Name).ToArray()
        );
        Assert.AreEqual(("Photos/a.mp4", SfeItemKind.Video, 10L), (video.Path, video.Kind, video.Size));
        Assert.AreEqual(("fake", "fake", true), (properties.Kind, properties.HashName, properties.Downloadable));
        Assert.IsNotNull(properties.ContentHash);
        Assert.IsTrue(await files.ExistsAsync("Photos/Trips", Token));
        Assert.IsFalse(await files.ExistsAsync("Photos/Nowhere", Token));
        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(async () =>
        {
            await foreach (var _ in files.ListAsync("Nowhere", Token)) { }
        });
    }

    [TestMethod]
    public async Task AFileIsDownloadedWholeInPartAndAsAStream()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var files = world.Connection.Files;
        using var whole = new MemoryStream();
        using var part = new MemoryStream();

        await files.DownloadAsync("Photos/a.mp4", whole, cancellationToken: Token);
        var written = await files.DownloadAsync(
            "Photos/a.mp4",
            part,
            new SfeDownloadOptions { Offset = 2, Length = 3 },
            Token
        );
        using var stream = await files.OpenReadAsync("Photos/a.mp4", 7, Token);
        using var reader = new StreamReader(stream);

        Assert.AreEqual("0123456789", System.Text.Encoding.UTF8.GetString(whole.ToArray()));
        Assert.AreEqual(3L, written);
        Assert.AreEqual("234", System.Text.Encoding.UTF8.GetString(part.ToArray()));
        Assert.AreEqual("789", await reader.ReadToEndAsync(Token));
    }

    [TestMethod]
    public async Task SeveralItemsAreDownloadedAsOneZip()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        using var destination = new MemoryStream();

        var name = await world.Connection.Files.DownloadZipAsync(
            ["Photos/a.mp4", "Photos/b.jpg"],
            destination,
            cancellationToken: Token
        );

        destination.Position = 0;
        using var zip = new ZipArchive(destination, ZipArchiveMode.Read);
        StringAssert.EndsWith(name, ".zip");
        CollectionAssert.AreEquivalent(
            new[] { "a.mp4", "b.jpg" },
            zip.Entries.Select(entry => entry.FullName).ToArray()
        );
    }

    [TestMethod]
    public async Task WhatTheProviderCantDoIsRefusedBeforeTheCall()
    {
        using var world = await ConnectedServer.StartAsync(Token, capabilities: capabilities =>
            capabilities with
            {
                RangedReads = false,
                UploadsInParts = false,
                ServerSideMove = false,
                ServerSideCopy = false,
            }
        );
        var files = world.Connection.Files;
        using var whole = new MemoryStream();
        using var content = new MemoryStream([1, 2, 3]);

        await files.DownloadAsync("Photos/a.mp4", whole, cancellationToken: Token);
        var part = await Assert.ThrowsExactlyAsync<SfeNotSupportedException>(() =>
            files.DownloadAsync("Photos/a.mp4", new MemoryStream(), new SfeDownloadOptions { Offset = 2 }, Token)
        );
        var offset = await Assert.ThrowsExactlyAsync<SfeNotSupportedException>(() =>
            files.OpenReadAsync("Photos/a.mp4", 2, Token)
        );
        var upload = await Assert.ThrowsExactlyAsync<SfeNotSupportedException>(() =>
            files.UploadAsync("Photos/new.bin", content, cancellationToken: Token)
        );
        var move = await Assert.ThrowsExactlyAsync<SfeNotSupportedException>(() =>
            files.MoveAsync(["Photos/b.jpg"], "Photos/Trips", cancellationToken: Token)
        );
        var copy = await Assert.ThrowsExactlyAsync<SfeNotSupportedException>(() =>
            files.CopyAsync(["Photos/b.jpg"], "Photos/Trips", cancellationToken: Token)
        );

        Assert.AreEqual(10, whole.Length);
        Assert.AreEqual(SfeConnectionCapability.RangedReads, part.Capability);
        Assert.AreEqual(SfeConnectionCapability.RangedReads, offset.Capability);
        Assert.AreEqual(SfeConnectionCapability.UploadsInParts, upload.Capability);
        Assert.AreEqual(SfeConnectionCapability.ServerSideMove, move.Capability);
        Assert.AreEqual(SfeConnectionCapability.ServerSideCopy, copy.Capability);
        Assert.AreEqual(SfeFunction.Connections, move.Function);
        Assert.IsFalse(world.Account.Calls.Any(call => call.StartsWith("move", StringComparison.Ordinal)));
        Assert.IsFalse(world.Account.Calls.Any(call => call.StartsWith("upload", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AProvidersFailuresAreTheirConnectionProblemsAndItsWaitIsRetried()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var files = world.Connection.Files;
        world.Account.RetryAfter = TimeSpan.FromSeconds(1);
        world.Account.FailNext("get", RemoteErrorKind.RateLimited);

        var waited = await files.GetAsync("Photos/b.jpg", Token);
        world.Account.Failing = RemoteErrorKind.Unavailable;
        var unavailable = await Assert.ThrowsExactlyAsync<SfeApiException>(() => files.GetAsync("Photos", Token));
        world.Account.Failing = RemoteErrorKind.CredentialsEnded;
        var ended = await Assert.ThrowsExactlyAsync<SfeConflictException>(() => files.GetAsync("Photos", Token));
        var info = await world.Connection.GetInfoAsync(Token);

        Assert.AreEqual("b.jpg", waited.Name);
        Assert.AreEqual(2, world.Account.Calls.Count(call => call == "get Photos/b.jpg"), "Tried again after the wait");
        Assert.AreEqual(
            (502, SfeConnectionProblem.Unavailable),
            (unavailable.StatusCode, unavailable.ConnectionProblem)
        );
        Assert.AreEqual("connection-needs-reconnecting", ended.ProblemType);
        Assert.AreEqual(SfeConnectionProblem.NeedsReconnecting, ended.ConnectionProblem);
        Assert.AreEqual(SfeConnectionState.NeedsReconnecting, info.State);
    }
}
