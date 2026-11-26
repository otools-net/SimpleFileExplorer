using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="SettingLayers.ServerLevel"/> is kept between calls (each request made with an access token asks it),
/// and resolved again once the server settings are saved or reset, or the configuration reloads.
/// </summary>
[TestClass]
public sealed class ServerLevelCacheTests
{
    private const string Key = "security.accessTokens.maxPerAccount";
    private const string ConfigurationKey = "Sfe:Security:AccessTokens:MaxPerAccount";

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
    public async Task TheSameSettingsAnswerUntilSomethingChanges()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        var layers = factory.Services.GetRequiredService<SettingLayers>();

        Assert.AreSame(layers.ServerLevel(), layers.ServerLevel());
    }

    [TestMethod]
    public async Task ASaveOrAResetOfTheServerSettingsIsSeenAtOnce()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        var layers = factory.Services.GetRequiredService<SettingLayers>();
        var before = layers.ServerLevel().Security.AccessTokens.MaxPerAccount;

        await layers.Server.SaveLayerAsync(new Dictionary<string, JsonNode?> { [Key] = 5 }, [], Cancel);
        var saved = layers.ServerLevel().Security.AccessTokens.MaxPerAccount;
        layers.Server.Reset();
        var reset = layers.ServerLevel().Security.AccessTokens.MaxPerAccount;

        Assert.AreEqual((20, 5, 20), (before, saved, reset));
    }

    [TestMethod]
    public async Task AReloadOfTheConfigurationIsSeen()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        var layers = factory.Services.GetRequiredService<SettingLayers>();
        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
        var before = layers.ServerLevel().Security.AccessTokens.MaxPerAccount;

        configuration[ConfigurationKey] = "7";
        configuration.Reload();

        Assert.AreEqual((20, 7), (before, layers.ServerLevel().Security.AccessTokens.MaxPerAccount));
    }
}
