using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Archives explored through HTTP (<c>api/files/explore</c>). Alice is in Users, with SpecialDir hidden from them; Bob
/// may browse but not open files.
/// </summary>
[TestClass]
public sealed class ExploreRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public Task InitializeAsync()
    {
        _library = new TestLibrary();
        return StartAsync();
    }

    private async Task StartAsync(Action<IDictionary<string, string?>>? configure = null)
    {
        _factory = ApiClients.CreateFactory(_library, configure);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        var browsers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Browsers", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-1", GroupIds: [browsers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Stop();
        _library.Dispose();
    }

    private void Stop()
    {
        _bob?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task AnArchiveIsReadOnceThenListedFolderByFolderInPages()
    {
        TestArchives.Zip(_library.Root, "Media/trip.zip",
            ("readme.txt", "hello"), ("Day 1/a.txt", "a"), ("Day 1/b.txt", "bb"), ("Day 1/c.txt", "ccc"), ("Day 2/Deep/x.txt", "x"));

        var root = await ListAsync(_alice, "path=Media/trip.zip");
        Assert.AreEqual(("zip", true, 5L, 0L), (root.Format, root.Complete, root.Entries, root.LeftOut));
        CollectionAssert.AreEqual(new[] { "Day 1", "Day 2", "readme.txt" }, root.Items.Select(item => item.Name).ToList());
        Assert.AreEqual(("folder", 3L, 3L), (root.Items[0].Kind, root.Items[0].Files!.Value, root.Items[0].AllFiles!.Value));
        Assert.AreEqual((5L, 1L + 1 + 2 + 3 + 5), (root.Folder.AllFiles!.Value, root.Folder.Size));
        Assert.IsNull(root.Next);

        var names = new List<string>();
        string? after = null;
        do
        {
            var page = await ListAsync(_alice, $"path=Media/trip.zip&folder={root.Items[0].Id}&limit=2" + (after is null ? "" : $"&after={after}"));
            names.AddRange(page.Items.Select(item => item.Name));
            Assert.AreEqual("Day 1", page.Trail.Last.Single().Name);
            after = page.Next;
        }
        while (after is not null);
        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.txt" }, names);

        var deep = await ListAsync(_alice, $"path=Media/trip.zip&at={Uri.EscapeDataString("Day 2/Deep")}");
        Assert.AreEqual("x.txt", deep.Items.Single().Name);
        CollectionAssert.AreEqual(new[] { "Day 2", "Deep" }, deep.Trail.Last.Select(crumb => crumb.Name).ToList());
        using (var none = await _alice.GetAsync("/api/files/explore?path=Media/trip.zip&at=Nowhere", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, none.StatusCode);
        using (var badCursor = await _alice.GetAsync("/api/files/explore?path=Media/trip.zip&after=nonsense", Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, badCursor.StatusCode);
    }

    [TestMethod]
    public async Task EntriesOpenSandboxedInlineOnlyWhenSafe()
    {
        var png = await File.ReadAllBytesAsync(TestSamples.PathOf("testsrc-64x48-still.png"), Token);
        var zip = Path.Combine(_library.Root, "Media", "files.zip");
        using (var file = File.Create(zip))
        using (var archive = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var entry = archive.CreateEntry("pictures/still.png").Open())
                entry.Write(png);
            using (var page = new StreamWriter(archive.CreateEntry("page.html").Open()))
                page.Write("<script>alert(1)</script>");
        }
        var listing = await ListAsync(_alice, "path=Media/files.zip");
        var html = listing.Items.Single(item => item.Name == "page.html");
        var pictures = await ListAsync(_alice, $"path=Media/files.zip&folder={listing.Items.Single(item => item.Name == "pictures").Id}");
        var still = pictures.Items.Single();
        var token = EpubEntries.Token("Media/files.zip");

        using (var picture = await _alice.GetAsync($"/api/files/explore/entry/{token}/{still.Id}/still.png", Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, picture.StatusCode, await picture.Content.ReadAsStringAsync(Token));
            Assert.AreEqual("image/png", picture.Content.Headers.ContentType?.MediaType);
            Assert.IsNull(picture.Content.Headers.ContentDisposition, "a picture shows inline");
            CollectionAssert.AreEqual(png, await picture.Content.ReadAsByteArrayAsync(Token));
            StringAssert.Contains(picture.Headers.GetValues("Content-Security-Policy").Single(), "sandbox");
            using var again = new HttpRequestMessage(HttpMethod.Get, $"/api/files/explore/entry/{token}/{still.Id}/still.png");
            again.Headers.IfNoneMatch.Add(picture.Headers.ETag!);
            using var notModified = await _alice.SendAsync(again, Token);
            Assert.AreEqual(HttpStatusCode.NotModified, notModified.StatusCode);
        }
        using (var page = await _alice.GetAsync($"/api/files/explore/entry/{token}/{html.Id}/page.html", Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
            Assert.AreEqual("attachment", page.Content.Headers.ContentDisposition?.DispositionType, "a page of a stranger never opens on the app's origin");
        }
        using (var saved = await _alice.GetAsync($"/api/files/explore/entry/{token}/{still.Id}/still.png?download=true", Token))
            Assert.AreEqual("attachment", saved.Content.Headers.ContentDisposition?.DispositionType);
        foreach (var wrong in new[] { $"{token}/{still.Id}/other.png", $"{token}/{listing.Items[0].Id + 1000}/still.png", $"not-a-token!/{still.Id}/still.png",
            $"{EpubEntries.Token("Media/none.zip")}/{still.Id}/still.png" })
        {
            using var response = await _alice.GetAsync($"/api/files/explore/entry/{wrong}", Token);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, wrong);
        }
        using (var bob = await _bob.GetAsync($"/api/files/explore/entry/{token}/{still.Id}/still.png", Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, bob.StatusCode, "Bob may not open files");
    }

    [TestMethod]
    public async Task TheRightsAndTheFolderRulesApply()
    {
        TestArchives.Zip(_library.Root, "SpecialDir/hidden.zip", ("a.txt", "a"));
        TestArchives.Zip(_library.Root, "Media/seen.zip", ("a.txt", "a"));
        TestArchives.Zip(_library.Root, ".sfe/trash/work.zip", ("a.txt", "a"));

        using (var bob = await _bob.GetAsync("/api/files/explore?path=Media/seen.zip", Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, bob.StatusCode);
        foreach (var path in new[] { "SpecialDir/hidden.zip", ".sfe/trash/work.zip", "Media/none.zip", "../Root/Media/seen.zip", "Media" })
        {
            using var response = await _alice.GetAsync($"/api/files/explore?path={Uri.EscapeDataString(path)}", Token);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
        }
        Assert.AreEqual(1L, (await ListAsync(_admin, "path=SpecialDir/hidden.zip")).Entries, "hidden from Alice only");
    }

    [TestMethod]
    public async Task WhatCanNotBeExploredAnswersSo()
    {
        await File.WriteAllTextAsync(_library.FullPath("Media/fake.zip"), "not an archive", Token);
        TestArchives.Tar(Path.Combine(_library.Root, "Media"), "one.gz", gzip: true, ("x.txt", "x"));

        foreach (var path in new[] { "Media/fake.zip", "Media/one.gz", "RFile1.txt" })
        {
            using var response = await _alice.GetAsync($"/api/files/explore?path={path}", Token);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, path);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(Token), ExploreEndpoints.NotExplorableType);
        }
    }

    [TestMethod]
    public async Task HostileEntriesAreLeftOutOrRefused()
    {
        var zip = TestArchives.Zip(_library.Root, "Media/hostile.zip", ("../../evil.txt", "evil"), ("zeros.bin", new string('\0', 64 * 1024)));
        // The directory claims 1 GB for 64 KB of zeros (a few hundred bytes deflated): past the ratio allowed.
        TestArchives.PatchDirectory(zip, 24, BitConverter.GetBytes(1_000_000_000u), record: 1);

        var listing = await ListAsync(_alice, "path=Media/hostile.zip");
        Assert.AreEqual(1L, listing.LeftOut, "the name leaving the archive");
        var zeros = listing.Items.Single();
        Assert.AreEqual(1_000_000_000L, zeros.Size);
        using var refused = await _alice.GetAsync($"/api/files/explore/entry/{EpubEntries.Token("Media/hostile.zip")}/{zeros.Id}/zeros.bin?download=true", Token);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [TestMethod]
    public async Task LargeEntriesAreOnlyDownloaded()
    {
        Stop();
        _library.Dispose();
        _library = new TestLibrary();
        await StartAsync(settings => settings["Sfe:Files:Archives:MaxPreviewMegabytes"] = "1");
        TestArchives.Zip(_library.Root, "Media/big.zip", ("big.txt", new string('b', 2 * 1024 * 1024)));
        var big = (await ListAsync(_alice, "path=Media/big.zip")).Items.Single();
        var url = $"/api/files/explore/entry/{EpubEntries.Token("Media/big.zip")}/{big.Id}/big.txt";

        using (var preview = await _alice.GetAsync(url, Token))
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, preview.StatusCode);
        using var download = await _alice.GetAsync(url + "?download=true", Token);
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual(2 * 1024 * 1024, (await download.Content.ReadAsByteArrayAsync(Token)).Length);
    }

    /// <summary>A folder's page, once the archive's list is read (202 until then).</summary>
    private async Task<ArchiveListing> ListAsync(HttpClient client, string query)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            using var response = await client.GetAsync($"/api/files/explore?{query}", Token);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(25, Token);
                continue;
            }
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
            return (await response.Content.ReadFromJsonAsync<ArchiveListing>(Json, Token))!;
        }
        Assert.Fail("The archive's list was never ready.");
        return null!;
    }
}
