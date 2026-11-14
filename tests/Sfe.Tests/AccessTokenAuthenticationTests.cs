using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Logs;
using Sfe.Tests.Infrastructure;
using static Sfe.Tests.Infrastructure.TokenClients;

namespace Sfe.Tests;

/// <summary>
/// Calling the API with a personal access token (<c>Authorization: Bearer</c>), and the attack vectors of
/// agents/docs/development/api-tokens-plan.md: wrong, expired and revoked tokens, an account disabled or deleted, a
/// token beyond its rights or its library, routes for sessions only, guessing, the secret in logs, a cookie sent
/// along, Local mode.
/// </summary>
[TestClass]
public sealed class AccessTokenAuthenticationTests
{
    private const string Localhost = "127.0.0.1";

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
    public async Task ABearerActsAsItsAccountWithoutCookieNorRequestHeader()
    {
        var factory = Factory();
        var (_, adminId) = await AdminAsync(factory);
        var token = await CreateAsync(factory, adminId, new("Script"), Token);
        var program = Program(factory, token.Secret);

        var config = await GetAsync<ClientConfig>(program, $"{V1}/config", Token);
        using var tag = await program.PostAsJsonAsync($"{V1}/tags", new { name = "Holiday" }, Token);
        using var inQuery = await Visitor(factory).GetAsync($"{V1}/config?access_token={token.Secret}", Token);

        Assert.AreEqual(ApiClients.AdminName, config.User?.UserName);
        Assert.AreEqual(HttpStatusCode.Created, tag.StatusCode, "No X-SFE-Request needed with a bearer");
        Assert.IsFalse(tag.Headers.Contains("Set-Cookie"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, inQuery.StatusCode, "Never taken from the query");
    }

    [TestMethod]
    public async Task WrongExpiredAndRevokedTokensAreRefusedAlike()
    {
        var factory = Factory();
        var (admin, adminId) = await AdminAsync(factory);
        var expiring = await CreateAsync(factory, adminId, new("Expiring"), Token);
        var revoked = await CreateAsync(factory, adminId, new("Revoked"), Token);
        var valid = await CreateAsync(factory, adminId, new("Valid"), Token);
        await ExpireAsync(factory, expiring.Token.Id);
        using (var revoking = await admin.DeleteAsync($"{V1}/me/tokens/{revoked.Token.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NoContent, revoking.StatusCode);

        var wrongSecret = valid.Secret[..^2] + (valid.Secret[^2] == 'A' ? "BA" : "AA");
        var answers = new List<HttpResponseMessage>();
        foreach (var presented in new[] { wrongSecret, "sfe_0000000000000000_" + valid.Secret[^43..], "garbage" })
            answers.Add(await Program(factory, presented, "192.0.2.1").GetAsync($"{V1}/config", Token));
        answers.Add(await Program(factory, expiring.Secret, "192.0.2.2").GetAsync($"{V1}/config", Token));
        answers.Add(await Program(factory, revoked.Secret, "192.0.2.2").PostAsync($"{V1}/tags", null, Token));

        foreach (var answer in answers)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, answer.StatusCode);
            StringAssert.StartsWith(answer.Headers.WwwAuthenticate.ToString(), "Bearer");
            Assert.IsFalse(answer.Headers.Contains("Set-Cookie"));
            answer.Dispose();
        }
    }

    [TestMethod]
    public async Task ATokenOfADisabledOrDeletedAccountStopsWorking()
    {
        var factory = Factory();
        var (admin, _) = await AdminAsync(factory);
        var administrators = await GroupAsync(admin, BuiltInGroup.Administrators, Token);
        var bob = await CreateUserAsync(admin, "bob", [administrators.Id], Token);
        var carol = await CreateUserAsync(admin, "carol", [administrators.Id], Token);
        var bobToken = await CreateAsync(factory, bob.Id, new("Bob's"), Token);
        var carolToken = await CreateAsync(factory, carol.Id, new("Carol's"), Token);
        using (var before = await Program(factory, bobToken.Secret).GetAsync($"{V1}/config", Token))
            Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);

        var disabled = new UserInput("bob", Disabled: true, GroupIds: [administrators.Id]);
        using (var disabling = await admin.PutAsJsonAsync($"{V1}/access/users/{bob.Id}", disabled, Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, disabling.StatusCode);
        using (var deleting = await admin.DeleteAsync($"{V1}/access/users/{carol.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NoContent, deleting.StatusCode);

        using var ofDisabled = await Program(factory, bobToken.Secret).GetAsync($"{V1}/config", Token);
        using var ofDeleted = await Program(factory, carolToken.Secret).GetAsync($"{V1}/config", Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, ofDisabled.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, ofDeleted.StatusCode);
    }

    [TestMethod]
    public async Task ATokenHasItsOwnRightsAmongWhatItsAccountHoldsNow()
    {
        var factory = Factory();
        var (admin, adminId) = await AdminAsync(factory);
        var browseOnly = await CreateAsync(factory, adminId, new("Reader", Rights: [Rights.Browse]), Token);
        var administrators = await GroupAsync(admin, BuiltInGroup.Administrators, Token);
        var users = await GroupAsync(admin, BuiltInGroup.Users, Token);
        var bob = await CreateUserAsync(admin, "bob", [administrators.Id], Token);
        var tagger = await CreateAsync(factory, bob.Id, new("Tagger", Rights: [Rights.ManageTags]), Token);

        var reader = Program(factory, browseOnly.Secret);
        var config = await GetAsync<ClientConfig>(reader, $"{V1}/config", Token);
        using var beyond = await reader.PostAsJsonAsync($"{V1}/tags", new { name = "Beyond" }, Token);
        using var allowed = await Program(factory, tagger.Secret)
            .PostAsJsonAsync($"{V1}/tags", new { name = "Allowed" }, Token);
        var demoted = new UserInput("bob", GroupIds: [users.Id]);
        using (var demoting = await admin.PutAsJsonAsync($"{V1}/access/users/{bob.Id}", demoted, Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, demoting.StatusCode);
        using var lost = await Program(factory, tagger.Secret)
            .PostAsJsonAsync($"{V1}/tags", new { name = "Lost" }, Token);

        CollectionAssert.AreEqual(new[] { Rights.Browse }, config.Rights.ToArray());
        Assert.AreEqual(HttpStatusCode.Forbidden, beyond.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, allowed.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, lost.StatusCode, "The account no longer holds the right");
    }

    [TestMethod]
    public async Task ATokenStaysInItsSpaceAndTheSpacesItsAccountReaches()
    {
        var factory = Factory();
        var (admin, _) = await AdminAsync(factory);
        var first = (await GetAsync<SessionInfo>(admin, $"{V1}/session", Token)).Tenant!;
        var media = await CreateLibraryAsync(admin, "Media");
        var users = await GroupAsync(admin, BuiltInGroup.Users, Token);
        var mediaUsers = await GroupAsync(admin, BuiltInGroup.Users, Token, media.Id);
        var bob = await CreateUserAsync(admin, "bob", [users.Id, mediaUsers.Id], Token);
        var limited = await CreateAsync(factory, bob.Id, new("Media only", TenantId: media.Id), Token);
        var free = await CreateAsync(factory, bob.Id, new("Anywhere"), Token);

        var inMedia = await GetAsync<ClientConfig>(Program(factory, limited.Secret), $"{V1}/config", Token);
        using var elsewhere = await Named(Program(factory, limited.Secret), first.Id).GetAsync($"{V1}/config", Token);
        var named = await GetAsync<ClientConfig>(Named(Program(factory, free.Secret), media.Id), $"{V1}/config", Token);
        using var unknown = await Named(Program(factory, free.Secret), 9999).GetAsync($"{V1}/config", Token);
        var left = new UserInput("bob", GroupIds: [users.Id]);
        using (var removing = await admin.PutAsJsonAsync($"{V1}/access/users/{bob.Id}", left, Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, removing.StatusCode);
        using var removed = await Program(factory, limited.Secret).GetAsync($"{V1}/config", Token);

        Assert.AreEqual(media.Id, inMedia.Tenant?.Id);
        Assert.AreEqual(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.AreEqual(media.Id, named.Tenant?.Id);
        Assert.AreEqual(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, removed.StatusCode, "The account left the token's space");
    }

    [TestMethod]
    public async Task RoutesForSessionsOnlyRefuseTokens()
    {
        var factory = Factory();
        var (_, adminId) = await AdminAsync(factory);
        var token = await CreateAsync(factory, adminId, new("Script"), Token);
        var program = Program(factory, token.Secret);

        var answers = new[]
        {
            await program.GetAsync($"{V1}/me/tokens", Token),
            await program.PostAsJsonAsync($"{V1}/me/tokens", new { name = "More" }, Token),
            await program.PostAsync($"{V1}/me/tokens/revoke-all", null, Token),
            await program.GetAsync($"{V1}/access/tokens", Token),
            await program.PutAsJsonAsync($"{V1}/session/password", new { current = "x", password = "y" }, Token),
            await program.PostAsJsonAsync($"{V1}/session/login", new SignInRequest("admin", "admin-password"), Token),
            await program.GetAsync($"{V1}/installation", Token),
            await program.GetAsync($"{V1}/setup", Token),
        };

        foreach (var answer in answers)
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, answer.StatusCode, answer.RequestMessage?.RequestUri?.ToString());
            StringAssert.Contains(await answer.Content.ReadAsStringAsync(Token), SessionOnlyRoutes.Type);
            Assert.IsFalse(answer.Headers.Contains("Set-Cookie"));
            answer.Dispose();
        }
        Assert.HasCount(1, await ListAsync(factory, adminId), "No token was made by a token");
    }

    [TestMethod]
    public async Task WrongTokensAreThrottledPerAddressAndTheSecretIsNeverLogged()
    {
        var factory = Factory();
        var (_, adminId) = await AdminAsync(factory);
        var token = await CreateAsync(factory, adminId, new("Script"), Token);
        var wrong = token.Secret[..^4] + "AAAA";

        for (var i = 0; i < 5; i++)
            (await Program(factory, wrong, "192.0.2.50").GetAsync($"{V1}/config", Token)).Dispose();
        for (var i = 1; i <= 5; i++)
            (await Program(factory, wrong, $"2001:db8:1:2::{i}").GetAsync($"{V1}/config", Token)).Dispose();
        using var blocked = await Program(factory, token.Secret, "192.0.2.50").GetAsync($"{V1}/config", Token);
        using var sameNetwork = await Program(factory, token.Secret, "2001:db8:1:2::99")
            .GetAsync($"{V1}/config", Token);
        using var elsewhere = await Program(factory, token.Secret, "192.0.2.51").GetAsync($"{V1}/config", Token);

        Assert.AreEqual(HttpStatusCode.Unauthorized, blocked.StatusCode, "The address is refused every token a while");
        Assert.AreEqual(HttpStatusCode.Unauthorized, sameNetwork.StatusCode, "An IPv6 visitor counts as its /64");
        Assert.AreEqual(HttpStatusCode.OK, elsewhere.StatusCode);

        var log = await LogTextAsync(factory);
        StringAssert.Contains(log, "192.0.2.50");
        StringAssert.Contains(log, AccessTokenSecret.Scheme + token.Token.Prefix[4..20]);
        Assert.DoesNotContain(token.Secret[(token.Token.Prefix.Length - 4)..], log);
        Assert.DoesNotContain(wrong[(token.Token.Prefix.Length - 4)..], log);
    }

    [TestMethod]
    public async Task ChangesMadeWithATokenAreLoggedWithTheAccountAndTheToken()
    {
        var factory = Factory();
        var (_, adminId) = await AdminAsync(factory);
        var token = await CreateAsync(factory, adminId, new("Nightly import"), Token);

        using var tag = await Program(factory, token.Secret)
            .PostAsJsonAsync($"{V1}/tags", new { name = "Import" }, Token);
        using var refused = await Program(factory, token.Secret)
            .PostAsJsonAsync($"{V1}/tags", new { name = "" }, Token);

        var log = await LogTextAsync(factory);
        StringAssert.Contains(log, $"POST {V1}/tags answered 201 to admin through access token {token.Token.Id}");
        StringAssert.Contains(log, "“Nightly import”");
        StringAssert.Contains(log, $"answered {(int)refused.StatusCode} to admin through access token");
        Assert.DoesNotContain(token.Secret[(token.Token.Prefix.Length - 4)..], log);
    }

    [TestMethod]
    public async Task WithACookieAndABearerTheBearerDecides()
    {
        var factory = Factory();
        var (admin, _) = await AdminAsync(factory);
        var users = await GroupAsync(admin, BuiltInGroup.Users, Token);
        var bob = await CreateUserAsync(admin, "bob", [users.Id], Token);
        var bobToken = await CreateAsync(factory, bob.Id, new("Bob's"), Token);

        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken.Secret);
        var asBob = await GetAsync<ClientConfig>(admin, $"{V1}/config", Token);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sfe_wrong");
        using var wrong = await admin.GetAsync($"{V1}/config", Token);
        admin.DefaultRequestHeaders.Authorization = null;
        admin.DefaultRequestHeaders.Remove(RequestGuard.Header);
        using var cookieWithoutHeader = await admin.PostAsJsonAsync($"{V1}/tags", new { name = "x" }, Token);

        Assert.AreEqual("bob", asBob.User?.UserName);
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode, "Never the cookie instead");
        Assert.AreEqual(HttpStatusCode.BadRequest, cookieWithoutHeader.StatusCode, "The cookie still needs the header");
    }

    [TestMethod]
    public async Task ALocalServerTakesTokensOfItsAdministratorsOnItsOwnComputerOnly()
    {
        var factory = Factory(settings => settings.Remove(ServerModes.Key));
        var (admin, adminId) = await AdminAsync(factory);
        admin.DefaultRequestHeaders.Add(ApiClients.AddressHeader, Localhost);
        var users = await GroupAsync(admin, BuiltInGroup.Users, Token);
        var viewer = await CreateUserAsync(admin, "viewer", [users.Id], Token);
        var adminToken = await CreateAsync(factory, adminId, new("Admin's"), Token);
        var viewerToken = await CreateAsync(factory, viewer.Id, new("Viewer's"), Token);

        using var local = await Program(factory, adminToken.Secret, Localhost).GetAsync($"{V1}/config", Token);
        using var remote = await Program(factory, adminToken.Secret, "192.0.2.9").GetAsync($"{V1}/config", Token);
        using var notAdmin = await Program(factory, viewerToken.Secret, Localhost).GetAsync($"{V1}/config", Token);

        Assert.AreEqual(HttpStatusCode.OK, local.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, remote.StatusCode);
        StringAssert.Contains(await remote.Content.ReadAsStringAsync(Token), ServerModes.LocalOnlyType);
        Assert.AreEqual(HttpStatusCode.Unauthorized, notAdmin.StatusCode);
    }

    private static HttpClient Named(HttpClient client, int tenantId)
    {
        client.DefaultRequestHeaders.Add(TenantMiddleware.Header, tenantId.ToString(CultureInfo.InvariantCulture));
        return client;
    }

    private HttpClient Program(WebApplicationFactory<Program> factory, string token, string? address = null)
    {
        var client = Bearer(factory, token, address);
        _disposables.Add(client);
        return client;
    }

    private HttpClient Visitor(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        _disposables.Add(client);
        return client;
    }

    private static async Task ExpireAsync(WebApplicationFactory<Program> factory, int tokenId)
    {
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db
            .AccessTokens.Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Expires, DateTime.UtcNow.AddMinutes(-1)));
        factory.Services.GetRequiredService<AccessChanges>().Changed();
    }

    private async Task<IReadOnlyList<AccessTokenInfo>> ListAsync(WebApplicationFactory<Program> factory, int userId) =>
        await factory.Services.GetRequiredService<AccessTokens>().ListAsync(userId, Token);

    /// <summary>Every entry of the server's log, as one text.</summary>
    private async Task<string> LogTextAsync(WebApplicationFactory<Program> factory)
    {
        await factory.Services.GetRequiredService<AuditLogService>().FlushAsync(cancellationToken: Token);
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(Token);
        var entries = await db.LogEntries.Select(e => e.Message + " " + e.Exception).ToListAsync(Token);
        return string.Join("\n", entries);
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

    private async Task<(HttpClient Client, int UserId)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = await ApiClients.SetUpAsync(factory, Token);
        _disposables.Add(client);
        var session = await GetAsync<SessionInfo>(client, $"{V1}/session", Token);
        return (client, session.User!.Id);
    }
}
