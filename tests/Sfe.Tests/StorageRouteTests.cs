using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>api/storage</c> and <c>api/connections/{id}/storage</c>: the space of the drive of a library folder (through a
/// junction inside the library: the volume it leads to) and of a connection's account, kept a while, not reported when the provider can't tell, and only for who may use it.
/// </summary>
[TestClass]
public sealed class StorageRouteTests
{
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

        _account = _provider.Account("acct").WithFile("a.txt", "0123456789");
        _account.TotalSpace = 1000;
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(
                new(FakeProvider.FakeKind, "My files", ConnectionLevel.Account, OwnerId: adminId),
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
        // TestLibrary can't delete through a junction: the link first, alone.
        if (new DirectoryInfo(Path.Combine(_library.Root, "Movies")) is { LinkTarget: not null } link)
            link.Delete();
        _library.Dispose();
    }

    [TestMethod]
    public async Task TheLibrarysDriveIsMeasuredWithoutItsPath()
    {
        using var response = await _bob.GetAsync($"{V1}/storage", Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        var usage = (await response.Content.ReadFromJsonAsync<StorageUsage>(Cancel))!;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsTrue(usage.Reported);
        Assert.IsGreaterThan(0L, usage.Total!.Value);
        Assert.AreEqual(usage.Total, usage.Used + usage.Free);
        Assert.DoesNotContain(Path.GetFileName(_library.Root), text);
    }

    [TestMethod]
    public async Task AFolderBehindAJunctionIsMeasuredOnItsVolume()
    {
        Directory.CreateDirectory(Path.Combine(_library.Root, "real", "deep"));
        Directory.CreateDirectory(Path.Combine(_library.Root, "plain"));
        var link = Path.Combine(_library.Root, "Movies");
        if (!DiskSpaceTests.TryLinkFolder(link, Path.Combine(_library.Root, "real")))
            Assert.Inconclusive("A junction or a link could not be made here.");
        var cache = _factory.Services.GetRequiredService<StorageUsageCache>();

        Assert.IsTrue((await LibraryStorageAsync("Movies/deep")).Reported);
        Assert.IsNotNull(cache.Find("library:" + link), "Kept for the junction, for every folder under it");
        Assert.IsNull(cache.Find("library:" + RootFolder), "The library's folder not measured");

        Assert.IsTrue((await LibraryStorageAsync("plain")).Reported);
        Assert.IsNotNull(cache.Find("library:" + RootFolder));
    }

    [TestMethod]
    public async Task AFolderNotThereOrOutsideIsMeasuredAsTheLibrary()
    {
        var cache = _factory.Services.GetRequiredService<StorageUsageCache>();

        Assert.IsTrue((await LibraryStorageAsync("../..")).Reported);
        Assert.IsTrue((await LibraryStorageAsync("not/there")).Reported);
        Assert.IsNotNull(cache.Find("library:" + RootFolder));
    }

    [TestMethod]
    public void AFolderOfNoDriveIsNotReported() => Assert.AreEqual(StorageUsage.NotReported, DiskSpace.Of("\0"));

    [TestMethod]
    public async Task AConnectionsAccountIsMeasuredAndKeptAWhile()
    {
        var first = await StorageAsync(_admin);
        _account.TotalSpace = 5000;
        var second = await StorageAsync(_admin);

        Assert.AreEqual(new StorageUsage(true, 10, 1000, 990), first);
        Assert.AreEqual(first, second, "Read again only after a few minutes");
        Assert.AreEqual(1, _account.Calls.Count(c => c == "space"));
    }

    [TestMethod]
    public async Task AProviderThatCannotTellIsNotReported()
    {
        _provider.Capabilities = _provider.Capabilities with { FreeSpace = false };

        Assert.AreEqual(StorageUsage.NotReported, await StorageAsync(_admin));
    }

    [TestMethod]
    public async Task AConnectionIsMeasuredOnlyForWhoMayUseIt()
    {
        using var bobs = await _bob.GetAsync($"{V1}/connections/{_id}/storage", Cancel);
        using var unknown = await _admin.GetAsync($"{V1}/connections/999/storage", Cancel);

        // Bob has no connections.use; with it he still would not see the administrator's own connection.
        Assert.AreEqual(HttpStatusCode.Forbidden, bobs.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.IsEmpty(_account.Calls);
    }

    private string RootFolder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(_library.Root));

    private async Task<StorageUsage> LibraryStorageAsync(string path)
    {
        using var response = await _bob.GetAsync($"{V1}/storage?path={Uri.EscapeDataString(path)}", Cancel);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<StorageUsage>(Cancel))!;
    }

    private async Task<StorageUsage> StorageAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"{V1}/connections/{_id}/storage", Cancel);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<StorageUsage>(Cancel))!;
    }
}
