using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>browse.startPage</c>: the dashboard unless chosen otherwise, a library's default that each account may change,
/// answered by <c>GET api/config</c> as <c>startPage</c>.
/// </summary>
[TestClass]
public sealed class StartPageTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _admin.Dispose();
        _factory.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public void ItIsAUserSetting() =>
        Assert.AreEqual(SettingLevel.User, SettingCatalog.Find("browse.startPage")!.Scope);

    [TestMethod]
    public async Task TheDashboardOpensUnlessTheLibraryOrTheUserChoseAnother()
    {
        var byDefault = await StartPageAsync();
        await PatchAsync($"{V1}/settings?level=tenant", "home", HttpStatusCode.OK);
        var library = await StartPageAsync();
        await PatchAsync($"{V1}/me/preferences", "root", HttpStatusCode.OK);
        var own = await StartPageAsync();

        Assert.AreEqual("dashboard", byDefault);
        Assert.AreEqual("home", library);
        Assert.AreEqual("root", own, "The user's own choice wins over the library's");
    }

    [TestMethod]
    public async Task APageNotOnTheListIsRefused()
    {
        await PatchAsync($"{V1}/me/preferences", "everything", HttpStatusCode.BadRequest);

        Assert.AreEqual("dashboard", await StartPageAsync());
    }

    private async Task<string?> StartPageAsync()
    {
        var config = await _admin.GetFromJsonAsync<JsonObject>($"{V1}/config", Json, Token);
        return config!["startPage"]?.GetValue<string>();
    }

    private async Task PatchAsync(string url, string value, HttpStatusCode expected)
    {
        using var response = await _admin.PatchAsJsonAsync(
            url,
            new SettingChanges(new Dictionary<string, JsonNode?> { ["browse.startPage"] = value }, null, null),
            Json,
            Token
        );
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }
}
