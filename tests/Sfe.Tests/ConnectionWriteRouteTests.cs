using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// A connection's files changed through <c>api/connections/{id}/files/folders</c>, <c>rename</c>, <c>delete</c>
/// and <c>transfer</c>: what reaches the provider, the answers of the library's routes, names refused, paths
/// leaving the root, a connection only read or not usable, and listings showing the change at once.
/// </summary>
[TestClass]
public sealed class ConnectionWriteRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private FakeAccount _account = null!;
    private int _adminId;
    private int _bobId;
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
        _adminId = (await accounts.GetServerAdminIdsAsync(Cancel)).Single();
        var bob = await accounts.CreateUserAsync(
            new("bob", Password: "bob-password-1", GroupIds: [users]),
            null,
            Cancel
        );
        _bobId = bob.Value!.Id;

        _account = _provider
            .Account("acct")
            .WithFolder("Photos/Trips")
            .WithFile("Photos/b.jpg", "picture b")
            .WithFile("Photos/a.txt", "text a")
            .WithFile("Secret.txt", "outside the root");
        _id = await CreateConnectionAsync(_adminId);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderIsCreatedAndTheListingShowsItAtOnce()
    {
        var before = await NamesAsync("");
        using var created = await PostAsync("folders", new CreateFolderRequest("Trips", "Day 1"));
        using var root = await PostAsync("folders", new CreateFolderRequest("", "New"));
        var after = await NamesAsync("");

        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<LibraryItem>(Json, Cancel))!;
        Assert.AreEqual(("Day 1", "Trips/Day 1", ItemKind.Directory), (item.Name, item.Path, item.Kind));
        Assert.IsTrue(_account.Exists("Photos/Trips/Day 1"));
        Assert.AreEqual(HttpStatusCode.OK, root.StatusCode);
        CollectionAssert.DoesNotContain(before, "New");
        CollectionAssert.Contains(after, "New");
    }

    [TestMethod]
    public async Task NamesTakenRefusedOrMissingFoldersAreAnswered()
    {
        using var taken = await PostAsync("folders", new CreateFolderRequest("", "trips"));
        using var invalid = await PostAsync("folders", new CreateFolderRequest("", "bad/name"));
        using var dot = await PostAsync("rename", new RenameRequest("a.txt", "a."));
        using var missing = await PostAsync("folders", new CreateFolderRequest("Nowhere", "x"));
        _account.Failing = RemoteErrorKind.InvalidName;
        using var refused = await PostAsync("folders", new CreateFolderRequest("", "Fine"));
        _account.Failing = null;

        Assert.AreEqual(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.AreEqual(FileEndpoints.ExistsType, await TypeOfAsync(taken));
        foreach (var answer in new[] { invalid, dot, refused })
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, answer.StatusCode);
            var problem = (await answer.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Cancel))!;
            Assert.IsTrue(problem.Errors.ContainsKey("name"));
        }
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.IsFalse(_account.Calls.Any(c => c.Contains("bad", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AnItemIsRenamedInItsFolder()
    {
        using var renamed = await PostAsync("rename", new RenameRequest("a.txt", "notes.txt"));
        using var taken = await PostAsync("rename", new RenameRequest("notes.txt", "b.jpg"));
        using var missing = await PostAsync("rename", new RenameRequest("gone.txt", "x.txt"));
        using var root = await PostAsync("rename", new RenameRequest("", "x"));

        var item = (await renamed.Content.ReadFromJsonAsync<LibraryItem>(Json, Cancel))!;
        Assert.AreEqual(("notes.txt", "notes.txt"), (item.Name, item.Path));
        Assert.AreEqual("text a", _account.TextOf("Photos/notes.txt"));
        Assert.IsFalse(_account.Exists("Photos/a.txt"));
        Assert.AreEqual(FileEndpoints.ExistsType, await TypeOfAsync(taken));
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, root.StatusCode);
    }

    [TestMethod]
    public async Task ItemsAreDeletedIntoTheProvidersTrash()
    {
        var before = await NamesAsync("");
        using var answer = await PostAsync(
            "delete",
            new DeleteRequest(["b.jpg", "Trips", "gone.txt", "", "../Secret.txt"])
        );
        var results = (await answer.Content.ReadFromJsonAsync<DeleteResults>(Json, Cancel))!;

        CollectionAssert.AreEqual(
            new[]
            {
                FileOperationStatus.Done,
                FileOperationStatus.Done,
                FileOperationStatus.NotFound,
                FileOperationStatus.NotFound,
                FileOperationStatus.NotFound,
            },
            results.Items.Select(i => i.Status).ToArray()
        );
        Assert.IsFalse(results.Permanent);
        Assert.IsTrue(results.Items.All(i => i.TrashId is null));
        CollectionAssert.AreEqual(new[] { "Photos/b.jpg", "Photos/Trips" }, _account.Trash);
        Assert.IsTrue(_account.Exists("Secret.txt"));
        CollectionAssert.Contains(before, "b.jpg");
        CollectionAssert.AreEqual(new[] { "a.txt" }, await NamesAsync(""));
    }

    [TestMethod]
    public async Task ItemsAreMovedAndCopiedWithTheConflictChoice()
    {
        _account.WithFile("Photos/Trips/b.jpg", "older b");

        var moved = await TransferAsync(new("move", ["a.txt"], "Trips"));
        var failed = await TransferAsync(new("copy", ["b.jpg"], "Trips"));
        var both = await TransferAsync(new("copy", ["b.jpg"], "Trips", "keepBoth"));
        var replaced = await TransferAsync(new("copy", ["b.jpg"], "Trips", "replace"));
        var same = await TransferAsync(new("move", ["b.jpg"], ""));

        Assert.AreEqual((FileOperationStatus.Done, "Trips/a.txt"), (moved[0].Status, moved[0].Item!.Path));
        Assert.IsFalse(_account.Exists("Photos/a.txt"));
        Assert.AreEqual(FileOperationStatus.Exists, failed[0].Status);
        Assert.AreEqual((FileOperationStatus.Done, "Trips/b (1).jpg"), (both[0].Status, both[0].Item!.Path));
        Assert.AreEqual(FileOperationStatus.Done, replaced[0].Status);
        Assert.AreEqual("picture b", _account.TextOf("Photos/Trips/b.jpg"));
        Assert.AreEqual(FileOperationStatus.Skipped, same[0].Status);
        CollectionAssert.AreEquivalent(new[] { "a.txt", "b (1).jpg", "b.jpg" }, await NamesAsync("Trips"));
    }

    [TestMethod]
    public async Task TransfersRefuseFoldersIntoThemselvesPathsOutsideAndOtherWords()
    {
        var into = await TransferAsync(new("move", ["Trips", "../Secret.txt", "gone.txt"], "Trips"));
        using var target = await PostAsync("transfer", new ConnectionTransferRequest("copy", ["b.jpg"], "../x"));
        using var kind = await PostAsync("transfer", new ConnectionTransferRequest("link", ["b.jpg"], ""));
        using var conflict = await PostAsync("transfer", new ConnectionTransferRequest("copy", ["b.jpg"], "", "ask"));
        _provider.Capabilities = _provider.Capabilities with { ServerSideCopy = false };
        using var unsupported = await PostAsync("transfer", new ConnectionTransferRequest("copy", ["b.jpg"], "Trips"));

        CollectionAssert.AreEqual(
            new[] { FileOperationStatus.Invalid, FileOperationStatus.NotFound, FileOperationStatus.NotFound },
            into.Select(i => i.Status).ToArray()
        );
        Assert.AreEqual(HttpStatusCode.NotFound, target.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, kind.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, conflict.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, unsupported.StatusCode);
        Assert.AreEqual(ConnectionProblems.NotSupportedType, await TypeOfAsync(unsupported));
        Assert.IsTrue(_account.Exists("Secret.txt"));
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("move Trips", StringComparison.Ordinal)));
        Assert.IsFalse(_account.Calls.Any(c => c.Contains("..", StringComparison.Ordinal) || c.Contains("Secret")));
    }

    [TestMethod]
    public async Task AConnectionOnlyReadIsNotChangedAndAnotherAccountsIsNotFound()
    {
        var connections = _factory.Services.GetRequiredService<ConnectionService>();
        await connections.UpdateAsync(_id, new ConnectionChange(ReadOnly: true), _adminId, Cancel);
        var bobs = await CreateConnectionAsync(_bobId);

        using var folder = await PostAsync("folders", new CreateFolderRequest("", "New"));
        using var rename = await PostAsync("rename", new RenameRequest("a.txt", "x.txt"));
        using var delete = await PostAsync("delete", new DeleteRequest(["a.txt"]));
        using var move = await PostAsync("transfer", new ConnectionTransferRequest("move", ["a.txt"], "Trips"));
        using var browse = await _admin.GetAsync($"{V1}/connections/{_id}/browse", Cancel);
        using var other = await _admin.PostAsJsonAsync(
            $"{V1}/connections/{bobs}/files/folders",
            new CreateFolderRequest("", "New"),
            Cancel
        );

        foreach (var answer in new[] { folder, rename, delete, move })
            Assert.AreEqual(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, browse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("folder", StringComparison.Ordinal)));
        Assert.IsTrue(_account.Exists("Photos/a.txt"));
    }

    private async Task<int> CreateConnectionAsync(int ownerId)
    {
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(
                new(
                    FakeProvider.FakeKind,
                    $"Files {ownerId}",
                    ConnectionLevel.Account,
                    OwnerId: ownerId,
                    RootPath: "Photos"
                ),
                new ConnectionGrant("acct", "acct@fake", "id:acct"),
                ownerId,
                Cancel
            );
        return made.Value!.Id;
    }

    private Task<HttpResponseMessage> PostAsync<T>(string route, T body) =>
        _admin.PostAsJsonAsync($"{V1}/connections/{_id}/files/{route}", body, Json, Cancel);

    private async Task<IReadOnlyList<ConnectionTransferItem>> TransferAsync(ConnectionTransferRequest request)
    {
        using var answer = await PostAsync("transfer", request);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode);
        return (await answer.Content.ReadFromJsonAsync<ConnectionTransferResults>(Json, Cancel))!.Items;
    }

    private async Task<string[]> NamesAsync(string path)
    {
        var url = $"{V1}/connections/{_id}/browse?path={Uri.EscapeDataString(path)}";
        var listing = (await _admin.GetFromJsonAsync<ConnectionListing>(url, Json, Cancel))!;
        return [.. listing.Listing.Items.Select(i => i.Name)];
    }

    private async Task<string?> TypeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancel))!.Type;
}
