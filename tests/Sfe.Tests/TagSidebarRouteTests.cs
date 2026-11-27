using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>GET api/tags/sidebar</c> shows the lists switched on by the user's own preferences, else by the library's
/// settings.
/// </summary>
[TestClass]
public sealed class TagSidebarRouteTests
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
    public async Task TheUsersOwnChoiceWinsOverTheLibrarys()
    {
        using (
            var tagged = await _admin.PostAsJsonAsync(
                $"{V1}/file-tags",
                new FileTagChange(["RFile1.txt"], Add: [BuiltInTags.Favorite]),
                Json,
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.OK, tagged.StatusCode);
        }

        var byDefault = await SidebarAsync();
        await PatchAsync($"{V1}/settings?level=tenant", "tags.sidebarPopular", true);
        var library = await SidebarAsync();
        await PatchAsync($"{V1}/me/preferences", "tags.sidebarPopular", false);
        await PatchAsync($"{V1}/me/preferences", "tags.sidebarLucky", true);
        var own = await SidebarAsync();

        Assert.IsFalse(byDefault.ShowPopular);
        Assert.IsEmpty(byDefault.Popular);
        Assert.IsTrue(library.ShowPopular);
        Assert.AreEqual("Favorite", library.Popular.Single().Tag.Name);
        Assert.IsFalse(own.ShowPopular, "The user switched it off for themselves");
        Assert.IsEmpty(own.Popular);
        Assert.IsTrue(own.ShowLucky);
        Assert.IsNotEmpty(own.Lucky);
    }

    private async Task<TagSidebar> SidebarAsync() =>
        (await _admin.GetFromJsonAsync<TagSidebar>($"{V1}/tags/sidebar", Json, Token))!;

    private async Task PatchAsync(string url, string key, bool value)
    {
        using var response = await _admin.PatchAsJsonAsync(
            url,
            new SettingChanges(new Dictionary<string, JsonNode?> { [key] = value }, null, null),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }
}
