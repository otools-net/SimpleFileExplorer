using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Resumable uploads into a connection through <c>api/connections/{id}/files/uploads</c>: the library's protocol,
/// chunks passed on to the provider's session, offsets, names taken, an upload seen by its owner only, names and
/// targets refused, and the provider's session id never in an answer.
/// </summary>
[TestClass]
public sealed class ConnectionUploadRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeProvider _provider = new();
    private readonly List<string> _answers = [];
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private FakeAccount _account = null!;
    private int _id;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder
                    // The space's limit holds on a connection too (BadNamesAndTargetsLeavingTheRootAreRefused).
                    .UseSetting("Sfe:Files:Uploads:MaxFileSizeMegabytes", "1")
                    .ConfigureServices(services => services.AddSingleton<IConnectionProvider>(_provider))
            );
        await ApiClients.SetUpAsync(_factory, Cancel);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var group = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Administrators, Cancel))!;
        var admins = group.Id;
        // The file management rights are not a space's by default.
        var rights = new GroupInput(
            group.Name,
            Rights: [.. group.Rights, Rights.UploadFiles, Rights.DeleteFiles],
            MemberIds: group.MemberIds
        );
        var updated = await accounts.UpdateGroupAsync(tenant, admins, rights, cancellationToken: Cancel);
        Assert.AreEqual(AccessStatus.Ok, updated.Status);
        var adminId = (await accounts.GetServerAdminIdsAsync(Cancel)).Single();
        await accounts.CreateUserAsync(new("alice", Password: "alice-password", GroupIds: [admins]), null, Cancel);
        await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [admins]), null, Cancel);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password", Cancel);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Cancel);

        _account = _provider
            .Account("acct")
            .WithFolder("Photos/Trips")
            .WithFile("Photos/a.txt", "text a")
            .WithFile("Secret.txt", "outside the root");
        // The library's connection, used by its administrators: Alice and Bob both upload into it.
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(
                new(FakeProvider.FakeKind, "Team", ConnectionLevel.Library, TenantId: tenant, RootPath: "Photos"),
                new ConnectionGrant("acct", "acct@fake", "id:acct"),
                adminId,
                Cancel
            );
        _id = made.Value!.Id;
        var shared = await _factory
            .Services.GetRequiredService<ConnectionSharing>()
            .SetGroupsAsync(_id, tenant, new Dictionary<int, FolderLevel> { [admins] = FolderLevel.Write }, adminId);
        Assert.AreEqual(AccessStatus.Ok, shared.Status);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
        // No answer ever carries the provider's session (the fake's are "session-…").
        Assert.IsFalse(_answers.Any(a => a.Contains("session-", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AFileIsUploadedInTwoChunksIntoItsFolder()
    {
        var check = await CheckAsync("Trips", "new.txt");
        var modified = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var upload = await StartAsync(new UploadStart("Trips", "new.txt", 11, modified));
        var listed = await ListAsync(_alice);
        var first = await SendAsync(_alice, upload.Id, 0, "hello ");
        var second = await SendAsync(_alice, upload.Id, 6, "world");
        using var finish = await FinishAsync(_alice, upload.Id, ConflictChoice.Ask);
        var item = await ReadAsync<LibraryItem>(finish);

        Assert.IsEmpty(check.Existing);
        Assert.IsTrue(check.CanReplace);
        Assert.MatchesRegex("^[0-9a-f]{32}$", upload.Id);
        Assert.AreEqual(("Trips", "new.txt", 11L, 0L), (upload.Target, upload.Name, upload.Size, upload.Received));
        Assert.AreEqual(upload.Id, listed.Single().Id);
        Assert.AreEqual((6L, 11L), (first.Received, second.Received));
        Assert.AreEqual(HttpStatusCode.OK, finish.StatusCode);
        Assert.AreEqual(("new.txt", "Trips/new.txt", 11L), (item.Name, item.Path, item.Size));
        Assert.AreEqual("hello world", _account.TextOf("Photos/Trips/new.txt"));
        Assert.IsEmpty(await ListAsync(_alice));
        Assert.IsEmpty(_account.Uploads);
        var listing = await _alice.GetFromJsonAsync<ConnectionListing>(
            $"{V1}/connections/{_id}/browse?path=Trips",
            Json,
            Cancel
        );
        CollectionAssert.Contains(listing!.Listing.Items.Select(i => i.Name).ToArray(), "new.txt");
    }

    [TestMethod]
    public async Task AChunkNotMatchingItsDigestNeverReachesTheProvider()
    {
        var upload = await StartAsync(new UploadStart("", "checked.txt", 10));

        using var damaged = await PutAsync(_alice, upload.Id, 0, "abcde", ChunkDigest.Of("abcdX"u8));
        var provider = _account.Locked(() => _account.Uploads.Values.Single().Length);
        var first = await SendAsync(_alice, upload.Id, 0, "abcde", ChunkDigest.Of("abcde"u8));
        await SendAsync(_alice, upload.Id, 5, "fghij");
        using var finish = await FinishAsync(_alice, upload.Id, ConflictChoice.Ask);

        Assert.AreEqual(HttpStatusCode.BadRequest, damaged.StatusCode);
        StringAssert.Contains(await damaged.Content.ReadAsStringAsync(Cancel), ProblemTypes.ChunkDigest);
        Assert.AreEqual(0L, provider);
        Assert.AreEqual(5, first.Received);
        Assert.AreEqual("abcdefghij", _account.TextOf("Photos/checked.txt"));
    }

    [TestMethod]
    public async Task AWrongOffsetIsAnsweredWithWhatTheProviderHas()
    {
        var upload = await StartAsync(new UploadStart("", "ten.txt", 10));
        await SendAsync(_alice, upload.Id, 0, "abcde");
        using var behind = await PutAsync(_alice, upload.Id, 3, "xx");
        // A part that reached the provider although its answer was lost.
        _account.Locked(() => _account.Uploads.Values.Single().Write("fgh"u8));
        using var lost = await PutAsync(_alice, upload.Id, 5, "fghij");
        var last = await SendAsync(_alice, upload.Id, 8, "ij");
        using var tooLong = await PutAsync(_alice, upload.Id, 10, "k");
        using var finish = await FinishAsync(_alice, upload.Id, ConflictChoice.Ask);

        Assert.AreEqual(HttpStatusCode.Conflict, behind.StatusCode);
        Assert.AreEqual(5, (await ReadAsync<UploadInfo>(behind)).Received);
        Assert.AreEqual(HttpStatusCode.Conflict, lost.StatusCode);
        Assert.AreEqual(8, (await ReadAsync<UploadInfo>(lost)).Received);
        Assert.AreEqual(10, last.Received);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, finish.StatusCode);
        Assert.AreEqual("abcdefghij", _account.TextOf("Photos/ten.txt"));
    }

    [TestMethod]
    public async Task AnUploadIsItsOwnersOnly()
    {
        var upload = await StartAsync(new UploadStart("", "mine.txt", 4));

        using var get = await _bob.GetAsync($"{Base}/{upload.Id}", Cancel);
        using var put = await PutAsync(_bob, upload.Id, 0, "evil");
        using var finish = await FinishAsync(_bob, upload.Id, ConflictChoice.Replace);
        using var cancel = await _bob.DeleteAsync($"{Base}/{upload.Id}", Cancel);
        var bobs = await ListAsync(_bob);
        var alices = await ListAsync(_alice);
        using var cancelled = await _alice.DeleteAsync($"{Base}/{upload.Id}", Cancel);

        foreach (var answer in new[] { get, put, finish, cancel })
            Assert.AreEqual(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.IsEmpty(bobs);
        Assert.AreEqual((upload.Id, 0L), (alices.Single().Id, alices.Single().Received));
        Assert.AreEqual(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.IsEmpty(await ListAsync(_alice));
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("upload-append", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ANameTakenIsAskedThenKeptBothSkippedOrReplaced()
    {
        var check = await CheckAsync("", "A.TXT", "new.txt", "Trips/x.txt", "a.txt/x");

        var both = await UploadAsync("a.txt", "second");
        using var asked = await FinishAsync(_alice, both.Id, ConflictChoice.Ask);
        using var kept = await FinishAsync(_alice, both.Id, ConflictChoice.KeepBoth);
        var skip = await UploadAsync("a.txt", "third");
        using var skipped = await FinishAsync(_alice, skip.Id, ConflictChoice.Skip);
        var replace = await UploadAsync("a.txt", "fourth");
        using var replaced = await FinishAsync(_alice, replace.Id, ConflictChoice.Replace);

        CollectionAssert.AreEqual(new[] { "A.TXT" }, check.Existing.ToArray());
        Assert.AreEqual(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.AreEqual(FileEndpoints.ExistsType, (await ReadAsync<ProblemDetails>(asked)).Type);
        Assert.AreEqual("a (1).txt", (await ReadAsync<LibraryItem>(kept)).Path);
        Assert.AreEqual("second", _account.TextOf("Photos/a (1).txt"));
        Assert.AreEqual(HttpStatusCode.NoContent, skipped.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, replaced.StatusCode);
        Assert.AreEqual("fourth", _account.TextOf("Photos/a.txt"));
        Assert.IsEmpty(await ListAsync(_alice));
    }

    [TestMethod]
    public async Task AFolderUploadMakesItsFoldersAtTheProvider()
    {
        var check = await CheckAsync("", "Trips", "Trips/x.txt");
        _account.WithFile("Photos/Trips/x.txt", "there");
        var again = await CheckAsync("", "Trips/x.txt", "Trips/y.txt");
        var upload = await UploadAsync("Trips/Day 1/b.txt", "deep");
        using var finish = await FinishAsync(_alice, upload.Id, ConflictChoice.Ask);

        CollectionAssert.AreEqual(new[] { "Trips" }, check.Existing.ToArray());
        CollectionAssert.AreEqual(new[] { "Trips/x.txt" }, again.Existing.ToArray());
        Assert.AreEqual("Trips/Day 1/b.txt", (await ReadAsync<LibraryItem>(finish)).Path);
        Assert.AreEqual("deep", _account.TextOf("Photos/Trips/Day 1/b.txt"));
    }

    [TestMethod]
    public async Task BadNamesAndTargetsLeavingTheRootAreRefused()
    {
        var names = new[] { "../Secret.txt", "a/../b", "..\\Secret.txt", "", "x.", " x", "a//b" };
        var answers = new List<HttpResponseMessage>();
        foreach (var name in names)
            answers.Add(await _alice.PostAsJsonAsync(Base, new UploadStart("", name, 1), Json, Cancel));
        using var negative = await _alice.PostAsJsonAsync(Base, new UploadStart("", "x.txt", -1), Json, Cancel);
        using var tooLarge = await _alice.PostAsJsonAsync(Base, new UploadStart("", "x.bin", 2 << 20), Json, Cancel);
        using var above = await _alice.PostAsJsonAsync(Base, new UploadStart("..", "x.txt", 1), Json, Cancel);
        using var missing = await _alice.PostAsJsonAsync(Base, new UploadStart("Nowhere", "x.txt", 1), Json, Cancel);
        using var aFile = await _alice.PostAsJsonAsync(Base, new UploadStart("a.txt", "x.txt", 1), Json, Cancel);
        using var checkAbove = await _alice.PostAsJsonAsync(
            $"{Base}/check",
            new UploadCheckRequest("../", ["Secret.txt"]),
            Json,
            Cancel
        );
        using var noNames = await _alice.PostAsJsonAsync($"{Base}/check", new UploadCheckRequest("", []), Json, Cancel);
        var check = await CheckAsync("", "../Secret.txt");

        foreach (var answer in answers.Append(negative).Append(tooLarge).Append(noNames))
            Assert.AreEqual(HttpStatusCode.BadRequest, answer.StatusCode);
        foreach (var answer in new[] { above, missing, aFile, checkAbove })
            Assert.AreEqual(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.IsEmpty(check.Existing);
        Assert.IsFalse(_account.Calls.Any(c => c.Contains("..", StringComparison.Ordinal) || c.Contains("Secret")));
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("upload", StringComparison.Ordinal)));
        answers.ForEach(a => a.Dispose());
    }

    [TestMethod]
    public async Task AConnectionOnlyReadOrAProviderWithoutPartsIsRefused()
    {
        var upload = await StartAsync(new UploadStart("", "x.txt", 1));
        var connections = _factory.Services.GetRequiredService<ConnectionService>();
        await connections.UpdateAsync(_id, new ConnectionChange(ReadOnly: true), null, Cancel);

        using var start = await _alice.PostAsJsonAsync(Base, new UploadStart("", "y.txt", 1), Json, Cancel);
        using var check = await _alice.PostAsJsonAsync(
            $"{Base}/check",
            new UploadCheckRequest("", ["y"]),
            Json,
            Cancel
        );
        using var put = await PutAsync(_alice, upload.Id, 0, "x");
        using var finish = await FinishAsync(_alice, upload.Id, ConflictChoice.Ask);
        await connections.UpdateAsync(_id, new ConnectionChange(ReadOnly: false), null, Cancel);
        _provider.Capabilities = _provider.Capabilities with { UploadsInParts = false };
        using var unsupported = await _alice.PostAsJsonAsync(Base, new UploadStart("", "y.txt", 1), Json, Cancel);

        foreach (var answer in new[] { start, check, put, finish })
            Assert.AreEqual(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, unsupported.StatusCode);
        Assert.AreEqual(ConnectionProblems.NotSupportedType, (await ReadAsync<ProblemDetails>(unsupported)).Type);
        Assert.IsFalse(_account.Exists("Photos/x.txt"));
    }

    private string Base => $"{V1}/connections/{_id}/files/uploads";

    private async Task<UploadInfo> StartAsync(UploadStart start)
    {
        using var answer = await _alice.PostAsJsonAsync(Base, start, Json, Cancel);
        Assert.AreEqual(HttpStatusCode.Created, answer.StatusCode, await answer.Content.ReadAsStringAsync(Cancel));
        return await ReadAsync<UploadInfo>(answer);
    }

    /// <summary>A whole upload of <paramref name="text"/> in one chunk, not finished.</summary>
    private async Task<UploadInfo> UploadAsync(string name, string text)
    {
        var upload = await StartAsync(new UploadStart("", name, Encoding.UTF8.GetByteCount(text)));
        return await SendAsync(_alice, upload.Id, 0, text);
    }

    private async Task<UploadCheck> CheckAsync(string target, params string[] names)
    {
        using var answer = await _alice.PostAsJsonAsync(
            $"{Base}/check",
            new UploadCheckRequest(target, names),
            Json,
            Cancel
        );
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode);
        return await ReadAsync<UploadCheck>(answer);
    }

    private async Task<IReadOnlyList<UploadInfo>> ListAsync(HttpClient client)
    {
        using var answer = await client.GetAsync(Base, Cancel);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode);
        return await ReadAsync<List<UploadInfo>>(answer);
    }

    private async Task<UploadInfo> SendAsync(
        HttpClient client,
        string id,
        long offset,
        string text,
        string? digest = null
    )
    {
        using var answer = await PutAsync(client, id, offset, text, digest);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Cancel));
        return await ReadAsync<UploadInfo>(answer);
    }

    private Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        string id,
        long offset,
        string text,
        string? digest = null
    )
    {
        var body = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (digest is not null)
            body.Headers.TryAddWithoutValidation(ChunkDigest.Header, digest);
        return client.PutAsync($"{Base}/{id}?offset={offset}", body, Cancel);
    }

    private Task<HttpResponseMessage> FinishAsync(HttpClient client, string id, ConflictChoice choice) =>
        client.PostAsJsonAsync($"{Base}/{id}/finish", new UploadFinish(choice), Json, Cancel);

    /// <summary>An answer's body, kept to check that no answer names the provider's session.</summary>
    private async Task<T> ReadAsync<T>(HttpResponseMessage answer)
    {
        var text = await answer.Content.ReadAsStringAsync(Cancel);
        _answers.Add(text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }
}
