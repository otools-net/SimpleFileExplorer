using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>The sidebar's quick links: each account's own folders and files, in its order, through HTTP.</summary>
[TestClass]
public sealed class QuickLinkTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const string Url = "/api/me/quick-links";

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AddsFoldersAndFilesInOrderWithTheirKinds()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);

        await AddAsync(admin, ["Media", "Media/photo.jpg"]);
        var links = await AddAsync(admin, ["", $"SpecialDir/{TestLibrary.SpecialFolder}", "Media/Holiday 2020.mp4", "Media"]);

        CollectionAssert.AreEqual(new[] { "Media", "Media/photo.jpg", "", $"SpecialDir/{TestLibrary.SpecialFolder}", "Media/Holiday 2020.mp4" },
            links.Select(l => l.Path).ToArray(), "New links go at the end; one already there stays where it is");
        CollectionAssert.AreEqual(new[] { ItemKind.Directory, ItemKind.Image, ItemKind.Directory, ItemKind.Directory, ItemKind.Video },
            links.Select(l => l.Kind).ToArray());
        Assert.AreEqual("Root", links[2].Name, "The root is named as the library's folder");
        Assert.AreEqual("photo.jpg", links[1].Name);
        Assert.AreEqual("jpg", links[1].Extension);
        Assert.IsTrue(links.All(l => l.Exists));
        CollectionAssert.AreEqual(links.Select(l => l.Path).ToArray(), (await ListAsync(admin)).Select(l => l.Path).ToArray());
    }

    [TestMethod]
    [DataRow(0, new[] { "Media/photo.jpg", "A", "B", "C" }, DisplayName = "to the top")]
    [DataRow(2, new[] { "A", "B", "Media/photo.jpg", "C" }, DisplayName = "before the third")]
    [DataRow(9, new[] { "A", "B", "C", "Media/photo.jpg" }, DisplayName = "past the end")]
    public async Task AddsAtAPosition(int position, string[] expected)
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        foreach (var folder in new[] { "A", "B", "C" })
            Directory.CreateDirectory(_library.FullPath(folder));
        await AddAsync(admin, ["A", "B", "C"]);

        var links = await AddAsync(admin, ["Media/photo.jpg"], position);

        CollectionAssert.AreEqual(expected, links.Select(l => l.Path).ToArray());
    }

    [TestMethod]
    [DataRow("C", 0, new[] { "C", "A", "B", "D" }, DisplayName = "up to the top")]
    [DataRow("A", 2, new[] { "B", "A", "C", "D" }, DisplayName = "down one, dropped before the third")]
    [DataRow("A", 1, new[] { "A", "B", "C", "D" }, DisplayName = "onto the next: stays")]
    [DataRow("B", 1, new[] { "A", "B", "C", "D" }, DisplayName = "onto itself: stays")]
    [DataRow("A", 4, new[] { "B", "C", "D", "A" }, DisplayName = "to the end")]
    [DataRow("D", 2, new[] { "A", "B", "D", "C" }, DisplayName = "up one")]
    public async Task MovesALinkBeforeAnother(string moved, int position, string[] expected)
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        foreach (var folder in new[] { "A", "B", "C", "D" })
            Directory.CreateDirectory(_library.FullPath(folder));
        await AddAsync(admin, ["A", "B", "C", "D"]);

        var links = await AddAsync(admin, [moved], position);

        CollectionAssert.AreEqual(expected, links.Select(l => l.Path).ToArray());
    }

    [TestMethod]
    public async Task RemovesALinkButNotTheFolder()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        await AddAsync(admin, ["Media", "ChildDir", "RFile1.txt"]);

        using var response = await admin.DeleteAsync($"{Url}?path=ChildDir", TestContext.CancellationToken);
        var links = (await response.Content.ReadFromJsonAsync<List<QuickLinkView>>(Json, TestContext.CancellationToken))!;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(new[] { "Media", "RFile1.txt" }, links.Select(l => l.Path).ToArray());
        Assert.IsTrue(Directory.Exists(_library.FullPath("ChildDir")));
    }

    [TestMethod]
    public async Task AGoneTargetStaysListedAsMissingAndCanStillMove()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        await AddAsync(admin, ["Media", "RFile1.txt"]);
        File.Delete(_library.FullPath("RFile1.txt"));

        var links = await ListAsync(admin);
        var moved = await AddAsync(admin, ["RFile1.txt"], 0);

        Assert.IsFalse(links.Single(l => l.Path == "RFile1.txt").Exists);
        Assert.AreEqual(ItemKind.File, links.Single(l => l.Path == "RFile1.txt").Kind, "Its kind is the one it had");
        Assert.IsTrue(links.Single(l => l.Path == "Media").Exists);
        CollectionAssert.AreEqual(new[] { "RFile1.txt", "Media" }, moved.Select(l => l.Path).ToArray());
    }

    [TestMethod]
    [DataRow("Nowhere", DisplayName = "missing")]
    [DataRow("../outside", DisplayName = "outside the root")]
    [DataRow("Media/../../outside", DisplayName = "escaping through a folder")]
    public async Task RefusesWhatIsNotInTheLibrary(string path)
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);

        using var response = await admin.PostAsJsonAsync(Url, new QuickLinkAdd(["Media", path], null), Json, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsEmpty(await ListAsync(admin), "Nothing is added when one path is refused");
    }

    [TestMethod]
    public async Task RefusesMoreThanTheLimit()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        var folders = Enumerable.Range(0, QuickLinkService.MaxLinks).Select(i => $"Many/{i:000}").ToList();
        foreach (var folder in folders)
            Directory.CreateDirectory(_library.FullPath(folder));
        await AddAsync(admin, folders);

        using var refused = await admin.PostAsJsonAsync(Url, new QuickLinkAdd(["Media"], null), Json, TestContext.CancellationToken);
        var moved = await AddAsync(admin, [folders[^1]], 0);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(folders[^1], moved[0].Path, "A full list still reorders");
        Assert.HasCount(QuickLinkService.MaxLinks, moved);
    }

    [TestMethod]
    public async Task EachAccountHasItsOwnLinks()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var users = (await GetAsync<List<GroupInfo>>(admin, "/api/access/groups")).Single(g => g.BuiltIn == BuiltInGroup.Users);
        using (var created = await admin.PostAsJsonAsync("/api/access/users",
                   new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);
        _disposables.Add(alice);

        await AddAsync(admin, ["Media"]);
        await AddAsync(alice, ["ChildDir"]);

        CollectionAssert.AreEqual(new[] { "Media" }, (await ListAsync(admin)).Select(l => l.Path).ToArray());
        CollectionAssert.AreEqual(new[] { "ChildDir" }, (await ListAsync(alice)).Select(l => l.Path).ToArray());
    }

    [TestMethod]
    public async Task VisitorsWithoutAnAccountHaveNone()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        using (var open = await admin.PutAsJsonAsync("/api/access/tenant/guest-access", new { guestAccess = true }, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, open.StatusCode);
        var visitor = ApiClients.Create(factory);
        _disposables.Add(visitor);

        using var list = await visitor.GetAsync(Url, TestContext.CancellationToken);
        using var add = await visitor.PostAsJsonAsync(Url, new QuickLinkAdd(["Media"], null), Json, TestContext.CancellationToken);

        Assert.IsFalse(RightCatalog.Find(Rights.QuickLinks)!.GuestAllowed);
        Assert.IsTrue(list.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, list.StatusCode.ToString());
        Assert.IsTrue(add.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, add.StatusCode.ToString());
    }

    /// <summary>
    /// An install of 1.41.0 has no QuickLinks table and its built-in groups lack the right: the update adds the table
    /// to the SQLite file and gives the right to the Administrators and Users groups.
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTableAndTheRight()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        await using (var connection = new SqliteConnection(_library.ConnectionString))
        {
            await connection.OpenAsync(TestContext.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DROP TABLE "QuickLinks";
                DELETE FROM "GroupRights" WHERE "Right" = '{Rights.QuickLinks}';
                UPDATE "Tenants" SET "RightsRevision" = 2;
                UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.41.0' WHERE "Key" = '{ServerProperty.VersionKey}';
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            await command.ExecuteNonQueryAsync(TestContext.CancellationToken);
        }

        var updated = Factory();
        await ApiClients.MigrateAsync(updated, TestContext.CancellationToken);
        var admin = await ApiClients.SignInAsync(updated, ApiClients.AdminName, ApiClients.AdminPassword, TestContext.CancellationToken);
        _disposables.Add(admin);
        var links = await AddAsync(admin, ["Media"]);
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");

        CollectionAssert.AreEqual(new[] { "Media" }, links.Select(l => l.Path).ToArray());
        Assert.Contains(Rights.QuickLinks, groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Rights);
        Assert.Contains(Rights.QuickLinks, groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Rights);
        Assert.DoesNotContain(Rights.QuickLinks, groups.Single(g => g.BuiltIn == BuiltInGroup.Guests).Rights);
    }

    private WebApplicationFactory<Program> Factory()
    {
        var factory = ApiClients.CreateFactory(_library);
        _disposables.Add(factory);
        return factory;
    }

    private async Task<List<QuickLinkView>> AddAsync(HttpClient client, IReadOnlyList<string> paths, int? position = null)
    {
        using var response = await client.PostAsJsonAsync(Url, new QuickLinkAdd(paths, position), Json, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<List<QuickLinkView>>(Json, TestContext.CancellationToken))!;
    }

    private Task<List<QuickLinkView>> ListAsync(HttpClient client) => GetAsync<List<QuickLinkView>>(client, Url);

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(TestContext.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }
}
