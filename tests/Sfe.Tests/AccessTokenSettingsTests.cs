using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The <c>security.accessTokens.*</c> settings: their defaults, server level for its administrators, checked,
/// lockable from the configuration, and what <see cref="AccessTokens"/> reads.
/// </summary>
[TestClass]
public sealed class AccessTokenSettingsTests
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

    [TestMethod]
    public void TheDefaultsAllowTokensForAYearTwentyPerAccount()
    {
        var options = new SfeOptions().Security.AccessTokens;
        var settings = EditableSettings.From(new SfeOptions()).Security.AccessTokens;

        Assert.AreEqual((true, 365, 20), (options.Enabled, options.MaxLifetimeDays, options.MaxPerAccount));
        Assert.AreEqual((true, 365, 20), (settings.Enabled, settings.MaxLifetimeDays, settings.MaxPerAccount));
        foreach (var key in new[] { "enabled", "maxLifetimeDays", "maxPerAccount" })
        {
            var definition = SettingCatalog.Find($"security.accessTokens.{key}")!;
            Assert.AreEqual((SettingLevel.Server, Rights.ServerSettings), (definition.Scope, definition.Right), key);
        }
    }

    [TestMethod]
    public async Task TheSettingsAreSavedAndChecked()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        using var none = await PatchAsync(admin, new() { ["security.accessTokens.maxPerAccount"] = 0 });
        using var tooLong = await PatchAsync(admin, new() { ["security.accessTokens.maxLifetimeDays"] = 3651 });
        using var saved = await PatchAsync(
            admin,
            new()
            {
                ["security.accessTokens.enabled"] = false,
                ["security.accessTokens.maxLifetimeDays"] = 0,
                ["security.accessTokens.maxPerAccount"] = 5,
            }
        );

        Assert.AreEqual(HttpStatusCode.BadRequest, none.StatusCode);
        StringAssert.Contains(await none.Content.ReadAsStringAsync(Cancel), "security.accessTokens.maxPerAccount");
        Assert.AreEqual(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Cancel));
        var options = factory.Services.GetRequiredService<AccessTokens>().Options;
        Assert.AreEqual((false, 0, 5), (options.Enabled, options.MaxLifetimeDays, options.MaxPerAccount));
    }

    [TestMethod]
    public async Task TheEnvironmentLocksThem()
    {
        await using var factory = ApiClients.CreateFactory(_library, settings =>
        {
            settings["Sfe:Security:AccessTokens:Enabled"] = "false";
            settings["Sfe:Locked:0"] = "security.accessTokens.*";
        });
        var admin = await ApiClients.SetUpAsync(factory, Cancel);

        using var change = await PatchAsync(admin, new() { ["security.accessTokens.enabled"] = true });

        Assert.AreEqual(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.IsFalse(factory.Services.GetRequiredService<AccessTokens>().Options.Enabled);
    }

    private Task<HttpResponseMessage> PatchAsync(HttpClient client, Dictionary<string, JsonNode?> values) =>
        client.PatchAsJsonAsync($"{V1}/settings?level=server", new SettingChanges(values, null, null), Cancel);
}
