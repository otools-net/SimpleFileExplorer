using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The sign-in routes (<c>api/connections/authorize</c>, its completion, the OAuth callback): rights by level, the
/// state bound to the session's account, the browser sent back into the client. Attack vectors of the plan: another
/// account's state, a state reused or forged, the callback signed out, a level the account may not make.
/// </summary>
[TestClass]
public sealed class ConnectionSignInRouteTests
{
    private const string Authorize = $"{V1}/connections/authorize";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    private WebApplicationFactory<Program> Factory() =>
        ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(new FakeProvider()))
            );

    [TestMethod]
    public async Task TheRedirectFlowEndsInTheClientWithTheConnectionMade()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        var begun = await BeginAsync(admin, ConnectionLevel.Account, SignInKind.OAuthRedirect);
        // The return address registered at the provider: unversioned for good.
        StringAssert.Contains(begun.Address, Uri.EscapeDataString(ApiRoutes.OAuthCallback));

        var landed = await CallbackAsync(admin, $"code=good:dave&state={Uri.EscapeDataString(begun.State!)}");
        var made = await SingleConnectionAsync(factory);
        Assert.AreEqual($"{ConnectionSignInEndpoints.ClientPage}?made={made.Id}", landed);
        Assert.AreEqual("dave@fake", made.Account);
        Assert.AreNotEqual("dave", made.Credentials);

        // Used once: the same answer again is late.
        Assert.AreEqual(
            $"{ConnectionSignInEndpoints.ClientPage}?error=expired",
            await CallbackAsync(admin, $"code=good:dave&state={Uri.EscapeDataString(begun.State!)}")
        );
    }

    [TestMethod]
    public async Task AnotherAccountsStateMakesNothing()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var alice = await AdministratorOfTheLibraryAsync(factory);

        var begun = await BeginAsync(admin, ConnectionLevel.Account, SignInKind.OAuthRedirect);
        var stolen = await CallbackAsync(alice, $"code=good:x&state={Uri.EscapeDataString(begun.State!)}");
        var forged = await CallbackAsync(admin, "code=good:x&state=forged");

        Assert.AreEqual($"{ConnectionSignInEndpoints.ClientPage}?error=expired", stolen);
        Assert.AreEqual($"{ConnectionSignInEndpoints.ClientPage}?error=expired", forged);
        // Spent by alice's try: the administrator can't use it either.
        Assert.AreEqual(
            $"{ConnectionSignInEndpoints.ClientPage}?error=expired",
            await CallbackAsync(admin, $"code=good:x&state={Uri.EscapeDataString(begun.State!)}")
        );
        await using var db = await DbAsync(factory);
        Assert.IsFalse(await db.Connections.AnyAsync(Cancel));
    }

    [TestMethod]
    public async Task ARefusalAtTheProviderOrASignedOutCallbackMakesNothing()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var begun = await BeginAsync(admin, ConnectionLevel.Account, SignInKind.OAuthRedirect);
        var state = Uri.EscapeDataString(begun.State!);

        using var signedOut = ApiClients.Create(factory);
        using var anonymous = await signedOut.GetAsync($"{ApiRoutes.OAuthCallback}?code=good:x&state={state}", Cancel);
        var refused = await CallbackAsync(admin, $"error=access_denied&state={state}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual($"{ConnectionSignInEndpoints.ClientPage}?error=refused", refused);
        Assert.AreEqual(
            $"{ConnectionSignInEndpoints.ClientPage}?error=expired",
            await CallbackAsync(admin, $"code=good:x&state={state}")
        );
    }

    [TestMethod]
    public async Task TheCodeFlowIsCompletedWithTheCodeTyped()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var begun = await BeginAsync(admin, ConnectionLevel.Server, SignInKind.OAuthCode);
        Assert.AreEqual("code", begun.Fields!.Single().Id);

        using var wrong = await CompleteAsync(admin, begun.State, "bad");
        var again = await BeginAsync(admin, ConnectionLevel.Server, SignInKind.OAuthCode);
        using var right = await CompleteAsync(admin, again.State, "good:erin");

        Assert.AreEqual(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, right.StatusCode, await right.Content.ReadAsStringAsync(Cancel));
        var made = await SingleConnectionAsync(factory);
        Assert.AreEqual(ConnectionLevel.Server, made.Level);
        Assert.IsNull(made.OwnerId);
        Assert.DoesNotContain("erin", (await right.Content.ReadAsStringAsync(Cancel)).Replace("erin@fake", ""));
    }

    [TestMethod]
    public async Task ItsOwnerSignsAConnectionInAgainThroughTheRedirect()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var alice = await AdministratorOfTheLibraryAsync(factory);
        var begun = await BeginAsync(admin, ConnectionLevel.Account, SignInKind.OAuthRedirect);
        await CallbackAsync(admin, $"code=good:dave&state={Uri.EscapeDataString(begun.State!)}");
        var made = await SingleConnectionAsync(factory);
        var reauthorize = $"{V1}/connections/{made.Id}/reauthorize";
        var again = new SignInAgain(SignInKind.OAuthRedirect);

        // Another account's connection, even for a library administrator: not found.
        using var foreign = await alice.PostAsJsonAsync(reauthorize, again, Json, Cancel);
        using var mine = await admin.PostAsJsonAsync(reauthorize, again, Json, Cancel);
        var state = (await mine.Content.ReadFromJsonAsync<SignInBegun>(Json, Cancel))!.State!;
        var landed = await CallbackAsync(admin, $"code=good:zoe&state={Uri.EscapeDataString(state)}");

        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.AreEqual($"{ConnectionSignInEndpoints.ClientPage}?reconnected={made.Id}&otherAccount=true", landed);
        var after = await SingleConnectionAsync(factory);
        Assert.AreEqual(made.Id, after.Id);
        Assert.AreEqual("zoe@fake", after.Account);
    }

    [TestMethod]
    public async Task ALevelTheAccountMayNotMakeIsRefused()
    {
        await using var factory = Factory();
        await ApiClients.SetUpAsync(factory, Cancel);
        var alice = await AdministratorOfTheLibraryAsync(factory);
        var users = await UserAsync(factory);

        using var server = await alice.PostAsJsonAsync(
            Authorize,
            Request(ConnectionLevel.Server, SignInKind.OAuthCode),
            Json,
            Cancel
        );
        using var library = await alice.PostAsJsonAsync(
            Authorize,
            Request(ConnectionLevel.Library, SignInKind.OAuthCode),
            Json,
            Cancel
        );
        using var none = await users.PostAsJsonAsync(
            Authorize,
            Request(ConnectionLevel.Account, SignInKind.OAuthCode),
            Json,
            Cancel
        );
        using var invalid = await alice.PostAsJsonAsync(
            Authorize,
            Request(ConnectionLevel.Account, SignInKind.Key),
            Json,
            Cancel
        );

        Assert.AreEqual(HttpStatusCode.Forbidden, server.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, library.StatusCode, await library.Content.ReadAsStringAsync(Cancel));
        Assert.AreEqual(HttpStatusCode.Forbidden, none.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static ConnectRequest Request(ConnectionLevel level, SignInKind method) =>
        new(FakeProvider.FakeKind, level, "Mine", method);

    private async Task<SignInBegun> BeginAsync(HttpClient client, ConnectionLevel level, SignInKind method)
    {
        using var response = await client.PostAsJsonAsync(Authorize, Request(level, method), Json, Cancel);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<SignInBegun>(Json, Cancel))!;
    }

    private Task<HttpResponseMessage> CompleteAsync(HttpClient client, string? state, string code) =>
        client.PostAsJsonAsync(
            $"{Authorize}/complete",
            new SignInCompletionInput(state, new() { ["code"] = code }),
            Json,
            Cancel
        );

    /// <summary>
    /// The provider's redirect, as a browser makes it (a navigation: no <see cref="RequestGuard.Header"/>); answers
    /// where it ended in the client (path and query). Unversioned for good: the address registered at the provider.
    /// </summary>
    private async Task<string> CallbackAsync(HttpClient client, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiRoutes.OAuthCallback}?{query}");
        client.DefaultRequestHeaders.Remove(RequestGuard.Header);
        try
        {
            using var response = await client.SendAsync(request, Cancel);
            return response.RequestMessage!.RequestUri!.PathAndQuery;
        }
        finally
        {
            client.DefaultRequestHeaders.Add(RequestGuard.Header, "1");
        }
    }

    private async Task<HttpClient> AdministratorOfTheLibraryAsync(WebApplicationFactory<Program> factory)
    {
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var admins = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Administrators, Cancel))!;
        await accounts.CreateUserAsync(new("alice", Password: "alice-password", GroupIds: [admins.Id]), null, Cancel);
        return await ApiClients.SignInAsync(factory, "alice", "alice-password", Cancel);
    }

    private async Task<HttpClient> UserAsync(WebApplicationFactory<Program> factory)
    {
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Cancel))!;
        await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [users.Id]), null, Cancel);
        return await ApiClients.SignInAsync(factory, "bob", "bob-password-1", Cancel);
    }

    private Task<AccessDbContext> DbAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Cancel);

    private async Task<Connection> SingleConnectionAsync(WebApplicationFactory<Program> factory)
    {
        await using var db = await DbAsync(factory);
        return await db.Connections.SingleAsync(Cancel);
    }
}
