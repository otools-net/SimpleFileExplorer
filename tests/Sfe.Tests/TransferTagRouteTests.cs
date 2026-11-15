using System.Net;
using System.Net.Http.Json;
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
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// A file moved or copied between the library and a connection by a transfer keeps its tags: given to the file
/// written (the connection's row is made by the change SFE tells as it writes), and for a move taken off the source.
/// </summary>
[TestClass]
public sealed class TransferTagRouteTests
{
    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private FakeAccount _account = null!;
    private int _tenant;
    private int _id;
    private int _cat;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private string Connection => $"{V1}/connections/{_id}";

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
        var adminId = (await accounts.GetServerAdminIdsAsync(Token)).Single();
        _account = _provider.Account("acct").WithFile("Photos/a.txt", "text a");
        var draft = new ConnectionDraft(
            FakeProvider.FakeKind,
            "Files",
            ConnectionLevel.Account,
            OwnerId: adminId,
            RootPath: "Photos"
        );
        var grant = new ConnectionGrant("acct", "acct@fake", "id:acct");
        _id = (
            await _factory.Services.GetRequiredService<ConnectionService>().CreateAsync(draft, grant, adminId, Token)
        )
            .Value!
            .Id;
        await IndexAsync();
        var tags = await LibraryAsync<TagService>();
        _cat = (await tags.CreateAsync(new TagInput("Cat"), Token)).Tag!.Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ACopyIntoAConnectionKeepsTheTagsOnBothSides()
    {
        await TagAsync($"{V1}/file-tags", "RFile1.txt");

        await TransferAsync(new("copy", new TransferSide(null, ["RFile1.txt"]), new TransferTarget(_id, "")));

        Assert.AreEqual("Cat", (await TagsAsync($"{Connection}/file-tags", "RFile1.txt")).Single().Name);
        Assert.AreEqual("Cat", (await TagsAsync($"{V1}/file-tags", "RFile1.txt")).Single().Name);
    }

    [TestMethod]
    public async Task AMoveOutOfAConnectionGivesTheTagsToTheLibrarysFileAndTakesThemOffTheSource()
    {
        await TagAsync($"{Connection}/file-tags", "a.txt");

        await TransferAsync(new("move", new TransferSide(_id, ["a.txt"]), new TransferTarget(null, "ChildDir")));

        Assert.AreEqual("Cat", (await TagsAsync($"{V1}/file-tags", "ChildDir/a.txt")).Single().Name);
        Assert.IsFalse(_account.Exists("Photos/a.txt"));
        var factory = await LibraryAsync<IDbContextFactory<SfeDbContext>>();
        await using var db = await factory.CreateDbContextAsync(Token);
        Assert.AreEqual(0, await db.RemoteFileTags.CountAsync(Token), "the source's row keeps none");
    }

    [TestMethod]
    public async Task AMoveIntoAConnectionKeepsTheTags()
    {
        await TagAsync($"{V1}/file-tags", "ChildDir/AFile2");

        await TransferAsync(new("move", new TransferSide(null, ["ChildDir"]), new TransferTarget(_id, "")));

        Assert.AreEqual("Cat", (await TagsAsync($"{Connection}/file-tags", "ChildDir/AFile2")).Single().Name);
        Assert.IsTrue(_account.Exists("Photos/ChildDir/AFile2"));
    }

    private async Task TagAsync(string route, string path)
    {
        using var answer = await _admin.PostAsJsonAsync(
            route,
            new FileTagChange([path], [_cat]),
            RuledConnection.Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Token));
    }

    private async Task<IReadOnlyList<TagSummary>> TagsAsync(string route, string path)
    {
        var url = $"{route}?path={Uri.EscapeDataString(path)}";
        return (await _admin.GetFromJsonAsync<FileTags>(url, RuledConnection.Json, Token))!.Tags;
    }

    private async Task TransferAsync(TransferRequest request)
    {
        using var started = await _admin.PostAsJsonAsync(
            $"{V1}/files/jobs/transfer",
            request,
            RuledConnection.Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.Accepted, started.StatusCode, await started.Content.ReadAsStringAsync(Token));
        var id = (await started.Content.ReadFromJsonAsync<FileJobView>(RuledConnection.Json, Token))!.Id;
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var url = $"{V1}/files/jobs/{id}";
            var job = (await _admin.GetFromJsonAsync<FileJobView>(url, RuledConnection.Json, Token))!;
            if (job.State == FileJobState.Completed)
            {
                Assert.IsTrue(job.Results.All(r => r.Status == FileOperationStatus.Done), job.Results[0].Message);
                return;
            }
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Job {id} is {job.State}");
            await Task.Delay(20, Token);
        }
    }

    private async Task<T> LibraryAsync<T>()
        where T : notnull
    {
        var host = await _factory.Services.GetRequiredService<TenantRegistry>().GetAsync(_tenant, Token);
        return host!.Services.GetRequiredService<T>();
    }

    private async Task IndexAsync()
    {
        var indexes = await LibraryAsync<ConnectionIndexes>();
        Assert.IsTrue(await indexes.StartAsync(_id, Otools.Net.Sfe.Core.Tasks.TaskTrigger.Manual, null));
        var until = DateTime.UtcNow.AddSeconds(10);
        while ((await indexes.StatusAsync(_id, Token)) is { Running: true } or { LastSync: null })
        {
            if (DateTime.UtcNow > until)
                throw new AssertFailedException("The connection's index was not synced.");
            await Task.Delay(20, Token);
        }
    }
}
