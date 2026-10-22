using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary><c>api/keep-alive</c> and the <c>keepAlive.*</c> settings of a space.</summary>
[TestClass]
public sealed class KeepAliveRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _ada = null!;
    private HttpClient _alice = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        using var admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var administrators = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Administrators, Token))!;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        await accounts.CreateUserAsync(new UserInput("ada", Password: "ada-password-1", GroupIds: [administrators.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        _ada = await ApiClients.SignInAsync(_factory, "ada", "ada-password-1", Token);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _alice?.Dispose();
        _ada?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public void TheSettingsBelongToTheSpaceUnderTheirOwnRight()
    {
        foreach (var key in new[] { "keepAlive.mode", "keepAlive.intervalSeconds", "keepAlive.timeoutSeconds" })
        {
            var definition = SettingCatalog.Find(key);
            Assert.IsNotNull(definition, key);
            Assert.AreEqual(SettingLevel.Tenant, definition.Scope, key);
            Assert.AreEqual(Rights.KeepAliveSettings, definition.Right, key);
        }

        var right = RightCatalog.Find(Rights.KeepAliveSettings)!;
        Assert.AreEqual(RightScope.Tenant, right.Scope);
        CollectionAssert.AreEqual(new[] { BuiltInGroup.Administrators }, right.DefaultGroups.ToArray());
        Assert.AreEqual(RightCatalog.Revision, right.Since, "a right added after 1.34 is given to existing spaces");
    }

    [TestMethod]
    public async Task SpaceAdministratorsSeeAndPingTheLibraryFolder()
    {
        var view = await _ada.GetFromJsonAsync<KeepAliveView>("/api/keep-alive", Json, Token);
        Assert.AreEqual(KeepAliveMode.Off, view!.Mode);
        Assert.IsEmpty(view.Folders);

        // Ping now works while it is off, to try a share first.
        using var pinged = await _ada.PostAsync("/api/keep-alive/ping", null, Token);
        Assert.AreEqual(HttpStatusCode.OK, pinged.StatusCode);
        var folder = (await pinged.Content.ReadFromJsonAsync<KeepAliveView>(Json, Token))!.Folders.Single();
        Assert.AreEqual("", folder.Path);
        Assert.IsFalse(folder.IsNetwork);
        Assert.IsNotNull(folder.LastPing);
        Assert.IsNull(folder.Error);
        Assert.IsFalse(folder.Waiting);

        Assert.HasCount(1, (await _ada.GetFromJsonAsync<KeepAliveView>("/api/keep-alive", Json, Token))!.Folders);
    }

    [TestMethod]
    public async Task OthersMayNotSeeOrPing()
    {
        using var get = await _alice.GetAsync("/api/keep-alive", Token);
        using var ping = await _alice.PostAsync("/api/keep-alive/ping", null, Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, ping.StatusCode);
    }

    [TestMethod]
    public async Task TheSpaceSettingsAreCheckedAndApplied()
    {
        using (var invalid = await PatchAsync(new() { ["keepAlive.intervalSeconds"] = 5, ["keepAlive.timeoutSeconds"] = 0 }))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            var body = await invalid.Content.ReadAsStringAsync(Token);
            StringAssert.Contains(body, "keepAlive.intervalSeconds");
            StringAssert.Contains(body, "keepAlive.timeoutSeconds");
        }

        using (var saved = await PatchAsync(new() { ["keepAlive.mode"] = "always", ["keepAlive.intervalSeconds"] = 60 }))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Token));

        var view = await _ada.GetFromJsonAsync<KeepAliveView>("/api/keep-alive", Json, Token);
        Assert.AreEqual(KeepAliveMode.Always, view!.Mode);
        Assert.AreEqual(60, view.IntervalSeconds);
    }

    [TestMethod]
    public async Task MoreFoldersAndTheWritePingAreSetPerSpace()
    {
        Assert.IsTrue(SettingCatalog.Find("keepAlive.folders")!.PerLibrary, "library paths have no server-wide value");
        Assert.AreEqual(SettingLevel.Tenant, SettingCatalog.Find("keepAlive.method")!.Scope);

        foreach (var refused in new[] { ".sfe", "../Elsewhere" })
        {
            using var invalid = await PatchAsync(new() { ["keepAlive.folders"] = new JsonArray(refused) });
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode, refused);
            StringAssert.Contains(await invalid.Content.ReadAsStringAsync(Token), "keepAlive.folders", refused);
        }

        using (var saved = await PatchAsync(new() { ["keepAlive.folders"] = new JsonArray("ChildDir/"), ["keepAlive.method"] = "touch" }))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Token));

        using var pinged = await _ada.PostAsync("/api/keep-alive/ping", null, Token);
        var view = (await pinged.Content.ReadFromJsonAsync<KeepAliveView>(Json, Token))!;
        Assert.AreEqual(KeepAliveMethod.Touch, view.Method);
        CollectionAssert.AreEqual(
            new[] { ("", true, (KeepAliveMethod?)KeepAliveMethod.Touch), ("ChildDir", false, KeepAliveMethod.List) },
            view.Folders.Select(f => (f.Path, f.IsRoot, f.Method)).ToList());
        Assert.IsTrue(view.Folders.All(f => f.Error is null), string.Join(", ", view.Folders.Select(f => f.Error)));
        Assert.IsTrue(Directory.Exists(_library.FullPath(".sfe/keep-alive")));
    }

    private Task<HttpResponseMessage> PatchAsync(Dictionary<string, JsonNode?> values) =>
        _ada.PatchAsJsonAsync("/api/settings?level=tenant", new SettingChanges(values), Json, Token);
}
