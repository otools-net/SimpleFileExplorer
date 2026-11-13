using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Sfe.Core.Connections;
using Sfe.Core.Connections.Dropbox;
using Sfe.Core.Options;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="DropboxStorage"/> against recorded answers: the paths it sends (under the connection's root), pages,
/// reads whole and in part, and Dropbox's errors as <see cref="RemoteStorageException"/>. No test calls Dropbox.
/// </summary>
[TestClass]
public sealed class DropboxStorageTests
{
    private const string Link = "https://ucabc.dl.dropboxusercontent.com/cd/0/get/x";

    private static readonly string File = """
        {".tag":"file","name":"Sunrise.jpg","path_lower":"/photos/sunrise.jpg","path_display":"/Photos/Sunrise.jpg",
         "id":"id:file1","client_modified":"2026-01-01T10:00:00Z","server_modified":"2026-01-02T10:00:00Z",
         "rev":"015f0000000000000000001","size":12,"is_downloadable":true,"content_hash":"hash1"}
        """;

    private static readonly string Folder = """
        {".tag":"folder","name":"Trips","path_lower":"/photos/trips","path_display":"/Photos/Trips","id":"id:folder1"}
        """;

    private static readonly string NotFound = """
        {"error_summary":"path/not_found/..","error":{".tag":"path","path":{".tag":"not_found"}}}
        """;

    private readonly RecordedDropbox _dropbox = new();
    private string _secretsFolder = null!;
    private IRemoteStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _secretsFolder = Path.Combine(Path.GetTempPath(), $"sfe-secrets-{Guid.NewGuid():N}");
        var provider = new DropboxProvider(new OutboundPolicy(() => _dropbox), new ProviderSecrets(_secretsFolder));
        _storage = provider.Open("refresh-1", "Photos", new ConnectionOptions { Dropbox = new() { AppKey = "key1" } });
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _storage.DisposeAsync();
        _dropbox.Dispose();
    }

    [TestMethod]
    public async Task AFolderIsListedInPagesUnderTheConnectionsRoot()
    {
        _dropbox.Answers["/2/files/list_folder"] = (
            HttpStatusCode.OK,
            $$"""{"entries":[{{File}},{{Folder}}],"cursor":"c1","has_more":true}"""
        );
        _dropbox.Answers["/2/files/list_folder/continue"] = (
            HttpStatusCode.OK,
            """{"entries":[{".tag":"deleted","name":"Old","path_lower":"/photos/old"}],"cursor":"c2","has_more":false}"""
        );

        var first = await _storage.ListAsync("", null, Token);
        var second = await _storage.ListAsync("", first.Cursor, Token);

        Assert.AreEqual("/Photos", Json(_dropbox.Last("/2/files/list_folder").Body)["path"]!.GetValue<string>());
        Assert.AreEqual("c1", first.Cursor);
        var file = first.Items[0];
        Assert.AreEqual(
            new RemoteItem(
                "Sunrise.jpg",
                "Sunrise.jpg",
                false,
                12,
                new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc),
                "id:file1",
                "hash1"
            ),
            file
        );
        Assert.AreEqual(new RemoteItem("Trips", "Trips", true, Id: "id:folder1"), first.Items[1]);
        Assert.IsEmpty(second.Items);
        Assert.IsNull(second.Cursor);
        Assert.AreEqual("c1", Json(_dropbox.Last("/2/files/list_folder/continue").Body)["cursor"]!.GetValue<string>());
        // The access token came from the refresh token, from Dropbox's own hosts only.
        Assert.IsTrue(_dropbox.Requests.All(r => DropboxProvider.Rule.Allows(r.Request.RequestUri!)));
        Assert.Contains("refresh-1", _dropbox.Last("/oauth2/token").Body);
    }

    [TestMethod]
    public async Task WhatIsNotThereIsNullOrNotFound()
    {
        _dropbox.Answers["/2/files/get_metadata"] = (HttpStatusCode.Conflict, NotFound);
        _dropbox.Answers["/2/files/list_folder"] = (HttpStatusCode.Conflict, NotFound);

        Assert.IsNull(await _storage.GetAsync("Gone.jpg", Token));
        Assert.AreEqual(
            "/Photos/Gone.jpg",
            Json(_dropbox.Last("/2/files/get_metadata").Body)["path"]!.GetValue<string>()
        );
        var error = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _storage.ListAsync("Gone", null, Token)
        );
        Assert.AreEqual(RemoteErrorKind.NotFound, error.Kind);
        // The root needs no call.
        Assert.IsTrue((await _storage.GetAsync("", Token))!.IsFolder);
    }

    [TestMethod]
    public async Task AFileIsReadWholeOrInPartThroughATemporaryLink()
    {
        _dropbox.Handlers["/2/files/download"] = _ =>
        {
            var answer = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("hello world!")),
            };
            answer.Headers.Add("Dropbox-API-Result", File.Replace("\n", "").Replace("\r", ""));
            return answer;
        };
        _dropbox.Answers["/2/files/get_temporary_link"] = (
            HttpStatusCode.OK,
            $$"""{"metadata":{{File}},"link":"{{Link}}"}"""
        );
        _dropbox.Handlers[new Uri(Link).AbsolutePath] = request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var answer = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("world")),
            };
            answer.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, 12);
            return answer;
        };

        await using (var whole = await _storage.OpenReadAsync("Sunrise.jpg", null, Token))
        {
            Assert.AreEqual("hello world!", await new StreamReader(whole.Stream).ReadToEndAsync(Token));
            Assert.AreEqual(12, whole.TotalLength);
            Assert.IsNull(whole.Range);
        }
        await using var part = await _storage.OpenReadAsync("Sunrise.jpg", new RemoteRange(6, 10), Token);

        Assert.AreEqual("world", await new StreamReader(part.Stream).ReadToEndAsync(Token));
        Assert.AreEqual(new RemoteRange(6, 10), part.Range);
        Assert.AreEqual(12, part.TotalLength);
        Assert.AreEqual("bytes=6-10", _dropbox.Last(new Uri(Link).AbsolutePath).Request.Headers.Range!.ToString());
    }

    [TestMethod]
    public async Task ARefusedRefreshEndsTheCredentials()
    {
        _dropbox.Answers["/oauth2/token"] = (
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"refresh token is invalid or revoked"}"""
        );

        var error = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => _storage.ListAsync("", null, Token));

        Assert.AreEqual(RemoteErrorKind.CredentialsEnded, error.Kind);
    }

    [TestMethod]
    public async Task ChangingFilesIsNotThereYet()
    {
        var error = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _storage.CreateFolderAsync("New", Token)
        );
        Assert.AreEqual(RemoteErrorKind.NotSupported, error.Kind);
    }

    private static JsonNode Json(string body) => JsonNode.Parse(body)!;
}
