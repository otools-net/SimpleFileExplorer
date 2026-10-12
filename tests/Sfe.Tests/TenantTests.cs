using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Api.Endpoints;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Several libraries (tenants) on one server: each its own root, index, tags and members.</summary>
[TestClass]
public sealed class TenantTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
    public async Task LibrariesHaveTheirOwnIndexAndTags()
    {
        var admin = await AdminAsync(Factory());
        var media = await CreateLibraryAsync(admin, "Media files", _library.FullPath("Media"));

        await SwitchAsync(admin, media.Slug);
        var config = await GetAsync<ClientConfig>(admin, "/api/config");
        await ScanAsync(admin);
        var inMedia = await GetAsync<SearchResults>(admin, "/api/search?q=photo");
        using (var tag = await admin.PostAsJsonAsync("/api/tags", new TagInput("Only in media"), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, tag.StatusCode);
        await SwitchAsync(admin, AccessService.DefaultTenantSlug);
        var inFirst = await GetAsync<SearchResults>(admin, "/api/search?q=photo");
        var tags = await GetAsync<TagSearchResults>(admin, "/api/tags?q=Only");

        Assert.AreEqual("media-files", media.Slug);
        Assert.AreEqual("Media", config.RootName);
        Assert.AreEqual("Media files", config.Tenant?.Name);
        Assert.AreEqual("photo.jpg", inMedia.Items.Single().Path, "Paths are relative to the library's own root");
        Assert.IsEmpty(inFirst.Items, "The first library's index was not scanned");
        Assert.IsEmpty(tags.Matches);
        Assert.IsTrue(File.Exists(Path.Combine(_library.BasePath, "tenants", "media-files.db")), "Its index sits next to the first library's");
    }

    [TestMethod]
    public async Task LibraryRootsStayInsideTheAllowedFolders()
    {
        var admin = await AdminAsync(Factory());
        var outside = Directory.CreateDirectory(Path.Combine(_library.BasePath, "Elsewhere")).FullName;

        using var refused = await admin.PostAsJsonAsync("/api/tenants", new TenantInput("Elsewhere", Root: outside), Json, TestContext.CancellationToken);
        using var missing = await admin.PostAsJsonAsync("/api/tenants", new TenantInput("Missing", Root: _library.FullPath("Nowhere")), Json, TestContext.CancellationToken);
        var roots = await GetAsync<FolderPicker>(admin, "/api/tenants/folders");
        var root = await GetAsync<FolderPicker>(admin, $"/api/tenants/folders?path={Uri.EscapeDataString(_library.Root)}");
        using var above = await admin.GetAsync($"/api/tenants/folders?path={Uri.EscapeDataString(_library.BasePath)}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(Path.GetFullPath(_library.Root), roots.Folders.Single().Path);
        CollectionAssert.Contains(root.Folders.Select(f => f.Name).ToList(), "Media");
        Assert.IsNull(root.Parent, "Nothing above an allowed root");
        Assert.AreEqual(HttpStatusCode.NotFound, above.StatusCode);
    }

    [TestMethod]
    public async Task OtherAllowedRootsCanBeConfigured()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(_library.BasePath, "Elsewhere")).FullName;
        File.WriteAllText(Path.Combine(elsewhere, "note.txt"), "hello");
        var admin = await AdminAsync(Factory(settings => settings["Sfe:Tenants:AllowedRoots:0"] = elsewhere));

        var created = await CreateLibraryAsync(admin, "Elsewhere", elsewhere);
        await SwitchAsync(admin, created.Slug);
        var listing = await GetAsync<DirectoryListing>(admin, "/api/browse");

        Assert.AreEqual("note.txt", listing.Items.Single().Name);
    }

    [TestMethod]
    public async Task AccountsOnlyReachTheirLibraries()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var media = await CreateLibraryAsync(admin, "Media", _library.FullPath("Media"));
        await SwitchAsync(admin, media.Slug);
        var users = (await GetAsync<List<GroupInfo>>(admin, "/api/access/groups")).Single(g => g.BuiltIn == BuiltInGroup.Users);
        using (var created = await admin.PostAsJsonAsync("/api/access/users", new UserInput("bob", Password: "bob-password", GroupIds: [users.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        var bob = await ApiClients.SignInAsync(factory, "bob", "bob-password", TestContext.CancellationToken);
        _disposables.Add(bob);
        var session = await GetAsync<SessionInfo>(bob, "/api/session");
        using var first = await bob.PostAsJsonAsync("/api/session/tenant", new TenantChoice(AccessService.DefaultTenantSlug), TestContext.CancellationToken);
        using var tenants = await bob.GetAsync("/api/tenants", TestContext.CancellationToken);
        var adminSession = await GetAsync<SessionInfo>(admin, "/api/session");

        Assert.AreEqual(media.Slug, session.Tenant?.Slug, "An account starts in a library it belongs to");
        Assert.AreEqual(media.Slug, session.Tenants.Single().Slug);
        Assert.AreEqual(HttpStatusCode.NotFound, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, tenants.StatusCode);
        Assert.HasCount(2, adminSession.Tenants, "Server administrators reach every library");
    }

    [TestMethod]
    public async Task ServerAdministratorsManageEverySpaceWithoutSwitching()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var first = (await GetAsync<SessionInfo>(admin, "/api/session")).Tenant!;
        var media = await CreateLibraryAsync(admin, "Media", _library.FullPath("Media"));
        var mediaUsers = (await GetAsync<List<GroupInfo>>(admin, $"/api/access/groups?tenant={media.Id}")).Single(g => g.BuiltIn == BuiltInGroup.Users);
        using (var kids = await admin.PostAsJsonAsync($"/api/access/groups?tenant={media.Id}", new GroupInput("Kids", Rights: [Rights.Browse]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, kids.StatusCode, await kids.Content.ReadAsStringAsync(TestContext.CancellationToken));
        using (var bob = await admin.PostAsJsonAsync("/api/access/users", new UserInput("bob", Password: "bob-password", GroupIds: [mediaUsers.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, bob.StatusCode);
        var administrators = (await GetAsync<List<GroupInfo>>(admin, "/api/access/groups")).Single(g => g.BuiltIn == BuiltInGroup.Administrators);
        using (var manager = await admin.PostAsJsonAsync("/api/access/users", new UserInput("manager", Password: "manager-password", GroupIds: [administrators.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, manager.StatusCode);

        var mediaGroups = await GetAsync<List<GroupInfo>>(admin, $"/api/access/groups?tenant={media.Id}");
        var everyGroup = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups?all=true");
        var mediaAccounts = await GetAsync<List<UserInfo>>(admin, $"/api/access/users?tenant={media.Id}");
        var firstAccounts = await GetAsync<List<UserInfo>>(admin, "/api/access/users");
        var details = (await GetAsync<List<TenantDetails>>(admin, "/api/tenants")).Single(t => t.Id == media.Id);
        await GetAsync<RightsCatalogInfo>(admin, $"/api/access/rights?tenant={media.Id}");

        var spaceAdmin = await ApiClients.SignInAsync(factory, "manager", "manager-password", TestContext.CancellationToken);
        _disposables.Add(spaceAdmin);
        using var otherGroups = await spaceAdmin.GetAsync($"/api/access/groups?tenant={media.Id}", TestContext.CancellationToken);
        using var otherAccounts = await spaceAdmin.GetAsync($"/api/access/users?tenant={media.Id}", TestContext.CancellationToken);
        using var otherGroup = await spaceAdmin.PostAsJsonAsync($"/api/access/groups?tenant={media.Id}", new GroupInput("Intruders"), Json, TestContext.CancellationToken);
        var ownGroups = await GetAsync<List<GroupInfo>>(spaceAdmin, "/api/access/groups?all=true");

        CollectionAssert.AreEquivalent(new[] { "Administrators", "Users", "Guests", "Kids" }, mediaGroups.Select(g => g.Name).ToList());
        Assert.IsTrue(mediaGroups.All(g => g.TenantId == media.Id));
        CollectionAssert.IsSubsetOf(mediaGroups.Select(g => g.Id).ToList(), everyGroup.Select(g => g.Id).ToList(), "all=true lists the groups of every space");
        Assert.IsTrue(everyGroup.Any(g => g.TenantId == first.Id));
        Assert.AreEqual("bob", mediaAccounts.Single().UserName);
        Assert.IsFalse(firstAccounts.Any(u => u.UserName == "bob"), "Without tenant, the request's own space");
        Assert.AreEqual(1, details.Members);
        Assert.AreEqual(4, details.Groups);
        Assert.AreEqual(HttpStatusCode.NotFound, otherGroups.StatusCode, "A space's administrator manages that space only");
        Assert.AreEqual(HttpStatusCode.NotFound, otherAccounts.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, otherGroup.StatusCode);
        Assert.IsTrue(ownGroups.All(g => g.TenantId == first.Id), "all=true is for server administrators: others get their own space's");
    }

    [TestMethod]
    public async Task DisabledAndDeletedLibrariesCannotBeBrowsed()
    {
        var admin = await AdminAsync(Factory());
        var media = await CreateLibraryAsync(admin, "Media", _library.FullPath("Media"));

        using (var disabled = await admin.PutAsJsonAsync($"/api/tenants/{media.Id}", new TenantInput("Media", Disabled: true), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        using var switchToDisabled = await admin.PostAsJsonAsync("/api/session/tenant", new TenantChoice(media.Slug), TestContext.CancellationToken);
        var list = await GetAsync<List<TenantDetails>>(admin, "/api/tenants");
        using var deleted = await admin.DeleteAsync($"/api/tenants/{media.Id}", TestContext.CancellationToken);
        var first = list.Single(t => t.IsDefault);
        using var deleteFirst = await admin.DeleteAsync($"/api/tenants/{first.Id}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, switchToDisabled.StatusCode);
        Assert.IsTrue(list.Single(t => t.Id == media.Id).Disabled);
        Assert.AreEqual(Path.GetFullPath(_library.Root), first.Root);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.IsTrue(File.Exists(Path.Combine(_library.BasePath, "tenants", "media.db")), "Its index stays on the disk");
        Assert.AreEqual(HttpStatusCode.Conflict, deleteFirst.StatusCode);
    }

    [TestMethod]
    public async Task ATabShowingAnotherLibraryIsToldToReload()
    {
        var admin = await AdminAsync(Factory());
        var session = await GetAsync<SessionInfo>(admin, "/api/session");

        using var same = new HttpRequestMessage(HttpMethod.Get, "/api/config") { Headers = { { TenantMiddleware.Header, session.Tenant!.Id.ToString() } } };
        using var sameResponse = await admin.SendAsync(same, TestContext.CancellationToken);
        using var other = new HttpRequestMessage(HttpMethod.Get, "/api/config") { Headers = { { TenantMiddleware.Header, (session.Tenant.Id + 1).ToString() } } };
        using var otherResponse = await admin.SendAsync(other, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, sameResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, otherResponse.StatusCode);
        StringAssert.Contains(await otherResponse.Content.ReadAsStringAsync(TestContext.CancellationToken), TenantMiddleware.TenantChangedType);
    }

    [TestMethod]
    public async Task ExternalPlayerLinksServeTheirOwnLibrarysFile()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var media = await CreateLibraryAsync(admin, "Media", _library.FullPath("Media"));
        await SwitchAsync(admin, media.Slug);

        using var created = await admin.PostAsJsonAsync("/api/files/links", new FileLinkRequest("photo.jpg"), TestContext.CancellationToken);
        var link = (await created.Content.ReadFromJsonAsync<Sfe.Api.Security.FileLink>(Json, TestContext.CancellationToken))!;
        await SwitchAsync(admin, AccessService.DefaultTenantSlug);
        var player = ApiClients.Create(factory);
        _disposables.Add(player);
        using var file = await player.GetAsync("/" + link.Url, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, file.StatusCode);
        Assert.AreEqual(3 * 1024, (await file.Content.ReadAsByteArrayAsync(TestContext.CancellationToken)).Length);
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        _disposables.Add(client);
        return client;
    }

    private async Task<TenantDetails> CreateLibraryAsync(HttpClient admin, string name, string root)
    {
        using var response = await admin.PostAsJsonAsync("/api/tenants", new TenantInput(name, Root: root), Json, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<TenantDetails>(Json, TestContext.CancellationToken))!;
    }

    private async Task SwitchAsync(HttpClient client, string slug)
    {
        using var response = await client.PostAsJsonAsync("/api/session/tenant", new TenantChoice(slug), TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private async Task ScanAsync(HttpClient client)
    {
        using (var start = await client.PostAsJsonAsync("/api/scan", new ScanRequest(""), TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Accepted, start.StatusCode, await start.Content.ReadAsStringAsync(TestContext.CancellationToken));
        for (var i = 0; i < 200; i++)
        {
            if ((await GetAsync<ScanStatus>(client, "/api/scan")).State is not ScanState.Running)
                return;
            await Task.Delay(50, TestContext.CancellationToken);
        }

        Assert.Fail("The scan did not end.");
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(TestContext.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }
}
