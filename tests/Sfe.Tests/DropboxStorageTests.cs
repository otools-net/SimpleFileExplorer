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

    private static readonly string Deleted = """{".tag":"deleted","name":"Old","path_lower":"/photos/old"}""";

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
            $$"""{"entries":[{{Deleted}}],"cursor":"c2","has_more":false}"""
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
    public async Task AFolderIsCreatedAndDeletedUnderTheConnectionsRoot()
    {
        _dropbox.Answers["/2/files/create_folder_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{Folder}}}""");
        _dropbox.Answers["/2/files/delete_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{Folder}}}""");

        var folder = await _storage.CreateFolderAsync("Trips", Token);
        await _storage.DeleteAsync("Trips", Token);

        Assert.AreEqual(new RemoteItem("Trips", "Trips", true, Id: "id:folder1"), folder);
        var created = Json(_dropbox.Last("/2/files/create_folder_v2").Body);
        Assert.AreEqual(("/Photos/Trips", false), (PathOf(created), created["autorename"]!.GetValue<bool>()));
        Assert.AreEqual("/Photos/Trips", PathOf(Json(_dropbox.Last("/2/files/delete_v2").Body)));
        // The root is never deleted.
        var root = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => _storage.DeleteAsync("", Token));
        Assert.AreEqual(RemoteErrorKind.NotFound, root.Kind);
    }

    [TestMethod]
    public async Task MovesAndCopiesAskDropboxToRenameOnlyToKeepBoth()
    {
        _dropbox.Answers["/2/files/move_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{File}}}""");
        _dropbox.Answers["/2/files/copy_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{File}}}""");

        var moved = await _storage.MoveAsync("a.jpg", "Sunrise.jpg", RemoteConflict.Fail, Token);
        var move = Json(_dropbox.Last("/2/files/move_v2").Body);
        await _storage.CopyAsync("a.jpg", "Trips/a.jpg", RemoteConflict.KeepBoth, Token);
        var copy = Json(_dropbox.Last("/2/files/copy_v2").Body);

        Assert.AreEqual("Sunrise.jpg", moved.Path);
        Assert.AreEqual(("/Photos/a.jpg", "/Photos/Sunrise.jpg"), (From(move), To(move)));
        Assert.IsFalse(move["autorename"]!.GetValue<bool>());
        Assert.IsTrue(move["allow_shared_folder"]!.GetValue<bool>());
        Assert.AreEqual(("/Photos/a.jpg", "/Photos/Trips/a.jpg"), (From(copy), To(copy)));
        Assert.IsTrue(copy["autorename"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task ReplacingDeletesTheFileThereButNeverAFolder()
    {
        _dropbox.Answers["/2/files/get_metadata"] = (HttpStatusCode.OK, File);
        _dropbox.Answers["/2/files/delete_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{File}}}""");
        _dropbox.Answers["/2/files/move_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{File}}}""");

        await _storage.MoveAsync("a.jpg", "Sunrise.jpg", RemoteConflict.Replace, Token);
        var calls = _dropbox.Requests.Select(r => r.Request.RequestUri!.AbsolutePath).Where(p => p.StartsWith("/2/"));
        var replaced = calls.ToArray();
        _dropbox.Answers["/2/files/get_metadata"] = (HttpStatusCode.OK, Folder);
        var folder = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _storage.MoveAsync("a.jpg", "Trips", RemoteConflict.Replace, Token)
        );

        CollectionAssert.AreEqual(
            new[] { "/2/files/get_metadata", "/2/files/delete_v2", "/2/files/move_v2" },
            replaced
        );
        Assert.AreEqual("/Photos/Sunrise.jpg", PathOf(Json(_dropbox.Last("/2/files/delete_v2").Body)));
        Assert.AreEqual(RemoteErrorKind.Exists, folder.Kind);
        Assert.HasCount(1, _dropbox.Requests.Where(r => r.Request.RequestUri!.AbsolutePath == "/2/files/delete_v2"));
    }

    [TestMethod]
    public async Task ARenameChangingOnlyTheCaseGoesThroughATemporaryName()
    {
        _dropbox.Answers["/2/files/move_v2"] = (HttpStatusCode.OK, $$"""{"metadata":{{File}}}""");

        await _storage.MoveAsync("sunrise.jpg", "Sunrise.jpg", RemoteConflict.Fail, Token);

        var moves = _dropbox
            .Requests.Where(r => r.Request.RequestUri!.AbsolutePath == "/2/files/move_v2")
            .Select(r => Json(r.Body))
            .ToList();
        Assert.HasCount(2, moves);
        Assert.AreEqual("/Photos/sunrise.jpg", From(moves[0]));
        Assert.AreEqual(To(moves[0]), From(moves[1]));
        Assert.StartsWith("/Photos/Sunrise.jpg.sfe-", To(moves[0]));
        Assert.AreEqual("/Photos/Sunrise.jpg", To(moves[1]));
    }

    [TestMethod]
    public async Task DropboxsWriteErrorsAreMapped()
    {
        // Dropbox's own answers: the summary, and the error as the SDK decodes it.
        var cases = new (string Summary, string Error, RemoteErrorKind Kind)[]
        {
            ("to/conflict/file/..", """{".tag":"to","to":{".tag":"conflict","conflict":{".tag":"file"}}}""", Exists),
            ("from_lookup/not_found/..", """{".tag":"from_lookup","from_lookup":{".tag":"not_found"}}""", NotThere),
            ("cant_move_folder_into_itself/", """{".tag":"cant_move_folder_into_itself"}""", Invalid),
            ("too_many_files/", """{".tag":"too_many_files"}""", RemoteErrorKind.Refused),
            ("insufficient_quota/", """{".tag":"insufficient_quota"}""", RemoteErrorKind.TooLarge),
            ("to/disallowed_name/", """{".tag":"to","to":{".tag":"disallowed_name"}}""", Invalid),
        };
        foreach (var (summary, body, kind) in cases)
        {
            _dropbox.Answers["/2/files/move_v2"] = (
                HttpStatusCode.Conflict,
                $$"""{"error_summary":"{{summary}}","error":{{body}}}"""
            );
            var error = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
                _storage.MoveAsync("a.jpg", "Trips/a.jpg", RemoteConflict.Fail, Token)
            );
            Assert.AreEqual(kind, error.Kind, summary);
        }
    }

    [TestMethod]
    public async Task AnUploadGoesThroughASequentialSessionUnderTheConnectionsRoot()
    {
        _dropbox.Answers["/2/files/upload_session/start"] = (HttpStatusCode.OK, """{"session_id":"s1"}""");
        _dropbox.Answers["/2/files/upload_session/append_v2"] = (HttpStatusCode.OK, "null");
        _dropbox.Answers["/2/files/upload_session/finish"] = (HttpStatusCode.OK, File);
        var modified = new DateTimeOffset(2026, 1, 1, 12, 0, 0, 500, TimeSpan.FromHours(2));

        var session = await _storage.BeginUploadAsync(Token);
        await _storage.AppendUploadAsync(session, 0, new MemoryStream(Encoding.ASCII.GetBytes("hello ")), Token);
        await _storage.AppendUploadAsync(session, 6, new OneWayStream(Encoding.ASCII.GetBytes("world!")), Token);
        var target = new RemoteUploadTarget("Sunrise.jpg", 12, RemoteConflict.KeepBoth, modified);
        var item = await _storage.FinishUploadAsync(session, target, Token);

        Assert.AreEqual("s1", session);
        var start = Arg(_dropbox.Last("/2/files/upload_session/start").Request);
        Assert.AreEqual("sequential", Tag(start["session_type"]!));
        Assert.IsFalse(start["close"]!.GetValue<bool>());
        var appends = _dropbox
            .Requests.Where(r => r.Request.RequestUri!.AbsolutePath == "/2/files/upload_session/append_v2")
            .ToList();
        CollectionAssert.AreEqual(new[] { "hello ", "world!" }, appends.Select(a => a.Body).ToArray());
        CollectionAssert.AreEqual(new[] { 0L, 6L }, appends.Select(a => OffsetOf(Arg(a.Request))).ToArray());
        Assert.IsTrue(appends.All(a => Arg(a.Request)["cursor"]!["session_id"]!.GetValue<string>() == "s1"));
        var finish = Arg(_dropbox.Last("/2/files/upload_session/finish").Request);
        Assert.AreEqual(12, OffsetOf(finish));
        var commit = finish["commit"]!;
        Assert.AreEqual(("/Photos/Sunrise.jpg", "add"), (PathOf(commit), Tag(commit["mode"]!)));
        Assert.IsTrue(commit["autorename"]!.GetValue<bool>());
        Assert.AreEqual("2026-01-01T10:00:00Z", commit["client_modified"]!.GetValue<string>());
        Assert.AreEqual("Sunrise.jpg", item.Path);
    }

    [TestMethod]
    public async Task ReplacingOverwritesAndWrongOffsetsAreMapped()
    {
        _dropbox.Answers["/2/files/upload_session/finish"] = (HttpStatusCode.OK, File);
        _dropbox.Answers["/2/files/upload_session/append_v2"] = (
            HttpStatusCode.Conflict,
            """{"error_summary":"incorrect_offset/..","error":{".tag":"incorrect_offset","correct_offset":12}}"""
        );

        await _storage.FinishUploadAsync("s1", new RemoteUploadTarget("a.jpg", 0, RemoteConflict.Replace), Token);
        var commit = Arg(_dropbox.Last("/2/files/upload_session/finish").Request)["commit"]!;
        var append = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _storage.AppendUploadAsync("s1", 6, new MemoryStream([1, 2]), Token)
        );
        _dropbox.Answers["/2/files/upload_session/finish"] = (
            HttpStatusCode.Conflict,
            """
            {"error_summary":"lookup_failed/incorrect_offset/..",
             "error":{".tag":"lookup_failed","lookup_failed":{".tag":"incorrect_offset","correct_offset":6}}}
            """
        );
        var finish = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _storage.FinishUploadAsync("s1", new RemoteUploadTarget("a.jpg", 9, RemoteConflict.Fail), Token)
        );
        _dropbox.Answers["/2/files/upload_session/finish"] = (
            HttpStatusCode.Conflict,
            """
            {"error_summary":"path/conflict/file/..",
             "error":{".tag":"path","path":{".tag":"conflict","conflict":{".tag":"file"}}}}
            """
        );
        var taken = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            _storage.FinishUploadAsync("s1", new RemoteUploadTarget("a.jpg", 9, RemoteConflict.Fail), Token)
        );

        Assert.AreEqual(("overwrite", false), (Tag(commit["mode"]!), commit["autorename"]!.GetValue<bool>()));
        Assert.AreEqual((RemoteErrorKind.WrongOffset, (long?)12), (append.Kind, append.Offset));
        Assert.AreEqual(RemoteErrorKind.WrongOffset, finish.Kind);
        Assert.AreEqual(RemoteErrorKind.Exists, taken.Kind);
    }

    private const RemoteErrorKind Exists = RemoteErrorKind.Exists;

    private const RemoteErrorKind NotThere = RemoteErrorKind.NotFound;

    private const RemoteErrorKind Invalid = RemoteErrorKind.InvalidName;

    private static string PathOf(JsonNode body) => body["path"]!.GetValue<string>();

    private static string From(JsonNode body) => body["from_path"]!.GetValue<string>();

    private static string To(JsonNode body) => body["to_path"]!.GetValue<string>();

    private static JsonNode Json(string body) => JsonNode.Parse(body)!;

    /// <summary>The arguments of a content call: Dropbox takes them in a header, the body being the bytes.</summary>
    private static JsonNode Arg(HttpRequestMessage request) =>
        Json(request.Headers.GetValues("Dropbox-API-Arg").Single());

    private static long OffsetOf(JsonNode arg) => arg["cursor"]!["offset"]!.GetValue<long>();

    /// <summary>A union's tag, written as a string or as an object with <c>.tag</c>.</summary>
    private static string Tag(JsonNode union) =>
        union is JsonValue ? union.GetValue<string>() : union[".tag"]!.GetValue<string>();

    /// <summary>A request's body as it comes: read once, no length.</summary>
    private sealed class OneWayStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
