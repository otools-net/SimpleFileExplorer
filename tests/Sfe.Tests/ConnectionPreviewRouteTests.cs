using System.Net;
using System.Net.Http.Json;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Previews of a connection's files (agents/docs/features/connection-previews.md): thumbnails asked of the provider
/// together and kept in the server's cache under the file's id and version, the size the provider tells written on
/// the library's row, text read only; and the attack vectors (a hidden folder, another account's connection, a file
/// changed meanwhile).
/// </summary>
[TestClass]
public sealed class ConnectionPreviewRouteTests
{
    private RuledConnection _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _world = await RuledConnection.CreateAsync(Token);
        _world.Provider.Capabilities = _world.Provider.Capabilities with { Thumbnails = true };
        for (var i = 0; i < 4; i++)
        {
            _world.Account.WithFile($"Public/p{i}.jpg", $"picture {i}");
            _world.Account.Thumbnailed[$"Public/p{i}.jpg"] = new RemoteMedia(640 + i, 480, null);
        }
        _world.Account.WithFile("Private/hidden.jpg", "hidden picture");
        _world.Account.Thumbnailed["Private/hidden.jpg"] = null;
        await _world.IndexAsync(Token);
        _world.Account.Calls.Clear();
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task ThumbnailsAreAskedTogetherKeptAndTellTheFilesSizeToTheListing()
    {
        var asked = Enumerable.Range(0, 4).Select(i => ThumbnailAsync($"Public/p{i}.jpg"));
        var thumbnails = await Task.WhenAll(asked);
        var calls = ThumbnailCalls();
        var again = await ThumbnailAsync("Public/p0.jpg");
        var listing = await _world.Bob.GetFromJsonAsync<ConnectionListing>(
            $"{_world.Base}/browse?path=Public",
            RuledConnection.Json,
            Token
        );

        Assert.IsTrue(thumbnails.All(t => t.StartsWith("thumbnail of Public/p", StringComparison.Ordinal)));
        Assert.IsTrue(calls is > 0 and < 4, $"Asked together: {calls} calls for 4 thumbnails");
        Assert.AreEqual(thumbnails[0], again);
        Assert.AreEqual(calls, ThumbnailCalls(), "The second time comes from the cache");
        Assert.IsFalse(_world.Called("get "), "The file's version came from the library's index");
        var p1 = listing!.Listing.Items.Single(i => i.Name == "p1.jpg");
        Assert.AreEqual((641, 480), (p1.Width, p1.Height));
    }

    [TestMethod]
    public async Task AFileChangedAtTheProviderGetsANewThumbnail()
    {
        var before = await ThumbnailAsync("Public/p0.jpg");
        _world.Account.WithFile("Public/p0.jpg", "another picture");
        await _world.IndexAsync(Token);

        var after = await ThumbnailAsync("Public/p0.jpg");

        Assert.AreNotEqual(before, after);
    }

    [TestMethod]
    public async Task NoThumbnailOfAHiddenFileOrOfAnotherAccountsConnection()
    {
        var mine = await OwnConnectionOfTheAdministratorAsync();

        using var hidden = await _world.Bob.GetAsync($"{_world.Base}/thumbnails/Private/hidden.jpg", Token);
        using var cased = await _world.Bob.GetAsync($"{_world.Base}/thumbnails/PRIVATE/hidden.jpg", Token);
        using var other = await _world.Bob.GetAsync($"{ApiPaths.V1}/connections/{mine}/thumbnails/x.jpg", Token);

        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, cased.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        Assert.IsFalse(_world.Called("thumbnails"));
    }

    [TestMethod]
    public async Task TextIsReadOnlyAndRefusedWhereTheFolderIsHidden()
    {
        _world.Account.WithBytes("Public/data.bin", [0x41, 0x00, 0x42, 0x00, 0x00, 0x00, 0x43]);
        _world.Account.WithBytes("Public/huge.txt", new byte[TextEndpoints.MaxBytes + 1]);

        var text = await _world.Bob.GetFromJsonAsync<TextFile>(
            $"{_world.Base}/files/text?path=Public/a.txt",
            RuledConnection.Json,
            Token
        );
        using var hidden = await _world.Bob.GetAsync($"{_world.Base}/files/text?path=Private/secret.txt", Token);
        using var binary = await _world.Bob.GetAsync($"{_world.Base}/files/text?path=Public/data.bin", Token);
        using var huge = await _world.Bob.GetAsync($"{_world.Base}/files/text?path=Public/huge.txt", Token);

        Assert.AreEqual("public a", text!.Text);
        Assert.IsFalse(text.Access!.Overwrite || text.Access.SaveCopy);
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, binary.StatusCode);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        Assert.IsFalse(_world.Called("secret"), "Nothing of a hidden folder is asked of the provider");
        Assert.IsFalse(_world.Called("read Public/huge.txt"), "A file over the limit is never read");
    }

    [TestMethod]
    public async Task AVideoOfAHiddenFolderIsNeitherProbedNorPlayed()
    {
        _world.Account.WithFile("Private/film.mp4", "#EXTM3U\nhttp://127.0.0.1:9/a.ts\n");

        using var probe = await _world.Bob.GetAsync($"{_world.Base}/streaming/probe?path=Private/film.mp4", Token);
        using var play = await _world.Bob.GetAsync($"{_world.Base}/streaming/play?path=Private/film.mp4", Token);

        Assert.AreEqual(HttpStatusCode.NotFound, probe.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, play.StatusCode);
        Assert.IsFalse(_world.Called("film.mp4"));
    }

    private async Task<string> ThumbnailAsync(string path)
    {
        using var answer = await _world.Bob.GetAsync($"{_world.Base}/thumbnails/{path}", Token);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, path);
        Assert.AreEqual("image/jpeg", answer.Content.Headers.ContentType?.MediaType);
        return await answer.Content.ReadAsStringAsync(Token);
    }

    private int ThumbnailCalls() =>
        _world.Account.Calls.Count(c => c.StartsWith("thumbnails ", StringComparison.Ordinal));

    /// <summary>An account-level connection of the server's administrator (bob may never open it).</summary>
    private async Task<int> OwnConnectionOfTheAdministratorAsync()
    {
        var admin = (await _world.Get<AccessService>().GetServerAdminIdsAsync(Token)).Single();
        var draft = new ConnectionDraft(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: admin);
        var grant = new ConnectionGrant("acct", "acct@fake", "id:acct");
        return (await _world.Get<ConnectionService>().CreateAsync(draft, grant, admin, Token)).Value!.Id;
    }
}
