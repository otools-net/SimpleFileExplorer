using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Who runs the server decides about connections: the <c>connections.*</c> settings (server level, lockable), the
/// rights that come with them, and what <c>api/config</c> tells the web client.
/// </summary>
[TestClass]
public sealed class ConnectionSettingsTests
{
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

    /// <summary>A server that knows the fake kind of storage, with changes to its configuration.</summary>
    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null) =>
        ApiClients
            .CreateFactory(_library, configure)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(new FakeProvider()))
            );

    private async Task<JsonElement> ConnectionsOfAsync(HttpClient client)
    {
        using var config = JsonDocument.Parse(await client.GetStringAsync($"{V1}/config", Cancel));
        return config.RootElement.GetProperty("connections").Clone();
    }

    [TestMethod]
    public async Task TheConfigTellsAdministratorsTheKindsAndLevels()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        var connections = await ConnectionsOfAsync(admin);

        CollectionAssert.AreEqual(
            new[] { "account", "library", "server" },
            connections.GetProperty("levels").EnumerateArray().Select(l => l.GetString()).ToArray()
        );
        var kind = connections.GetProperty("kinds").EnumerateArray().Single();
        Assert.AreEqual(FakeProvider.FakeKind, kind.GetProperty("kind").GetString());
        Assert.AreEqual("oAuthRedirect", kind.GetProperty("signIn")[0].GetProperty("kind").GetString());
        Assert.IsTrue(kind.GetProperty("capabilities").GetProperty("caseInsensitiveNames").GetBoolean());
    }

    [TestMethod]
    public async Task SomeoneWithoutAConnectionRightIsToldNothing()
    {
        await using var factory = Factory();
        await ApiClients.SetUpAsync(factory, Cancel);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Cancel))!;
        await accounts.CreateUserAsync(new("alice", Password: "alice-password", GroupIds: [users.Id]), null, Cancel);
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", Cancel);

        Assert.AreEqual(JsonValueKind.Null, (await ConnectionsOfAsync(alice)).ValueKind);
        // New rights are for administrators only: the Users group got none of them.
        Assert.IsFalse(users.Rights.Any(r => r.StartsWith("connections.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TheConfigurationTurnsConnectionsOffOrNamesTheKinds()
    {
        await using (var off = Factory(settings => settings["Sfe:Connections:Enabled"] = "false"))
        {
            var admin = await ApiClients.SetUpAsync(off, Cancel);
            Assert.AreEqual(JsonValueKind.Null, (await ConnectionsOfAsync(admin)).ValueKind);
        }

        await using var others = Factory(settings => settings["Sfe:Connections:Providers:0"] = "dropbox");
        var signedIn = await ApiClients.SignInAsync(others, ApiClients.AdminName, ApiClients.AdminPassword, Cancel);
        Assert.AreEqual(0, (await ConnectionsOfAsync(signedIn)).GetProperty("kinds").GetArrayLength());
    }

    [TestMethod]
    public async Task TheSettingsAreSavedCleanedAndValidated()
    {
        await using var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        using var wrongLevel = await PatchAsync(admin, new() { ["connections.levels"] = new JsonArray("everyone") });
        using var wrongKind = await PatchAsync(admin, new() { ["connections.providers"] = new JsonArray("Drop box!") });
        using var saved = await PatchAsync(
            admin,
            new()
            {
                ["connections.levels"] = new JsonArray(" Account ", "library", "account"),
                ["connections.providers"] = new JsonArray("FAKE"),
            }
        );

        Assert.AreEqual(HttpStatusCode.BadRequest, wrongLevel.StatusCode);
        StringAssert.Contains(await wrongLevel.Content.ReadAsStringAsync(Cancel), "connections.levels");
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Cancel));
        var connections = await ConnectionsOfAsync(admin);
        CollectionAssert.AreEqual(
            new[] { "account", "library" },
            connections.GetProperty("levels").EnumerateArray().Select(l => l.GetString()).ToArray()
        );
        Assert.AreEqual(1, connections.GetProperty("kinds").GetArrayLength());

        using var off = await PatchAsync(admin, new() { ["connections.enabled"] = false });
        Assert.AreEqual(HttpStatusCode.OK, off.StatusCode);
        Assert.AreEqual(JsonValueKind.Null, (await ConnectionsOfAsync(admin)).ValueKind);
    }

    [TestMethod]
    public async Task TheEnvironmentLocksThem()
    {
        await using var factory = Factory(settings =>
        {
            settings["Sfe:Connections:Enabled"] = "false";
            settings["Sfe:Locked:0"] = "connections.*";
        });
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        using var change = await PatchAsync(admin, new() { ["connections.enabled"] = true });

        Assert.AreEqual(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.AreEqual(JsonValueKind.Null, (await ConnectionsOfAsync(admin)).ValueKind);
    }

    [TestMethod]
    public void TheRightsAreNewNeverGuestsAndForAdministrators()
    {
        string[] space = [Rights.UseConnections, Rights.OwnConnections, Rights.LibraryConnections];

        foreach (var definition in space.Select(id => RightCatalog.Find(id)!))
        {
            Assert.AreEqual(RightScope.Tenant, definition.Scope, definition.Id);
            Assert.IsFalse(definition.GuestAllowed, definition.Id);
            CollectionAssert.AreEqual(new[] { BuiltInGroup.Administrators }, definition.DefaultGroups.ToArray());
            // Existing spaces get them once, by their revision.
            Assert.IsGreaterThan(14, definition.Since, definition.Id);
            Assert.IsNull(definition.AtFolder, definition.Id);
        }
        Assert.AreEqual(RightScope.Server, RightCatalog.Find(Rights.ServerConnections)!.Scope);
        Assert.AreEqual(Rights.ServerSettings, SettingCatalog.Find("connections.enabled")!.Right);
        Assert.AreEqual(SettingLevel.Server, SettingCatalog.Find("connections.levels")!.Scope);
    }

    private Task<HttpResponseMessage> PatchAsync(HttpClient client, Dictionary<string, JsonNode?> values) =>
        client.PatchAsJsonAsync($"{V1}/settings?level=server", new SettingChanges(values, null, null), Cancel);
}
