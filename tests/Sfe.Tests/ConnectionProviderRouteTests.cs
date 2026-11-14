using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// How a kind of connection is set up (<c>api/connections/providers/{kind}</c>): Dropbox's app key as a server
/// setting, its secret written only (a private file, never in an answer or <c>settings.json</c>), the return address
/// to register, the test.
/// </summary>
[TestClass]
public sealed class ConnectionProviderRouteTests
{
    private const string Dropbox = $"{V1}/connections/providers/dropbox";

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

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null) =>
        ApiClients
            .CreateFactory(_library, configure)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(new FakeProvider()))
            );

    [TestMethod]
    public async Task DropboxIsOfferedOnceItHasAnAppKey()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        var before = await admin.GetFromJsonAsync<ProviderSetupView>(Dropbox, Cancel);
        Assert.IsFalse(before!.Offered);
        Assert.AreEqual(ProviderSetupView.NoSecret, before.AppSecret);
        // The return address registered at the provider: unversioned for good.
        StringAssert.EndsWith(before.ReturnAddress, ApiRoutes.OAuthCallback);
        Assert.IsNull(
            (await admin.GetFromJsonAsync<ProviderSetupView>($"{V1}/connections/providers/fake", Cancel))!.AppSecret
        );
        Assert.AreEqual(
            HttpStatusCode.NotFound,
            (await admin.GetAsync($"{V1}/connections/providers/nope", Cancel)).StatusCode
        );

        using var wrong = await PatchAsync(admin, new() { ["connections.dropbox.appKey"] = "not a key!" });
        using var saved = await PatchAsync(admin, new() { ["connections.dropbox.appKey"] = " abc123 " });

        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);
        StringAssert.Contains(await wrong.Content.ReadAsStringAsync(Cancel), "connections.dropbox.appKey");
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Cancel));
        Assert.IsTrue((await admin.GetFromJsonAsync<ProviderSetupView>(Dropbox, Cancel))!.Offered);
        using var config = JsonDocument.Parse(await admin.GetStringAsync($"{V1}/config", Cancel));
        CollectionAssert.Contains(
            config
                .RootElement.GetProperty("connections")
                .GetProperty("kinds")
                .EnumerateArray()
                .Select(k => k.GetProperty("kind").GetString())
                .ToArray(),
            "dropbox"
        );
        Assert.AreEqual(SettingLevel.Server, SettingCatalog.Find("connections.dropbox.appKey")!.Scope);
    }

    [TestMethod]
    public async Task TheSecretIsWrittenButNeverReadBack()
    {
        const string secret = "very-secret-value";
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        using var empty = await admin.PutAsJsonAsync($"{Dropbox}/secret", new AppSecretInput(" "), Cancel);
        using var fake = await admin.PutAsJsonAsync(
            $"{V1}/connections/providers/fake/secret",
            new AppSecretInput("x"),
            Cancel
        );
        using var set = await admin.PutAsJsonAsync($"{Dropbox}/secret", new AppSecretInput(secret), Cancel);

        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, fake.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, set.StatusCode);
        var status = await admin.GetStringAsync(Dropbox, Cancel);
        StringAssert.Contains(status, $"\"{ProviderSetupView.SecretOnPage}\"");
        foreach (var answer in new[] { status, await admin.GetStringAsync($"{V1}/settings?level=server", Cancel) })
            Assert.DoesNotContain(secret, answer);
        Assert.IsFalse(File.Exists(_library.SettingsPath) && File.ReadAllText(_library.SettingsPath).Contains(secret));
        var secrets = factory.Services.GetRequiredService<ProviderSecrets>();
        Assert.AreEqual(secret, secrets.Get("dropbox"));
        Assert.IsTrue(secrets.Directory.StartsWith(_library.BasePath, StringComparison.OrdinalIgnoreCase));

        using var removed = await admin.DeleteAsync($"{Dropbox}/secret", Cancel);
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.AreEqual(
            ProviderSetupView.NoSecret,
            (await admin.GetFromJsonAsync<ProviderSetupView>(Dropbox, Cancel))!.AppSecret
        );
    }

    [TestMethod]
    public async Task ASecretInTheConfigurationWins()
    {
        await using var factory = Factory(settings => settings["Sfe:Connections:Dropbox:AppSecret"] = "from-config");
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        var status = await admin.GetFromJsonAsync<ProviderSetupView>(Dropbox, Cancel);

        Assert.AreEqual(ProviderSetupView.SecretInConfiguration, status!.AppSecret);
    }

    [TestMethod]
    public async Task TheTestAnswersWhatTheProviderFound()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        using var fake = await admin.PostAsync($"{V1}/connections/providers/fake/test", null, Cancel);
        using var dropbox = await admin.PostAsync($"{Dropbox}/test", null, Cancel);

        Assert.IsTrue((await fake.Content.ReadFromJsonAsync<ProviderTest>(Cancel))!.Ok);
        // No app key: nothing is sent to Dropbox, and the test says why.
        Assert.IsFalse((await dropbox.Content.ReadFromJsonAsync<ProviderTest>(Cancel))!.Ok);
    }

    [TestMethod]
    public async Task OnlyWhoMayChangeTheServerSettingsSeesThem()
    {
        await using var factory = Factory();
        await ApiClients.SetUpAsync(factory, Cancel);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var admins = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Administrators, Cancel))!;
        await accounts.CreateUserAsync(new("alice", Password: "alice-password", GroupIds: [admins.Id]), null, Cancel);
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", Cancel);

        // A library's administrator, not the server's.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await alice.GetAsync(Dropbox, Cancel)).StatusCode);
        using var put = await alice.PutAsJsonAsync($"{Dropbox}/secret", new AppSecretInput("x"), Cancel);
        Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await alice.PostAsync($"{Dropbox}/test", null, Cancel)).StatusCode);
    }

    private Task<HttpResponseMessage> PatchAsync(HttpClient client, Dictionary<string, JsonNode?> values) =>
        client.PatchAsJsonAsync($"{V1}/settings?level=server", new SettingChanges(values, null, null), Cancel);
}
