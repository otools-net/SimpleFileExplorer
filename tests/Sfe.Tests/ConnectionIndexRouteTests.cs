using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Tasks;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The connections' index through the server: started when a connection is first opened in a library, its status
/// and "Sync now" (<c>api/connections/{id}/index</c>) for who manages it only, a run of the "Connection index" task
/// per sync, changes made through SFE written at once and told to the change feeds of those who use the connection,
/// and the rows dropped when the connection is removed or no longer offered to the library.
/// </summary>
[TestClass]
public sealed class ConnectionIndexRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _bob = null!;
    private int _adminId;
    private int _tenant;
    private int _id;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(_provider))
            );
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Token)).Single().Id;
        _adminId = (await accounts.GetServerAdminIdsAsync(Token)).Single();
        // Bob administers the library (he manages its connections) but never sees an account's own.
        var administrators = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Administrators, Token))!.Id;
        await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [administrators]), null, Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);

        _provider
            .Account("acct")
            .WithFile("Photos/Trips/Beach.jpg", "beach")
            .WithFile("Photos/a.txt", "a")
            .WithFile("Readme.txt", "read me");
        _id = await CreateAsync(new(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: _adminId));
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _bob.Dispose();
        _admin.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task OpeningAConnectionIndexesItAndOnlyItsOwnerSeesWhereItStands()
    {
        using var opened = await _admin.GetAsync($"{V1}/connections/{_id}/browse", Token);
        Assert.AreEqual(HttpStatusCode.OK, opened.StatusCode);

        var status = await SyncedAsync();
        Assert.AreEqual((3L, 3L, false), (status.Files, status.Folders, status.Listing));
        using var bobAsks = await _bob.GetAsync($"{V1}/connections/{_id}/index", Token);
        using var bobSyncs = await _bob.PostAsync($"{V1}/connections/{_id}/index/sync", null, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, bobAsks.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, bobSyncs.StatusCode);

        var runs = await Library<TaskHistory>().ListAsync(BackgroundTaskKind.ConnectionIndex, null, null, Token);
        var run = runs.Runs.Single();
        Assert.AreEqual((TaskTrigger.Automatic, TaskOutcome.Completed), (run.Trigger, run.Outcome));
        Assert.IsNull(run.Path, "An account's own connection is not named in the library's history.");
    }

    [TestMethod]
    public async Task SyncNowIsARunOfTheTaskAndChangesThroughSfeAreIndexedAtOnce()
    {
        using var started = await _admin.PostAsync($"{V1}/connections/{_id}/index/sync", null, Token);
        Assert.AreEqual(HttpStatusCode.Accepted, started.StatusCode);
        await SyncedAsync();
        using var tasks = await _admin.GetAsync($"{V1}/tasks", Token);
        StringAssert.Contains(await tasks.Content.ReadAsStringAsync(Token), "\"connectionIndex\"");

        await PostFilesAsync("folders", new CreateFolderRequest("Photos", "New"));
        await PostFilesAsync("rename", new RenameRequest("Readme.txt", "Read me first.txt"));
        await PostFilesAsync("delete", new DeleteRequest(["Photos/Trips"], false));

        await using var db = Library<IDbContextFactory<SfeDbContext>>().CreateDbContext();
        var live = await db.RemoteFiles.Where(f => !f.Removed).Select(f => f.FullName).ToListAsync(Token);
        CollectionAssert.AreEquivalent(new[] { "photos/a.txt", "read me first.txt" }, live);
        Assert.IsTrue(await db.RemoteDirectories.AnyAsync(d => d.FullName == "photos/new" && !d.Removed, Token));
    }

    [TestMethod]
    public async Task TheChangeFeedTellsAConnectionsFolderOnlyToThoseWhoUseIt()
    {
        using var feed = await _admin.GetAsync($"{V1}/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);
        using var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        using var bobFeed = await _bob.GetAsync($"{V1}/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);
        using var bobReader = new StreamReader(await bobFeed.Content.ReadAsStreamAsync(Token));

        await PostFilesAsync("folders", new CreateFolderRequest("Photos", "New"));
        using var local = await _admin.PostAsJsonAsync($"{V1}/files/folders", new CreateFolderRequest("", "x"), Token);

        var change = await NextChangeAsync(reader);
        Assert.AreEqual(("Photos", _id), (change.Path, change.Connection));
        var bobs = await NextChangeAsync(bobReader);
        Assert.AreEqual(("", 0), (bobs.Path, bobs.Connection), "Bob hears the library's folder only.");
    }

    [TestMethod]
    public async Task RowsGoWhenTheConnectionIsRemovedOrNoLongerOfferedToTheLibrary()
    {
        var shared = await CreateAsync(new(FakeProvider.FakeKind, "Shared", ConnectionLevel.Server));
        var sharing = _factory.Services.GetRequiredService<ConnectionSharing>();
        await sharing.SetTenantsAsync(shared, true, [], _adminId, Token);
        var indexes = Library<ConnectionIndexes>();
        foreach (var id in new[] { _id, shared })
        {
            Assert.IsTrue(await indexes.StartAsync(id, TaskTrigger.Manual, null));
            await SyncedAsync(id);
        }

        var other = await _factory
            .Services.GetRequiredService<AccessService>()
            .CreateTenantAsync(new TenantInput("Other"), _library.Root, Token);
        await sharing.SetTenantsAsync(shared, false, [other.Value!.Id], _adminId, Token);
        using var removed = await _admin.DeleteAsync($"{V1}/connections/{_id}", Token);
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        await indexes.PruneAsync(Token);

        await using var db = Library<IDbContextFactory<SfeDbContext>>().CreateDbContext();
        Assert.AreEqual(0, await db.RemoteFiles.CountAsync(Token));
        Assert.AreEqual(0, await db.RemoteDirectories.CountAsync(Token));
        Assert.AreEqual(0, await db.RemoteIndexes.CountAsync(Token));
    }

    [TestMethod]
    public async Task AFolderTheIndexHoldsIsListedFromItWithWhatSfeChangedAtOnce()
    {
        using (await _admin.GetAsync($"{V1}/connections/{_id}/browse", Token)) { }
        await SyncedAsync();
        var account = _provider.Account("acct");
        account.WithFile("Photos/behind.txt", "made at the provider, not synced yet");
        account.Calls.Clear();

        await PostFilesAsync("folders", new CreateFolderRequest("Photos", "New"));
        var photos = await BrowseAsync("Photos");

        CollectionAssert.AreEqual(
            new[] { "New", "Trips", "a.txt" },
            photos.Listing.Items.Select(i => i.Name).ToArray()
        );
        Assert.AreEqual((3, 2), (photos.Listing.Total, photos.Listing.DirectoryCount));
        Assert.IsTrue(photos.Listing.Items.All(i => i.Indexed == true));
        Assert.IsFalse(account.Calls.Any(c => c.StartsWith("list", StringComparison.Ordinal)), "no listing asked");
    }

    [TestMethod]
    public async Task DuringAFullListingTheProviderIsReadAndWhatTheIndexLacksIsMarked()
    {
        using (await _admin.GetAsync($"{V1}/connections/{_id}/browse", Token)) { }
        await SyncedAsync();
        _provider.Account("acct").WithFile("Photos/behind.txt", "not in the index").WithFolder("Photos/Later");
        await using (var db = Library<IDbContextFactory<SfeDbContext>>().CreateDbContext())
        {
            await db.RemoteIndexes.ExecuteUpdateAsync(
                s => s.SetProperty(i => i.Phase, RemoteIndexPhase.Listing),
                Token
            );
        }

        var photos = await BrowseAsync("Photos");

        var marks = photos.Listing.Items.ToDictionary(i => i.Name, i => i.Indexed);
        Assert.HasCount(4, marks);
        Assert.AreEqual((true, true), (marks["Trips"], marks["a.txt"]));
        Assert.AreEqual((false, false), (marks["Later"], marks["behind.txt"]));
    }

    private async Task<ConnectionListing> BrowseAsync(string path)
    {
        var url = $"{V1}/connections/{_id}/browse?path={Uri.EscapeDataString(path)}";
        using var answer = await _admin.GetAsync(url, Token);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode);
        return (await answer.Content.ReadFromJsonAsync<ConnectionListing>(Json, Token))!;
    }

    private async Task<int> CreateAsync(ConnectionDraft draft)
    {
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(draft, new ConnectionGrant("acct", "acct@fake", "id:acct"), _adminId, Token);
        return made.Value!.Id;
    }

    private T Library<T>()
        where T : notnull
    {
        var host = _factory.Services.GetRequiredService<TenantRegistry>().GetAsync(_tenant, Token).Result;
        return host!.Services.GetRequiredService<T>();
    }

    /// <summary>The index's status once a sync ended (10 s at most).</summary>
    private async Task<ConnectionIndexStatus> SyncedAsync(int? id = null)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var status = await Library<ConnectionIndexes>().StatusAsync(id ?? _id, Token);
            if (!status.Running && status.LastSync is not null)
            {
                Assert.IsNull(status.LastError);
                using var asked = await _admin.GetAsync($"{V1}/connections/{id ?? _id}/index", Token);
                Assert.AreEqual(HttpStatusCode.OK, asked.StatusCode);
                return (await asked.Content.ReadFromJsonAsync<ConnectionIndexStatus>(Json, Token))!;
            }
            if (DateTime.UtcNow > until)
                throw new AssertFailedException($"No sync ended: {status}");
            await Task.Delay(50, Token);
        }
    }

    private async Task PostFilesAsync<T>(string route, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{V1}/connections/{_id}/files/{route}")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Add(ChangeEndpoints.TabHeader, "tab-1");
        using var answer = await _admin.SendAsync(request, Token);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Token));
    }

    /// <summary>The next `changed` event: a library's folder has no connection (0).</summary>
    private async Task<ConnectionFolderChange> NextChangeAsync(StreamReader reader)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? current = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
                current = line["event: ".Length..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && current == "changed")
                return JsonSerializer.Deserialize<ConnectionFolderChange>(line["data: ".Length..], Json)!;
        }
        throw new AssertFailedException("The feed ended without a change.");
    }
}
