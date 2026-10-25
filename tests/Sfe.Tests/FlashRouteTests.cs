using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Flash movies through HTTP (<c>api/files/flash-links</c>, <c>api/files/flash/{token}/…</c>) and the headers of the
/// player's frame. Alice is in Users, with <c>Games/Run/secret</c> and SpecialDir hidden from them; Bob may browse but
/// not open files. The movie's files are fetched by an anonymous client: the player's frame has no cookie.
/// </summary>
[TestClass]
public sealed class FlashRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private HttpClient _frame = null!;
    private int _aliceId;
    private int _users;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _library = new TestLibrary();
        foreach (var file in new[] { "Games/Run/game.swf", "Games/Run/level2.swf", "Games/Run/data/map.xml", "Games/Run/secret/key.txt", "Games/other.swf", "SpecialDir/hidden.swf", "Games/Run/notes.txt" })
            _library.AddFile(file, 300);
    }

    private async Task StartAsync(Action<IDictionary<string, string?>>? configure = null)
    {
        _factory = ApiClients.CreateFactory(_library, configure);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        _users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!.Id;
        var browsers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Browsers", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        _aliceId = (await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [_users]), null, Token)).Value!.Id;
        await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-1", GroupIds: [browsers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "Games/Run/secret", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token)).Status);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);
        _frame = ApiClients.Create(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _frame?.Dispose();
        _bob?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AMovieLoadsTheFilesOfItsFolderWithoutACookie()
    {
        await StartAsync();
        var link = await LinkAsync(_alice, "Games/Run/game.swf");
        StringAssert.StartsWith(link.Url, "api/files/flash/");
        StringAssert.EndsWith(link.Url, "/game.swf");
        var folder = "/" + link.Url[..(link.Url.LastIndexOf('/') + 1)];

        using (var movie = await _frame.GetAsync("/" + link.Url, Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, movie.StatusCode);
            Assert.AreEqual("*", movie.Headers.GetValues("Access-Control-Allow-Origin").Single(), "the player's frame has no origin");
            StringAssert.Contains(movie.Headers.GetValues("Content-Security-Policy").Single(), "sandbox");
            Assert.AreEqual(300, (await movie.Content.ReadAsByteArrayAsync(Token)).Length);
        }
        foreach (var sibling in new[] { "level2.swf", "data/map.xml", "notes.txt" })
            Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(_frame, folder + sibling), sibling);
        foreach (var outside in new[] { "%2e%2e/other.swf", "..%2Fother.swf", "data/%2e%2e/%2e%2e/other.swf", "secret/key.txt", "none.swf", "" })
            Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_frame, folder + outside), outside);
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_frame, "/api/files/flash/not-a-token/game.swf"));
    }

    [TestMethod]
    public async Task OnlyMoviesTheCallerMayOpenGetALink()
    {
        await StartAsync();

        using var bob = await _bob.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest("Games/Run/game.swf"), Json, Token);
        using var hidden = await _alice.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest("SpecialDir/hidden.swf"), Json, Token);
        using var text = await _alice.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest("Games/Run/notes.txt"), Json, Token);
        using var missing = await _alice.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest("Games/none.swf"), Json, Token);
        using var escape = await _alice.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest("../outside.swf"), Json, Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, bob.StatusCode, "Bob may not open files");
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, text.StatusCode, "only .swf files");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, escape.StatusCode);
    }

    [TestMethod]
    public async Task ALinkFollowsTheAccountAndTheRulesAsTheyAreNow()
    {
        await StartAsync();
        var link = await LinkAsync(_alice, "Games/Run/game.swf");
        var level = "/" + link.Url.Replace("/game.swf", "/level2.swf");

        await _factory.Services.GetRequiredService<FolderRuleService>().SetAsync(
            (await _factory.Services.GetRequiredService<AccessService>().GetTenantsAsync(Token)).Single(t => t.IsDefault).Id,
            "Games/Run", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_frame, level), "a rule made after the link hides the files from it");

        var other = await LinkAsync(_admin, "Games/other.swf");
        await _factory.Services.GetRequiredService<AccessService>().UpdateUserAsync(_aliceId, new UserInput("alice", Disabled: true, GroupIds: [_users]), null, Token);
        Assert.AreEqual(HttpStatusCode.Forbidden, await StatusAsync(_frame, "/" + link.Url), "a disabled account ends its links");
        Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(_frame, "/" + other.Url), "the administrator's link still works");
    }

    [TestMethod]
    public async Task AnExternalPlayersLinkDoesNotOpenAFolder()
    {
        await StartAsync();
        using var created = await _admin.PostAsJsonAsync("/api/files/links", new FileLinkRequest("Games/Run/game.swf"), Json, Token);
        var shared = (await created.Content.ReadFromJsonAsync<FileLink>(Json, Token))!;
        var token = shared.Url.Split('/')[3];

        Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(_frame, "/" + shared.Url));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_frame, $"/api/files/flash/{token}/game.swf"));
    }

    [TestMethod]
    public async Task WithFlashOffMoviesAreOnlyDownloaded()
    {
        await StartAsync();
        var link = await LinkAsync(_alice, "Games/Run/game.swf");
        Assert.IsTrue((await ConfigAsync(_alice)).GetProperty("flash").GetBoolean());
        Assert.IsFalse((await ConfigAsync(_bob)).GetProperty("flash").GetBoolean(), "Bob may not open files");

        var settings = (await _admin.GetFromJsonAsync<SettingsView>("/api/settings", Json, Token))!;
        Assert.IsTrue(settings.Values.Player.Flash, "on by default");
        settings.Values.Player.Flash = false;
        using (var off = await _admin.PutAsJsonAsync("/api/settings", settings.Values, Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, off.StatusCode, await off.Content.ReadAsStringAsync(Token));

        using var refused = await _alice.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest("Games/Run/game.swf"), Json, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_frame, "/" + link.Url), "links given before stop");
        Assert.IsFalse((await ConfigAsync(_alice)).GetProperty("flash").GetBoolean());
    }

    [TestMethod]
    public async Task OnlyThePlayersPageMayBeFramedAndRunWebAssembly()
    {
        await StartAsync();

        using var page = await _frame.GetAsync("/flash/player.html", Token);
        using var ruffle = await _frame.GetAsync("/flash/ruffle/ruffle.js", Token);
        using var app = await _frame.GetAsync("/", Token);
        using var api = await _alice.GetAsync("/api/config", Token);

        Assert.AreEqual("SAMEORIGIN", page.Headers.GetValues("X-Frame-Options").Single());
        Assert.AreEqual(SecurityHeaders.FlashPolicy, page.Headers.GetValues("Content-Security-Policy").Single());
        StringAssert.Contains(SecurityHeaders.FlashPolicy, "sandbox allow-scripts");
        Assert.IsFalse(SecurityHeaders.FlashPolicy.Contains("allow-same-origin"), "the frame must have no origin");
        Assert.AreEqual("*", ruffle.Headers.GetValues("Access-Control-Allow-Origin").Single());
        foreach (var other in new[] { app, api })
        {
            Assert.AreEqual("DENY", other.Headers.GetValues("X-Frame-Options").Single());
            Assert.IsFalse(other.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.IsFalse(other.Headers.GetValues("Content-Security-Policy").Single().Contains("wasm-unsafe-eval"));
        }
    }

    private async Task<FlashLink> LinkAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsJsonAsync("/api/files/flash-links", new FlashLinkRequest(path), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FlashLink>(Json, Token))!;
    }

    private async Task<HttpStatusCode> StatusAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Token);
        return response.StatusCode;
    }

    private async Task<JsonElement> ConfigAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/config", Token);
}
