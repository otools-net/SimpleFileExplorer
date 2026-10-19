using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// What a Local server (<see cref="ServerModes"/>) lets in: its own computer only, administrators only, no visitors
/// without an account. Installs whose configuration sets no mode are Local, as installs from before the modes.
/// </summary>
[TestClass]
public sealed class ServerModeTests
{
    private const string Localhost = "127.0.0.1";

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
    public async Task ALocalServerIsSetUpFromAnywhereThenAnswersOnlyItsOwnComputer()
    {
        var factory = LocalFactory();
        var remote = ApiClients.Create(factory);

        using var setupInfo = await remote.GetAsync("/api/setup", TestContext.CancellationToken);
        using (var setup = await remote.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, ApiClients.AdminName, ApiClients.AdminPassword),
                   TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, setup.StatusCode, "Nothing is restricted before the setup is done");
        using var remoteBrowse = await remote.GetAsync("/api/browse", TestContext.CancellationToken);
        using var remoteSession = await remote.GetAsync("/api/session", TestContext.CancellationToken);
        using var remoteDocs = await remote.GetAsync("/openapi/v1.json", TestContext.CancellationToken);
        remote.DefaultRequestHeaders.Add(ApiClients.AddressHeader, Localhost);
        using var localBrowse = await remote.GetAsync("/api/browse", TestContext.CancellationToken);
        var config = await GetAsync<JsonObject>(remote, "/api/config");

        Assert.AreEqual(HttpStatusCode.OK, setupInfo.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteBrowse.StatusCode);
        StringAssert.Contains(await remoteBrowse.Content.ReadAsStringAsync(TestContext.CancellationToken), ServerModes.LocalOnlyType);
        Assert.AreEqual(HttpStatusCode.OK, remoteSession.StatusCode, "Signing in stays open: the server's administrator may switch to External");
        Assert.AreEqual(HttpStatusCode.Forbidden, remoteDocs.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, localBrowse.StatusCode);
        Assert.AreEqual("local", config["mode"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ALocalServerLetsOnlyAdministratorsSignIn()
    {
        var factory = LocalFactory();
        var admin = await LocalAdminAsync(factory);
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");
        await CreateUserAsync(admin, "viewer", groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Id);
        await CreateUserAsync(admin, "spaceadmin", groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Id);

        using var viewer = await SignInAsync(factory, "viewer");
        using var spaceAdmin = await SignInAsync(factory, "spaceadmin");

        Assert.AreEqual(HttpStatusCode.BadRequest, viewer.StatusCode);
        StringAssert.Contains(await viewer.Content.ReadAsStringAsync(TestContext.CancellationToken), SessionEndpoints.LocalAdministratorsOnlyType);
        Assert.AreEqual(HttpStatusCode.OK, spaceAdmin.StatusCode, await spaceAdmin.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task TheSessionOfAnAccountNoLongerAdministratorEnds()
    {
        var factory = LocalFactory();
        var admin = await LocalAdminAsync(factory);
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");
        var id = await CreateUserAsync(admin, "spaceadmin", groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Id);
        var client = ApiClients.CreateFrom(factory, Localhost);
        using (var signedIn = await client.PostAsJsonAsync("/api/session/login", new SignInRequest("spaceadmin", "spaceadmin-password"), TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, signedIn.StatusCode);
        using var before = await client.GetAsync("/api/browse", TestContext.CancellationToken);

        using (var demoted = await admin.PutAsJsonAsync($"/api/access/users/{id}",
                   new UserInput("spaceadmin", GroupIds: [groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, demoted.StatusCode, await demoted.Content.ReadAsStringAsync(TestContext.CancellationToken));
        using var after = await client.GetAsync("/api/browse", TestContext.CancellationToken);
        var session = await GetAsync<JsonObject>(client, "/api/session");

        Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.IsNull(session["user"]);
    }

    [TestMethod]
    public async Task ALocalServerHasNoVisitorsWithoutAnAccount()
    {
        var factory = LocalFactory();
        var admin = await LocalAdminAsync(factory);
        var visitor = ApiClients.CreateFrom(factory, Localhost);

        using var opened = await admin.PutAsJsonAsync("/api/access/tenant/guest-access", new GuestAccessChange(true), TestContext.CancellationToken);
        var access = (await opened.Content.ReadFromJsonAsync<TenantAccessInfo>(Json, TestContext.CancellationToken))!;
        var session = await GetAsync<JsonObject>(visitor, "/api/session");
        using var browse = await visitor.GetAsync("/api/browse", TestContext.CancellationToken);
        using var guest = await visitor.PostAsJsonAsync("/api/session/guest", new TenantChoice(AccessService.DefaultTenantSlug), TestContext.CancellationToken);

        Assert.IsFalse(access.AnonymousGuestsAllowed);
        Assert.IsEmpty(session["guestTenants"]!.AsArray());
        Assert.AreEqual(HttpStatusCode.Unauthorized, browse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, guest.StatusCode);
    }

    [TestMethod]
    public async Task AnExternalServerKeepsEveryAccountAndVisitor()
    {
        var factory = ApiClients.CreateFactory(_library);
        _disposables.Add(factory);
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");
        await CreateUserAsync(admin, "viewer", groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Id);

        var viewer = await ApiClients.SignInAsync(factory, "viewer", "viewer-password", TestContext.CancellationToken);
        using var browse = await viewer.GetAsync("/api/browse", TestContext.CancellationToken);
        var config = await GetAsync<JsonObject>(viewer, "/api/config");

        Assert.AreEqual(HttpStatusCode.OK, browse.StatusCode, "From another computer, as an account that is no administrator");
        Assert.AreEqual("external", config["mode"]!.GetValue<string>());
    }

    /// <summary>A server whose configuration sets no mode: Local, as installs from before the modes.</summary>
    private WebApplicationFactory<Program> LocalFactory()
    {
        var factory = ApiClients.CreateFactory(_library, settings => settings.Remove(ServerModes.Key));
        _disposables.Add(factory);
        return factory;
    }

    /// <summary>The setup, then the administrator's client on the server's own computer.</summary>
    private async Task<HttpClient> LocalAdminAsync(WebApplicationFactory<Program> factory)
    {
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        admin.DefaultRequestHeaders.Add(ApiClients.AddressHeader, Localhost);
        return admin;
    }

    private async Task<int> CreateUserAsync(HttpClient admin, string name, int groupId)
    {
        using var created = await admin.PostAsJsonAsync("/api/access/users", new UserInput(name, Password: $"{name}-password", GroupIds: [groupId]), Json,
            TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await created.Content.ReadFromJsonAsync<UserInfo>(Json, TestContext.CancellationToken))!.Id;
    }

    private Task<HttpResponseMessage> SignInAsync(WebApplicationFactory<Program> factory, string name) =>
        ApiClients.CreateFrom(factory, Localhost).PostAsJsonAsync("/api/session/login", new SignInRequest(name, $"{name}-password"), TestContext.CancellationToken);

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }
}
