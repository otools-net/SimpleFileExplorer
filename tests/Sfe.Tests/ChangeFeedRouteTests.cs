using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The change feed and new files through HTTP. Alice is in Users (who may browse, not create files) with SpecialDir
/// hidden from them; the administrator changes folders.
/// </summary>
[TestClass]
public sealed class ChangeFeedRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderChangedElsewhereIsToldToThoseWhoSeeIt()
    {
        using var feed = await _alice.GetAsync("/api/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);
        Assert.AreEqual(HttpStatusCode.OK, feed.StatusCode);
        Assert.AreEqual("text/event-stream", feed.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        Assert.AreEqual("retry: 5000", await reader.ReadLineAsync(Token));

        // Hidden from Alice: not told. Then a folder she sees, from a tab of the administrator's.
        await CreateFolderAsync("SpecialDir", "hidden news", null);
        await CreateFolderAsync("ChildDir", "news", "tab-1");

        var change = await NextChangeAsync(reader);
        Assert.AreEqual("ChildDir", change.Path);
        Assert.AreEqual("tab-1", change.Origin);
    }

    [TestMethod]
    public async Task NewFilesNeedTheirRight()
    {
        using var refused = await _alice.PostAsJsonAsync("/api/files/new-file", new CreateFileRequest("ChildDir", "notes.txt"), Json, Token);
        using var created = await _admin.PostAsJsonAsync("/api/files/new-file", new CreateFileRequest("ChildDir", "notes.txt"), Json, Token);
        using var again = await _admin.PostAsJsonAsync("/api/files/new-file", new CreateFileRequest("ChildDir", "notes.txt"), Json, Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode, await created.Content.ReadAsStringAsync(Token));
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/notes.txt")));
    }

    private async Task CreateFolderAsync(string path, string name, string? tab)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/folders") { Content = JsonContent.Create(new CreateFolderRequest(path, name), options: Json) };
        if (tab is not null)
            request.Headers.Add(ChangeEndpoints.TabHeader, tab);
        using var response = await _admin.SendAsync(request, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    private async Task<FolderChange> NextChangeAsync(StreamReader reader)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<FolderChange>(line["data: ".Length..], Json)!;
        }
        throw new AssertFailedException("The feed ended without a change.");
    }
}
