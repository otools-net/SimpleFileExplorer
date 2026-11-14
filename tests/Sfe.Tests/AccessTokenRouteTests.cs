using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;
using static Otools.Net.Sfe.Tests.Infrastructure.TokenClients;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The access tokens' routes (<c>me/tokens</c>, <c>access/tokens</c>): what an account and its administrators do
/// with them, their rights, the routes a token may not call, the client's configuration and the OpenAPI document.
/// </summary>
[TestClass]
public sealed class AccessTokenRouteTests
{
    /// <summary>The route groups for sessions only: what lets someone in, and the tokens themselves.</summary>
    private static readonly string[] SessionOnlyGroups =
    [
        "session",
        "setup",
        "installation",
        "migration",
        "me/tokens",
        "access/tokens",
    ];

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AnAccountMakesListsAndRevokesItsTokens()
    {
        var admin = await AdminAsync(Factory());

        using var made = await admin.PostAsJsonAsync(
            $"{V1}/me/tokens",
            new { name = "Backup script", rights = new[] { Rights.Browse } },
            Token
        );
        var created = (await made.Content.ReadFromJsonAsync<JsonObject>(Json, Token))!;
        var secret = created["secret"]!.GetValue<string>();
        var id = created["token"]!["id"]!.GetValue<int>();
        using var listed = await admin.GetAsync($"{V1}/me/tokens", Token);
        var listText = await listed.Content.ReadAsStringAsync(Token);
        var list = JsonNode.Parse(listText)!.AsArray();

        Assert.AreEqual(HttpStatusCode.Created, made.StatusCode);
        Assert.StartsWith(AccessTokenSecret.Scheme, secret);
        Assert.AreEqual("personal", created["token"]!["kind"]!.GetValue<string>());
        Assert.AreEqual("Backup script", list.Single()!["name"]!.GetValue<string>());
        Assert.AreEqual(Rights.Browse, list.Single()!["rights"]![0]!.GetValue<string>());
        Assert.DoesNotContain(secret[created["token"]!["prefix"]!.GetValue<string>().Length..], listText);

        using var revoked = await admin.DeleteAsync($"{V1}/me/tokens/{id}", Token);
        using var unknown = await admin.DeleteAsync($"{V1}/me/tokens/{id + 100}", Token);
        await MakeAsync(admin, "Second");
        await MakeAsync(admin, "Third");
        using var all = await admin.PostAsync($"{V1}/me/tokens/revoke-all", null, Token);
        var after = await GetAsync<List<AccessTokenInfo>>(admin, $"{V1}/me/tokens", Token);

        Assert.AreEqual(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(2, (await all.Content.ReadFromJsonAsync<RevokedTokens>(Json, Token))!.Revoked);
        Assert.HasCount(3, after);
        Assert.IsTrue(after.All(t => t.Revoked is not null));
    }

    [TestMethod]
    public async Task ANewTokenIsCheckedBeforeItIsMade()
    {
        var factory = Factory(settings => settings["Sfe:Security:AccessTokens:MaxPerAccount"] = "1");
        var admin = await AdminAsync(factory);

        using var nameless = await admin.PostAsJsonAsync($"{V1}/me/tokens", new { name = " " }, Token);
        using var unknownRight = await admin.PostAsJsonAsync(
            $"{V1}/me/tokens",
            new { name = "x", rights = new[] { "no.such.right" } },
            Token
        );
        using var past = await admin.PostAsJsonAsync(
            $"{V1}/me/tokens",
            new { name = "x", expires = DateTime.UtcNow.AddDays(-1) },
            Token
        );
        await MakeAsync(admin, "Only one");
        using var tooMany = await admin.PostAsJsonAsync($"{V1}/me/tokens", new { name = "Second" }, Token);

        Assert.AreEqual(HttpStatusCode.BadRequest, nameless.StatusCode);
        StringAssert.Contains(await nameless.Content.ReadAsStringAsync(Token), "\"name\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownRight.StatusCode);
        StringAssert.Contains(await unknownRight.Content.ReadAsStringAsync(Token), "\"rights\"");
        Assert.AreEqual(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.IsTrue(
            (await GetAsync<JsonObject>(admin, $"{V1}/config", Token))["accessTokens"]!["enabled"]!.GetValue<bool>()
        );
    }

    [TestMethod]
    public async Task TokensTurnedOffAreNeitherMadeNorUsed()
    {
        var factory = Factory(settings => settings["Sfe:Security:AccessTokens:Enabled"] = "false");
        var admin = await AdminAsync(factory);
        var adminId = (await GetAsync<SessionInfo>(admin, $"{V1}/session", Token)).User!.Id;
        var tokens = factory.Services.GetRequiredService<AccessTokens>();

        using var refused = await admin.PostAsJsonAsync($"{V1}/me/tokens", new { name = "x" }, Token);
        var config = await GetAsync<JsonObject>(admin, $"{V1}/config", Token);
        var made = await tokens.CreateAsync(adminId, new("x"), Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.IsFalse(config["accessTokens"]!["enabled"]!.GetValue<bool>());
        Assert.AreEqual(365, config["accessTokens"]!["maxLifetimeDays"]!.GetValue<int>());
        Assert.AreEqual(AccessStatus.Conflict, made.Status);
    }

    [TestMethod]
    public async Task TheTokenRightsAreTheAdministratorsAlone()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var users = await GroupAsync(admin, BuiltInGroup.Users, Token);
        await CreateUserAsync(admin, "bob", [users.Id], Token);
        var bob = await SignInAsync(factory, "bob");

        using var own = await bob.GetAsync($"{V1}/me/tokens", Token);
        using var make = await bob.PostAsJsonAsync($"{V1}/me/tokens", new { name = "x" }, Token);
        using var everyone = await bob.GetAsync($"{V1}/access/tokens", Token);
        var config = await GetAsync<JsonObject>(bob, $"{V1}/config", Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, make.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, everyone.StatusCode);
        Assert.IsNull(config["accessTokens"], "Nothing about tokens for a session that may not have them");
        foreach (var right in new[] { Rights.OwnTokens, Rights.ManageTokens })
        {
            var definition = RightCatalog.Find(right)!;
            Assert.IsFalse(definition.GuestAllowed, right);
            CollectionAssert.AreEqual(new[] { BuiltInGroup.Administrators }, definition.DefaultGroups.ToArray(), right);
            Assert.AreEqual(RightCatalog.Revision, definition.Since, right);
        }
    }

    [TestMethod]
    public async Task AdministratorsSeeAndRevokeTheTokensOfTheirSpacesOnly()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var adminId = (await GetAsync<SessionInfo>(admin, $"{V1}/session", Token)).User!.Id;
        var media = await CreateLibraryAsync(admin, "Media");
        var mediaAdmins = await GroupAsync(admin, BuiltInGroup.Administrators, Token, media.Id);
        var mediaUsers = await GroupAsync(admin, BuiltInGroup.Users, Token, media.Id);
        var firstUsers = await GroupAsync(admin, BuiltInGroup.Users, Token);
        var manager = await CreateUserAsync(admin, "manager", [mediaAdmins.Id], Token);
        var member = await CreateUserAsync(admin, "member", [mediaUsers.Id], Token);
        var outsider = await CreateUserAsync(admin, "outsider", [firstUsers.Id], Token);
        var memberToken = await CreateAsync(factory, member.Id, new("Member's"), Token);
        var outsiderToken = await CreateAsync(factory, outsider.Id, new("Outsider's"), Token);
        var adminToken = await CreateAsync(factory, adminId, new("Admin's"), Token);
        var managerClient = await SignInAsync(factory, "manager");

        var everyToken = await GetAsync<List<AccessTokenInfo>>(admin, $"{V1}/access/tokens", Token);
        var managed = await GetAsync<List<AccessTokenInfo>>(managerClient, $"{V1}/access/tokens", Token);
        using var otherSpace = await managerClient.DeleteAsync($"{V1}/access/tokens/{outsiderToken.Token.Id}", Token);
        using var serverAdmin = await managerClient.DeleteAsync($"{V1}/access/tokens/{adminToken.Token.Id}", Token);
        using var notOwn = await managerClient.DeleteAsync($"{V1}/me/tokens/{memberToken.Token.Id}", Token);
        using var revoked = await managerClient.DeleteAsync($"{V1}/access/tokens/{memberToken.Token.Id}", Token);
        using var byServerAdmin = await admin.DeleteAsync($"{V1}/access/tokens/{outsiderToken.Token.Id}", Token);
        var afterwards = await GetAsync<List<AccessTokenInfo>>(admin, $"{V1}/access/tokens", Token);

        Assert.HasCount(3, everyToken);
        CollectionAssert.AreEquivalent(new[] { "Member's" }, managed.Select(t => t.Name).ToArray());
        Assert.AreEqual(HttpStatusCode.NotFound, otherSpace.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, serverAdmin.StatusCode, "Never a server administrator's token");
        Assert.AreEqual(HttpStatusCode.NotFound, notOwn.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, byServerAdmin.StatusCode);
        Assert.AreEqual(manager.Id, afterwards.Single(t => t.Id == memberToken.Token.Id).RevokedById);
        Assert.IsNull(afterwards.Single(t => t.Id == adminToken.Token.Id).Revoked);
    }

    [TestMethod]
    public async Task EveryRouteLettingSomeoneInOrHandlingTokensIsForSessionsOnly()
    {
        var factory = Factory();
        await AdminAsync(factory);

        var routes = factory
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(V1 + "/", StringComparison.Ordinal) == true)
            .ToList();
        var expected = routes.Where(e => SessionOnlyGroups.Any(g => IsUnder(e, g))).ToList();
        var missing = expected.Where(e => !SessionOnlyRoutes.IsSessionOnly(e)).Select(e => e.RoutePattern.RawText);
        var extra = routes.Except(expected).Where(SessionOnlyRoutes.IsSessionOnly).Select(e => e.RoutePattern.RawText);

        Assert.IsGreaterThan(15, expected.Count);
        Assert.IsEmpty(missing.ToList(), "Routes a token may call but shouldn't: " + string.Join(", ", missing));
        Assert.IsEmpty(extra.ToList(), "Session-only routes not in the list: " + string.Join(", ", extra));
    }

    [TestMethod]
    public async Task TheOpenApiDocumentDeclaresTheBearerScheme()
    {
        var admin = await AdminAsync(Factory());

        var document = await GetAsync<JsonObject>(admin, "/openapi/v1.json", Token);
        var scheme = document["components"]!["securitySchemes"]![Otools.Net.Sfe.Api.ApiDocument.BearerScheme]!;
        var signIn = document["paths"]![$"{V1}/session/login"]!["post"]!;
        var browse = document["paths"]![$"{V1}/browse"]!["get"]!;

        Assert.AreEqual("http", scheme["type"]!.GetValue<string>());
        Assert.AreEqual("bearer", scheme["scheme"]!.GetValue<string>());
        Assert.IsNotNull(document["security"]![0]![Otools.Net.Sfe.Api.ApiDocument.BearerScheme]);
        Assert.IsTrue(signIn[Otools.Net.Sfe.Api.ApiDocument.SessionOnlyExtension]!.GetValue<bool>());
        Assert.IsNull(browse[Otools.Net.Sfe.Api.ApiDocument.SessionOnlyExtension]);
    }

    private static bool IsUnder(RouteEndpoint endpoint, string group)
    {
        var path = endpoint.RoutePattern.RawText!.TrimEnd('/');
        var prefix = $"{V1}/{group}";
        return path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    private async Task MakeAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync($"{V1}/me/tokens", new { name }, Token);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    private async Task<TenantDetails> CreateLibraryAsync(HttpClient admin, string name)
    {
        var root = Directory.CreateDirectory(_library.FullPath(name)).FullName;
        using var response = await admin.PostAsJsonAsync(
            $"{V1}/tenants",
            new TenantInput(name, Root: root),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TenantDetails>(Json, Token))!;
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = await ApiClients.SetUpAsync(factory, Token);
        _disposables.Add(client);
        return client;
    }

    private async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string name)
    {
        var client = await ApiClients.SignInAsync(factory, name, $"{name}-password", Token);
        _disposables.Add(client);
        return client;
    }
}
