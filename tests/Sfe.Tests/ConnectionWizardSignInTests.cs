using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// What the add-a-connection wizard asks of <c>api/connections/authorize</c>: its checks alone
/// (<c>validateOnly</c>, a password tried at the provider), the groups given with the connection in the same save, and
/// the provider's redirect brought back to the wizard.
/// </summary>
[TestClass]
public sealed class ConnectionWizardSignInTests
{
    private const string Authorize = $"{V1}/connections/authorize";
    private const string Check = $"{Authorize}?validateOnly=true";

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

    [TestMethod]
    public async Task TheRedirectComesBackToTheWizardWithTheGroupsSavedWithTheConnection()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var (tenant, users) = await UsersGroupAsync(factory);
        var request = Request(ConnectionLevel.Server, SignInKind.OAuthRedirect) with
        {
            Groups = new Dictionary<int, FolderLevel> { [users] = FolderLevel.Read },
            Return = SignInReturn.Wizard,
        };

        var begun = await BeginAsync(admin, request);
        await using (var before = await DbAsync(factory))
            Assert.IsFalse(await before.Connections.AnyAsync(Cancel));
        var landed = await CallbackAsync(admin, $"code=good:dave&state={Uri.EscapeDataString(begun.State!)}");

        await using var db = await DbAsync(factory);
        var made = await db.Connections.Include(c => c.Groups).Include(c => c.Tenants).SingleAsync(Cancel);
        Assert.AreEqual($"/wizard?id=add-connection&made={made.Id}", landed);
        Assert.AreEqual(users, made.Groups.Single().GroupId);
        Assert.AreEqual(FolderLevel.Read, made.Groups.Single().Level);
        // A server-level one is offered to the library whose groups use it.
        Assert.AreEqual(tenant, made.Tenants.Single().TenantId);
    }

    [TestMethod]
    public async Task ARefusalAtTheProviderComesBackToTheWizard()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var request = Request(ConnectionLevel.Account, SignInKind.OAuthRedirect) with { Return = SignInReturn.Wizard };
        var begun = await BeginAsync(admin, request);
        var state = Uri.EscapeDataString(begun.State!);

        Assert.AreEqual(
            "/wizard?id=add-connection&error=refused",
            await CallbackAsync(admin, $"error=x&state={state}")
        );
        // Spent: where a late answer lands is no longer known.
        Assert.AreEqual(
            $"{ConnectionSignInEndpoints.ClientPage}?error=expired",
            await CallbackAsync(admin, $"code=good:x&state={state}")
        );
    }

    [TestMethod]
    public async Task OnlyTheChecksRunWithValidateOnly()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        var (_, users) = await UsersGroupAsync(factory);
        var groups = new Dictionary<int, FolderLevel> { [users] = FolderLevel.Write };

        using var redirect = await PostAsync(admin, Request(ConnectionLevel.Library, SignInKind.OAuthRedirect));
        using var unnamed = await PostAsync(admin, Request(ConnectionLevel.Library, SignInKind.OAuthRedirect, ""));
        using var mine = await PostAsync(
            admin,
            Request(ConnectionLevel.Account, SignInKind.OAuthRedirect) with
            {
                Groups = groups,
            }
        );
        using var stranger = await PostAsync(
            admin,
            Request(ConnectionLevel.Library, SignInKind.OAuthRedirect) with
            {
                Groups = new Dictionary<int, FolderLevel> { [999] = FolderLevel.Read },
            }
        );

        Assert.AreEqual(
            HttpStatusCode.NoContent,
            redirect.StatusCode,
            await redirect.Content.ReadAsStringAsync(Cancel)
        );
        Assert.AreEqual("name", await RefusedFieldAsync(unnamed));
        Assert.AreEqual("groups", await RefusedFieldAsync(mine));
        Assert.AreEqual("groups", await RefusedFieldAsync(stranger));
        Assert.AreEqual(0, factory.Services.GetRequiredService<PendingSignIns>().Count);
    }

    [TestMethod]
    public async Task APasswordIsTriedAtTheProviderWithoutMakingAnything()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);
        ConnectRequest WithPassword(string password) =>
            Request(ConnectionLevel.Account, SignInKind.Password) with
            {
                Fields = new Dictionary<string, string> { ["name"] = "dave", ["password"] = password },
            };

        using var wrong = await PostAsync(admin, WithPassword("nope"));
        using var right = await PostAsync(admin, WithPassword("secret"));

        Assert.AreEqual(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, right.StatusCode, await right.Content.ReadAsStringAsync(Cancel));
        await using var db = await DbAsync(factory);
        Assert.IsFalse(await db.Connections.AnyAsync(Cancel));
    }

    private WebApplicationFactory<Program> Factory() =>
        ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(new FakeProvider()))
            );

    private static ConnectRequest Request(ConnectionLevel level, SignInKind method, string name = "Shared") =>
        new(FakeProvider.FakeKind, level, name, method);

    private Task<HttpResponseMessage> PostAsync(HttpClient client, ConnectRequest request) =>
        client.PostAsJsonAsync(Check, request, Json, Cancel);

    private async Task<string> RefusedFieldAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        return problem.RootElement.GetProperty("errors").EnumerateObject().Single().Name;
    }

    private async Task<SignInBegun> BeginAsync(HttpClient client, ConnectRequest request)
    {
        using var response = await client.PostAsJsonAsync(Authorize, request, Json, Cancel);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<SignInBegun>(Json, Cancel))!;
    }

    /// <summary>The provider's redirect, as a browser makes it; answers where it ended in the client.</summary>
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

    private async Task<(int Tenant, int Users)> UsersGroupAsync(WebApplicationFactory<Program> factory)
    {
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Cancel))!;
        return (tenant, users.Id);
    }

    private Task<AccessDbContext> DbAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Cancel);
}
