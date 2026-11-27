using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>api/help</c>: the user documentation built into the app, its index of titles and headings, who may read it
/// (agents/docs/features/help.md).
/// </summary>
[TestClass]
public sealed class HelpRouteTests
{
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program>? _factory;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task EveryPageOfTheDocsIsListedWithItsTitleAndHeadings()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        var index = (await admin.GetFromJsonAsync<HelpIndex>($"{V1}/help", Token))!;

        var docs = Path.Combine(RepositoryRoot(), "docs");
        var pages = Directory
            .GetFiles(docs, "*.md", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(docs, file).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        CollectionAssert.AreEquivalent(pages, index.Pages.Select(page => page.Path).ToList());
        Assert.AreEqual("README.md", index.Pages[0].Path, "The help's first page");
        var browsing = index.Pages.Single(page => page.Path == "user-guide/browsing.md");
        Assert.AreEqual("Browsing", browsing.Title);
        Assert.AreEqual(new HelpHeading(2, "Layouts"), browsing.Headings[0]);
    }

    [TestMethod]
    public async Task APageAndAPictureAreServedSandboxedAndKept()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        using var page = await admin.GetAsync($"{V1}/help/files/user-guide/browsing.md", Token);
        using var picture = await admin.GetAsync($"{V1}/help/files/images/library-grid.png", Token);
        using var again = new HttpRequestMessage(HttpMethod.Get, $"{V1}/help/files/images/library-grid.png");
        again.Headers.IfNoneMatch.Add(picture.Headers.ETag!);
        using var kept = await admin.SendAsync(again, Token);

        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.AreEqual("text/markdown", page.Content.Headers.ContentType?.MediaType);
        StringAssert.StartsWith(await page.Content.ReadAsStringAsync(Token), "# Browsing");
        Assert.AreEqual(
            SecurityHeaders.FilePolicy,
            page.Headers.GetValues("Content-Security-Policy").Single(),
            "Sandboxed as the library's files"
        );
        Assert.AreEqual("image/png", picture.Content.Headers.ContentType?.MediaType);
        CollectionAssert.AreEqual(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs", "images", "library-grid.png")),
            await picture.Content.ReadAsByteArrayAsync(Token)
        );
        Assert.AreEqual(HttpStatusCode.NotModified, kept.StatusCode, "Asked again with its ETag");
    }

    [TestMethod]
    [DataRow("user-guide/nothing.md")]
    [DataRow("../CHANGELOG.md")]
    [DataRow("user-guide/../../AGENTS.md")]
    [DataRow("images")]
    public async Task WhatTheHelpHasNotIsNotFound(string path)
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        using var answer = await admin.GetAsync($"{V1}/help/files/{path}", Token);

        Assert.AreEqual(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [TestMethod]
    public async Task TheHelpAnswersBeforeTheSetup()
    {
        var stranger = ApiClients.Create(Factory());

        using var index = await stranger.GetAsync($"{V1}/help", Token);
        using var page = await stranger.GetAsync($"{V1}/help/files/first-start.md", Token);
        using var config = await stranger.GetAsync($"{V1}/config", Token);

        Assert.AreEqual(HttpStatusCode.OK, index.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, config.StatusCode, "The rest waits for the setup");
    }

    [TestMethod]
    public async Task OnceSetUpVisitorsReadItOnlyInASpaceOpenToGuests()
    {
        var factory = Factory(settings => settings["Sfe:Security:AnonymousGuests"] = "true");
        await ApiClients.SetUpAsync(factory, Token);
        var visitor = ApiClients.Create(factory);

        using var closed = await visitor.GetAsync($"{V1}/help", Token);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single();
        await accounts.SetGuestAccessAsync(tenant.Id, true, Token);
        using var open = await visitor.GetAsync($"{V1}/help", Token);

        Assert.AreEqual(HttpStatusCode.Unauthorized, closed.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, open.StatusCode);
    }

    [TestMethod]
    public async Task AnAccountWithoutRightsReadsIt()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, Token);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single().Id;
        var none = (await accounts.CreateGroupAsync(tenant, new GroupInput("Nothing"), cancellationToken: Token))
            .Value!
            .Id;
        await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-1", GroupIds: [none]), null, Token);
        var bob = await ApiClients.SignInAsync(factory, "bob", "bob-password-1", Token);

        using var index = await bob.GetAsync($"{V1}/help", Token);

        Assert.AreEqual(HttpStatusCode.OK, index.StatusCode);
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? settings = null) =>
        _factory = ApiClients.CreateFactory(_library, settings);

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Sfe.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("No repository above the tests.");
    }
}
