using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// A connection's files through <c>api/connections/{id}/browse</c>, <c>files/raw</c> and <c>thumbnails</c>: the
/// library's listing shape, ranges, the sandbox of served files, and the attack vectors of the plan (another
/// account's connection, paths leaving the root, a provider whose sign-in ended).
/// </summary>
[TestClass]
public sealed class ConnectionFileRouteTests
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
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(_provider))
            );
        _admin = await ApiClients.SetUpAsync(_factory, Cancel);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Cancel))!.Id;
        var adminId = (await accounts.GetServerAdminIdsAsync(Cancel)).Single();
        await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [users]), null, Cancel);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Cancel);

        _account = _provider
            .Account("acct")
            .WithFolder("Photos/Trips")
            .WithFile("Photos/b.jpg", "picture b")
            .WithFile("Photos/a.mp4", "0123456789")
            .WithFile("Photos/page.html", "<script>alert(1)</script>")
            .WithFile("Secret.txt", "outside the root");
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(
                new(FakeProvider.FakeKind, "My files", ConnectionLevel.Account, OwnerId: adminId, RootPath: "Photos"),
                new ConnectionGrant("acct", "acct@fake", "id:acct"),
                adminId,
                Cancel
            );
        _id = made.Value!.Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderIsListedInTheLibrarysShapeUnderTheConnectionsRoot()
    {
        var first = (await _admin.GetFromJsonAsync<ConnectionListing>(Browse("limit=2"), Json, Cancel))!;
        var next = (await _admin.GetFromJsonAsync<ConnectionListing>(Browse("offset=2&limit=2"), Json, Cancel))!;
        var trips = (await _admin.GetFromJsonAsync<ConnectionListing>(Browse("path=Trips"), Json, Cancel))!;

        var listing = first.Listing;
        CollectionAssert.AreEqual(new[] { "Trips", "a.mp4" }, listing.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual((4, 1, 3), (listing.Total, listing.DirectoryCount, listing.FileCount));
        Assert.AreEqual(ItemKind.Video, listing.Items[1].Kind);
        Assert.AreEqual(("My files", ""), (listing.Breadcrumbs[0].Name, listing.Breadcrumbs[0].Path));
        Assert.AreEqual((FolderLevel.Write, true, false), (first.Access, first.Sorted, first.HasMore));
        Assert.IsTrue(first.Capabilities.RangedReads);
        CollectionAssert.AreEqual(new[] { "b.jpg", "page.html" }, next.Listing.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual(("Trips", "", "Trips"), (trips.Listing.Name, trips.Listing.ParentPath, trips.Listing.Path));
    }

    [TestMethod]
    public async Task NothingOutsideTheRootNorAnotherAccountsConnectionIsReached()
    {
        foreach (var path in new[] { "..", "../Secret.txt", "Trips/../../Secret.txt", "a\\b" })
        {
            using var browse = await _admin.GetAsync(Browse($"path={Uri.EscapeDataString(path)}"), Cancel);
            Assert.AreEqual(HttpStatusCode.NotFound, browse.StatusCode, path);
        }
        using var raw = await _admin.GetAsync($"{Base}/files/raw/..%2FSecret.txt", Cancel);
        using var missing = await _admin.GetAsync(Browse("path=Nowhere"), Cancel);
        using var bobs = await _bob.GetAsync(Browse(""), Cancel);
        using var unknown = await _admin.GetAsync($"{V1}/connections/999/browse", Cancel);

        Assert.AreEqual(HttpStatusCode.NotFound, raw.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        // Bob has no connections.use; with it he still would not see the administrator's own connection.
        Assert.AreEqual(HttpStatusCode.Forbidden, bobs.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        // An encoded slash stays in the name (as in the library's routes): a file of that name inside the root.
        Assert.IsFalse(
            _account.Calls.Any(c => c.Contains("../", StringComparison.Ordinal) || c.EndsWith(" Secret.txt"))
        );
    }

    [TestMethod]
    public async Task AFileIsServedWholeOrInPartSandboxed()
    {
        using var whole = await _admin.GetAsync($"{Base}/files/raw/a.mp4", Cancel);
        using var part = await GetRangeAsync("a.mp4", "bytes=2-4");
        using var tail = await GetRangeAsync("a.mp4", "bytes=-3");
        using var past = await GetRangeAsync("a.mp4", "bytes=50-");
        using var page = await _admin.GetAsync($"{Base}/files/raw/page.html", Cancel);
        using var folder = await _admin.GetAsync($"{Base}/files/raw/Trips", Cancel);

        Assert.AreEqual("0123456789", await whole.Content.ReadAsStringAsync(Cancel));
        Assert.AreEqual("bytes", whole.Headers.AcceptRanges.Single());
        Assert.AreEqual(SecurityHeaders.FilePolicy, whole.Headers.GetValues("Content-Security-Policy").Single());
        Assert.AreEqual(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.AreEqual("234", await part.Content.ReadAsStringAsync(Cancel));
        Assert.AreEqual("bytes 2-4/10", part.Content.Headers.ContentRange!.ToString());
        Assert.AreEqual("789", await tail.Content.ReadAsStringAsync(Cancel));
        Assert.AreEqual(HttpStatusCode.RequestedRangeNotSatisfiable, past.StatusCode);
        // A page the browser could run is only ever downloaded.
        Assert.AreEqual("attachment", page.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual(HttpStatusCode.NotFound, folder.StatusCode);
    }

    [TestMethod]
    public async Task WithoutRangedReadsTheWholeFileIsServed()
    {
        _provider.Capabilities = _provider.Capabilities with { RangedReads = false };

        using var answer = await GetRangeAsync("a.mp4", "bytes=2-4");

        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode);
        Assert.AreEqual("0123456789", await answer.Content.ReadAsStringAsync(Cancel));
        Assert.IsEmpty(answer.Headers.AcceptRanges);
    }

    [TestMethod]
    public async Task AnEndedSignInMarksTheConnectionAndAnUnreachableProviderIsSaid()
    {
        _account.Failing = RemoteErrorKind.Unavailable;
        using var down = await _admin.GetAsync(Browse(""), Cancel);

        _account.Failing = RemoteErrorKind.CredentialsEnded;
        using var ended = await _admin.GetAsync(Browse(""), Cancel);
        _account.Failing = null;
        using var after = await _admin.GetAsync(Browse(""), Cancel);

        Assert.AreEqual(HttpStatusCode.BadGateway, down.StatusCode);
        Assert.AreEqual(ConnectionProblems.UnavailableType, await TypeOfAsync(down));
        Assert.AreEqual(HttpStatusCode.Conflict, ended.StatusCode);
        Assert.AreEqual(ConnectionProblems.NeedsReconnectingType, await TypeOfAsync(ended));
        // Marked: nobody opens it until someone signs in again.
        Assert.AreEqual(ConnectionProblems.NeedsReconnectingType, await TypeOfAsync(after));
        var info = await _factory.Services.GetRequiredService<ConnectionService>().FindAsync(_id, Cancel);
        Assert.AreEqual(ConnectionState.NeedsReconnecting, info!.State);
    }

    [TestMethod]
    public async Task WhileTheProviderAsksToWaitNobodyCallsIt()
    {
        _account.Failing = RemoteErrorKind.RateLimited;
        _account.RetryAfter = TimeSpan.FromMinutes(2);
        using var first = await _admin.GetAsync(Browse(""), Cancel);
        _account.Failing = null;
        var calls = _account.Calls.Count;

        using var next = await _admin.GetAsync(Browse("path=Trips"), Cancel);
        using var read = await _admin.GetAsync($"{Base}/files/raw/a.mp4", Cancel);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.AreEqual(TimeSpan.FromMinutes(2), first.Headers.RetryAfter!.Delta);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, next.StatusCode);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, read.StatusCode);
        Assert.IsGreaterThan(TimeSpan.FromSeconds(60), next.Headers.RetryAfter!.Delta!.Value);
        Assert.AreEqual(ConnectionProblems.RateLimitedType, await TypeOfAsync(read));
        Assert.HasCount(calls, _account.Calls, "the provider was not called during its wait");
    }

    [TestMethod]
    public async Task AThumbnailIsAskedOnlyOfAProviderThatMakesThem()
    {
        using var none = await _admin.GetAsync($"{Base}/thumbnails/b.jpg", Cancel);

        Assert.AreEqual(HttpStatusCode.NotFound, none.StatusCode);
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("thumbnail", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SeveralItemsComeAsOneZipFromTheProviderOnce()
    {
        _account.WithFile("Photos/Trips/day1.txt", "first day");

        using var refused = await _admin.PostAsJsonAsync(
            $"{Base}/archive",
            new ArchiveRequest(["../Secret.txt"]),
            Cancel
        );
        using var created = await _admin.PostAsJsonAsync(
            $"{Base}/archive",
            new ArchiveRequest(["Trips", "b.jpg"]),
            Cancel
        );
        var link = (await created.Content.ReadFromJsonAsync<ArchiveLink>(Json, Cancel))!;
        var url = "/" + link.Url.TrimStart('/');
        using var library = await _admin.GetAsync(url.Replace($"connections/{_id}/archive", "files/archive"), Cancel);
        using var download = await _admin.GetAsync(url, Cancel);
        using var again = await _admin.GetAsync(url, Cancel);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual("My files.zip", link.FileName);
        // The link is the connection's: the library's route doesn't take it (nor use it up); it works once.
        Assert.AreEqual(HttpStatusCode.NotFound, library.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);
        await using var stream = await download.Content.ReadAsStreamAsync(Cancel);
        using var archive = new System.IO.Compression.ZipArchive(stream);
        CollectionAssert.AreEquivalent(
            new[] { "Trips/", "Trips/day1.txt", "b.jpg" },
            archive.Entries.Select(e => e.FullName).ToArray()
        );
        using var reader = new StreamReader(archive.GetEntry("Trips/day1.txt")!.Open());
        Assert.AreEqual("first day", await reader.ReadToEndAsync(Cancel));
    }

    [TestMethod]
    public async Task AnItemsPropertiesComeFromTheProvider()
    {
        var date = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        _account.WithFile("Photos/Trips/day1.txt", "first day", date);

        var file = (
            await _admin.GetFromJsonAsync<ConnectionItemProperties>(Properties("Trips/day1.txt"), Json, Cancel)
        )!;
        var folder = (await _admin.GetFromJsonAsync<ConnectionItemProperties>(Properties("Trips"), Json, Cancel))!;
        var root = (await _admin.GetFromJsonAsync<ConnectionItemProperties>($"{Base}/files/properties", Json, Cancel))!;

        Assert.AreEqual(("day1.txt", "Trips/day1.txt", 9L), (file.Item.Name, file.Item.Path, file.Item.Size));
        Assert.AreEqual(new DateTimeOffset(date), file.Item.Modified);
        Assert.AreEqual((FakeProvider.FakeKind, "acct@fake", "fake"), (file.Kind, file.Account, file.HashName));
        Assert.IsFalse(string.IsNullOrEmpty(file.ContentHash));
        Assert.IsTrue(file.Downloadable);
        Assert.AreEqual((ItemKind.Directory, null), (folder.Item.Kind, folder.ContentHash));
        Assert.AreEqual(("My files", "", ItemKind.Directory), (root.Item.Name, root.Item.Path, root.Item.Kind));
    }

    [TestMethod]
    public async Task PropertiesStayInsideTheRootAndTheProviderAccountWithItsOwner()
    {
        var shared = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(
                new(FakeProvider.FakeKind, "Shared", ConnectionLevel.Library, TenantId: await TenantAsync()),
                new ConnectionGrant("acct", "acct@fake", "id:acct"),
                null,
                Cancel
            );

        foreach (var path in new[] { "..", "../Secret.txt", "Nowhere" })
        {
            using var answer = await _admin.GetAsync(Properties(path), Cancel);
            Assert.AreEqual(HttpStatusCode.NotFound, answer.StatusCode, path);
        }
        using var bobs = await _bob.GetAsync(Properties("b.jpg"), Cancel);
        using var unknown = await _admin.GetAsync($"{V1}/connections/999/files/properties?path=b.jpg", Cancel);
        var library = (
            await _admin.GetFromJsonAsync<ConnectionItemProperties>(
                $"{V1}/connections/{shared.Value!.Id}/files/properties?path=Secret.txt",
                Json,
                Cancel
            )
        )!;

        Assert.AreEqual(HttpStatusCode.Forbidden, bobs.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        // A library's connection never tells whose provider account it reads.
        Assert.AreEqual(("Secret.txt", null), (library.Item.Name, library.Account));
    }

    private string Base => $"{V1}/connections/{_id}";

    private string Properties(string path) => $"{Base}/files/properties?path={Uri.EscapeDataString(path)}";

    private async Task<int> TenantAsync() =>
        (await _factory.Services.GetRequiredService<AccessService>().GetTenantsAsync(Cancel)).Single().Id;

    private string Browse(string query) => $"{Base}/browse?{query}";

    private async Task<HttpResponseMessage> GetRangeAsync(string path, string range)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/files/raw/{path}");
        request.Headers.Range = RangeHeaderValue.Parse(range);
        return await _admin.SendAsync(request, Cancel);
    }

    private async Task<string?> TypeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancel))!.Type;
}
