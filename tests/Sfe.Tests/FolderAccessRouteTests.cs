using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Folder rules at the routes, through HTTP. Alice is in Users, for whom SpecialDir is hidden, RepeatedDir hidden but
/// for RepeatedDir/ChildDir (so RepeatedDir only leads there), and Media and Edited read-only.
/// </summary>
[TestClass]
public sealed class FolderAccessRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private FolderRuleService _rules = null!;
    private int _tenant;
    private int _users;
    private readonly List<string> _links = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library, settings => settings["Sfe:Security:AnonymousGuests"] = "true");
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        _users = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Token))!.Id;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [_users]), null, Token);
        _rules = _factory.Services.GetRequiredService<FolderRuleService>();
        await RuleAsync("SpecialDir", FolderLevel.Hidden);
        await RuleAsync("RepeatedDir", FolderLevel.Hidden);
        await RuleAsync("RepeatedDir/ChildDir", FolderLevel.Read);
        await RuleAsync("Media", FolderLevel.Read);
        await RuleAsync("Edited", FolderLevel.Read);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // A junction first: deleting the library through it would delete what it leads to.
        foreach (var link in _links)
            Directory.Delete(link);
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ListingsLeaveOutWhatTheSubjectDoesNotSee()
    {
        var everything = await GetAsync<DirectoryListing>(_admin, "/api/browse");
        var root = await GetAsync<DirectoryListing>(_alice, "/api/browse");
        var through = await GetAsync<DirectoryListing>(_alice, "/api/browse?path=RepeatedDir");
        var inside = await GetAsync<DirectoryListing>(_alice, "/api/browse?path=RepeatedDir/ChildDir");
        using var hidden = await _alice.GetAsync("/api/browse?path=SpecialDir", Token);
        using var below = await _alice.GetAsync($"/api/browse?path={Uri.EscapeDataString("SpecialDir/" + TestLibrary.SpecialFolder)}", Token);

        CollectionAssert.Contains(everything.Items.Select(i => i.Name).ToList(), "SpecialDir", "Server administrators see everything");
        CollectionAssert.DoesNotContain(root.Items.Select(i => i.Name).ToList(), "SpecialDir");
        CollectionAssert.Contains(root.Items.Select(i => i.Name).ToList(), "RepeatedDir", "A folder leading to a visible one is listed");
        Assert.AreEqual(everything.Total - 1, root.Total);
        Assert.AreEqual(everything.DirectoryCount - 1, root.DirectoryCount);
        CollectionAssert.AreEqual(new[] { "ChildDir" }, through.Items.Select(i => i.Name).ToArray(), "Only the way to what Alice sees");
        Assert.AreEqual(1, through.Total);
        Assert.AreEqual(2, inside.Total);
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, below.StatusCode);
    }

    [TestMethod]
    public async Task PagesOfAFilteredListingFollowEachOther()
    {
        var all = await GetAsync<DirectoryListing>(_alice, "/api/browse");
        var pages = new List<string>();
        for (var offset = 0; offset < all.Total; offset += 2)
            pages.AddRange((await GetAsync<DirectoryListing>(_alice, $"/api/browse?offset={offset}&limit=2")).Items.Select(i => i.Name));

        CollectionAssert.AreEqual(all.Items.Select(i => i.Name).ToList(), pages);
    }

    [TestMethod]
    public async Task FilesOfHiddenFoldersAnswerAsMissing()
    {
        var spelled = LibraryPaths.IsCaseInsensitiveFileSystem ? "specialdir/SDEspañol" : "SpecialDir/SDEspañol";

        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/files/raw/SpecialDir/SDEspañol"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/files/raw/" + spelled), "Whatever the case on Windows");
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/files/raw/RepeatedDir/RDFile1.txt"), "A folder that only leads elsewhere shows no file");
        Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(_alice, "/api/files/raw/RepeatedDir/ChildDir/AFile1.txt"));
        Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(_admin, "/api/files/raw/SpecialDir/SDEspañol"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/thumbnails/SpecialDir/SDEspañol"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/file-tags?path=SpecialDir/SDEspañol"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/search?path=SpecialDir"));
        if (OperatingSystem.IsWindows())
            Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/files/raw/SpecialDir./SDEspañol"), "Windows opens \"SpecialDir.\" as SpecialDir");
    }

    [TestMethod]
    public async Task ALinkIntoAHiddenFolderLeadsNowhere()
    {
        if (!TryLinkFolder("ChildDir/Into", "SpecialDir"))
            Assert.Inconclusive("Folder links can't be made here.");

        await ScanAsync();
        var adminSearch = await GetAsync<SearchResults>(_admin, "/api/search?q=SDEspañol");
        var aliceSearch = await GetAsync<SearchResults>(_alice, "/api/search?q=SDEspañol");

        Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(_admin, "/api/files/raw/ChildDir/Into/SDEspañol"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/files/raw/ChildDir/Into/SDEspañol"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/browse?path=ChildDir/Into"));
        CollectionAssert.Contains(adminSearch.Items.Select(i => i.Path).ToList(), "ChildDir/Into/SDEspañol", "Scans follow the link");
        Assert.IsEmpty(aliceSearch.Items, "The file through the link is where Alice doesn't see");
    }

    [TestMethod]
    public async Task SiblingsSkipHiddenFolders()
    {
        var everything = await GetAsync<FolderSiblings>(_admin, "/api/browse/siblings?path=Media");
        var alice = await GetAsync<FolderSiblings>(_alice, "/api/browse/siblings?path=Media");

        Assert.AreEqual(everything.Count - 1, alice.Count);
        Assert.AreNotEqual("SpecialDir", alice.Next?.Name);
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/browse/siblings?path=SpecialDir"));
    }

    [TestMethod]
    public async Task SearchesAndTagCountsOnlyCountWhatTheSubjectSees()
    {
        await ScanAsync();
        var sea = await PostAsync<TagDetails>(_admin, "/api/tags", new TagInput("Sea"));
        using (var tagged = await _admin.PostAsJsonAsync("/api/file-tags", new FileTagChange(["SpecialDir/SDEspañol", "Media/photo.jpg"], Add: [sea.Id]), Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, tagged.StatusCode);

        var adminFiles = await GetAsync<SearchResults>(_admin, "/api/search?q=RDFile");
        var aliceFiles = await GetAsync<SearchResults>(_alice, "/api/search?q=RDFile");
        var aliceOthers = await GetAsync<SearchResults>(_alice, "/api/search?q=AFile1");
        var tagForAdmin = await GetAsync<TagDetails>(_admin, $"/api/tags/{sea.Id}");
        var tagForAlice = await GetAsync<TagDetails>(_alice, $"/api/tags/{sea.Id}");
        var tagSearch = await GetAsync<TagSearchResults>(_alice, "/api/tags?q=Sea");

        Assert.AreEqual(4, adminFiles.Total);
        Assert.AreEqual(0, aliceFiles.Total, "Every RDFile is in a hidden folder for Alice");
        CollectionAssert.AreEquivalent(new[] { "ChildDir/AFile1.txt", "RepeatedDir/ChildDir/AFile1.txt" }, aliceOthers.Items.Select(i => i.Path).ToList());
        Assert.AreEqual(2, tagForAdmin.FileCount);
        Assert.AreEqual(1, tagForAlice.FileCount);
        Assert.AreEqual(1, tagSearch.Matches.Single().FileCount);
    }

    [TestMethod]
    public async Task TaggingNeedsToSeeEveryFile()
    {
        await ScanAsync();
        var sea = await PostAsync<TagDetails>(_admin, "/api/tags", new TagInput("Sea"));

        using var readOnly = await _alice.PostAsJsonAsync("/api/file-tags", new FileTagChange(["Media/photo.jpg"], Add: [sea.Id]), Json, Token);
        using var mixed = await _alice.PostAsJsonAsync("/api/file-tags", new FileTagChange(["Media/photo.jpg", "SpecialDir/SDEspañol"], Add: [sea.Id]), Json, Token);
        using var scope = await _alice.PostAsJsonAsync("/api/file-tags/scope", new ScopeTagChange("", true, null, Add: [sea.Id]), Json, Token);
        var tag = await GetAsync<TagDetails>(_admin, $"/api/tags/{sea.Id}");

        Assert.AreEqual(HttpStatusCode.OK, readOnly.StatusCode, "Tagging changes the index, not the files: a read-only folder may be tagged");
        Assert.AreEqual(HttpStatusCode.BadRequest, mixed.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, scope.StatusCode, await scope.Content.ReadAsStringAsync(Token));
        var everything = await GetAsync<SearchResults>(_admin, "/api/search?q=*");
        var hiddenCount = everything.Items.Count(i => i.Path.StartsWith("SpecialDir/", StringComparison.Ordinal)
            || (i.Path.StartsWith("RepeatedDir/", StringComparison.Ordinal) && !i.Path.StartsWith("RepeatedDir/ChildDir/", StringComparison.Ordinal)));
        Assert.AreEqual(everything.Total - hiddenCount, tag.FileCount, "Tagging the whole library reaches only the files Alice sees");
    }

    [TestMethod]
    public async Task ReadOnlyFoldersRefuseChangesToTheirFiles()
    {
        // Cleaners may delete duplicates, but Media is read-only and SpecialDir hidden for them too.
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var alice = (await accounts.GetUsersAsync(_tenant, Token)).Single(u => u.UserName == "alice").Id;
        var cleaners = (await accounts.CreateGroupAsync(_tenant,
            new GroupInput("Cleaners", Rights: [Rights.Browse, Rights.ViewFiles, Rights.RemoveDuplicates], MemberIds: [alice]), cancellationToken: Token)).Value!.Id;
        await _rules.SetAsync(_tenant, "Media", FolderPrincipalRef.Group(cleaners), FolderLevel.Read, null, Token);
        await _rules.SetAsync(_tenant, "SpecialDir", FolderPrincipalRef.Group(cleaners), FolderLevel.Hidden, null, Token);

        using var editor = await _alice.PostAsync("/api/editor/images?path=Media/photo.jpg", new ByteArrayContent([1, 2, 3]), Token);
        using var hiddenEditor = await _alice.PostAsync("/api/editor/images?path=SpecialDir/SDEspañol", new ByteArrayContent([1, 2, 3]), Token);
        using var readOnly = await _alice.PostAsJsonAsync("/api/duplicates/remove", new RemoveDuplicateRequest("Media/photo.jpg", "ChildDir/AFile1.txt"), Json, Token);
        using var keepHidden = await _alice.PostAsJsonAsync("/api/duplicates/remove", new RemoveDuplicateRequest("ChildDir/AFile1.txt", "SpecialDir/SDEspañol"), Json, Token);
        using var removeHidden = await _alice.PostAsJsonAsync("/api/duplicates/remove", new RemoveDuplicateRequest("SpecialDir/SDEspañol", "ChildDir/AFile1.txt"), Json, Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, editor.StatusCode, "Edited pictures go to Edited/Media: read-only");
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenEditor.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, readOnly.StatusCode, "Seen but read-only");
        Assert.AreEqual(HttpStatusCode.NotFound, keepHidden.StatusCode, "The copy kept must be one Alice sees");
        Assert.AreEqual(HttpStatusCode.NotFound, removeHidden.StatusCode);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));
        Assert.IsTrue(File.Exists(_library.FullPath("SpecialDir/SDEspañol")));
    }

    [TestMethod]
    public async Task ARuleMadeAfterALinkEndsTheLink()
    {
        var link = await PostAsync<FileLink>(_alice, "/api/files/links", new FileLinkRequest("ChildDir/AFile1.txt"));
        var url = "/" + link.Url.TrimStart('/');
        var anonymous = ApiClients.Create(_factory);

        var before = await StatusAsync(anonymous, url);
        await RuleAsync("ChildDir", FolderLevel.Hidden);
        var after = await StatusAsync(anonymous, url);

        Assert.AreEqual(HttpStatusCode.OK, before);
        Assert.AreEqual(HttpStatusCode.Forbidden, after);
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/files/raw/ChildDir/AFile1.txt"));
    }

    [TestMethod]
    public async Task QuickLinksOnlyLeadWhereTheSubjectSees()
    {
        using var hidden = await _alice.PostAsJsonAsync("/api/me/quick-links", new QuickLinkAdd(["SpecialDir"], null), Json, Token);
        var added = await PostAsync<List<QuickLinkView>>(_alice, "/api/me/quick-links", new QuickLinkAdd(["ChildDir"], null));
        await RuleAsync("ChildDir", FolderLevel.Hidden);
        var after = await GetAsync<List<QuickLinkView>>(_alice, "/api/me/quick-links");

        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.IsTrue(added.Single().Exists);
        Assert.IsFalse(after.Single().Exists, "A link to a folder hidden since shows as missing");
    }

    [TestMethod]
    public async Task VisitorsSeeOnlyTheFoldersOfTheGuestsGroup()
    {
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        await accounts.SetGuestAccessAsync(_tenant, true, Token);
        var guests = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Guests, Token))!.Id;
        await _rules.SetAsync(_tenant, "", FolderPrincipalRef.Group(guests), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Media", FolderPrincipalRef.Group(guests), FolderLevel.Read, null, Token);
        var visitor = ApiClients.Create(_factory);

        var root = await GetAsync<DirectoryListing>(visitor, "/api/browse");

        CollectionAssert.AreEqual(new[] { "Media" }, root.Items.Select(i => i.Name).ToArray(), "The root only leads to Media");
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(visitor, "/api/files/raw/RFile1.txt"));
        Assert.AreEqual(HttpStatusCode.OK, await StatusAsync(visitor, "/api/files/raw/Media/photo.jpg"));
    }

    [TestMethod]
    public async Task ListingsTellWhatTheSubjectMayDoThere()
    {
        var root = await GetAsync<DirectoryListing>(_alice, "/api/browse");
        var media = await GetAsync<DirectoryListing>(_alice, "/api/browse?path=Media");
        var admin = await GetAsync<DirectoryListing>(_admin, "/api/browse");

        Assert.IsNull(admin.Access, "No rule applies to server administrators");
        CollectionAssert.Contains(root.Access!.Rights.ToList(), Rights.EditImages);
        Assert.IsTrue(root.Access.Children!["RepeatedDir"].PathOnly);
        CollectionAssert.DoesNotContain(root.Access.Children["Media"].Rights.ToList(), Rights.EditImages, "Media is read-only");
        CollectionAssert.Contains(root.Access.Children["Media"].Rights.ToList(), Rights.AssignTags);
        Assert.IsFalse(root.Access.Children.ContainsKey("SpecialDir"), "Nothing about a hidden folder");
        CollectionAssert.DoesNotContain(media.Access!.Rights.ToList(), Rights.EditImages);
    }

    [TestMethod]
    public async Task ManagersSetRulesAndSeeWhoMayDoWhat()
    {
        using var refused = await _alice.PutAsJsonAsync("/api/access/folder-rules", new FolderRuleChange("Media", _users, null, FolderLevel.Write), Json, Token);
        using var set = await _admin.PutAsJsonAsync("/api/access/folder-rules", new FolderRuleChange("ChildDir", _users, null, FolderLevel.Hidden), Json, Token);
        var rules = (await set.Content.ReadFromJsonAsync<List<FolderRuleView>>(Json, Token))!;
        var report = await GetAsync<FolderAccessReport>(_admin, "/api/access/folder-access?path=ChildDir");
        var picker = await GetAsync<FolderPicker>(_admin, "/api/access/folders");

        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, "Only those who manage the space's groups");
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode);
        var rule = rules.Single(r => r.Path == "ChildDir");
        Assert.AreEqual("Users", rule.Name);
        Assert.IsTrue(rule.Exists);
        var users = report.Groups.Single(g => g.GroupId == _users);
        Assert.AreEqual(FolderLevel.Hidden, users.Level);
        Assert.IsTrue(users.Explicit);
        Assert.IsFalse(users.Sees);
        Assert.IsFalse(report.Accounts.Single(a => a.Name == "alice").Sees);
        Assert.IsTrue(report.Accounts.Single(a => a.Name != "alice").Sees, "The server administrator");
        CollectionAssert.Contains(picker.Folders.Select(f => f.Name).ToList(), "SpecialDir", "Managers pick among every folder");
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/browse?path=ChildDir"));
    }

    [TestMethod]
    public async Task RulesOfAFolderGoneOutsideSfeCanBeRepointed()
    {
        await RuleAsync("Gone/Away", FolderLevel.Hidden);

        var listed = await GetAsync<List<FolderRuleView>>(_admin, "/api/access/folder-rules");
        using var stillThere = await _admin.PostAsJsonAsync("/api/access/folder-rules/repoint", new FolderRuleRepoint("Media", "ChildDir"), Json, Token);
        var moved = await PostAsync<List<FolderRuleView>>(_admin, "/api/access/folder-rules/repoint", new FolderRuleRepoint("Gone/Away", "ChildDir"));

        Assert.IsFalse(listed.Single(r => r.Path == "Gone/Away").Exists);
        Assert.AreEqual(HttpStatusCode.Conflict, stillThere.StatusCode, "A folder that is there moves with its rules, not without");
        Assert.IsFalse(moved.Any(r => r.Path == "Gone/Away"));
        Assert.IsTrue(moved.Single(r => r.Path == "ChildDir").Exists);
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_alice, "/api/browse?path=ChildDir"));
    }

    private async Task RuleAsync(string path, FolderLevel level) =>
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, path, FolderPrincipalRef.Group(_users), level, null, Token)).Status);

    private async Task ScanAsync()
    {
        using (var started = await _admin.PostAsJsonAsync("/api/scan", new ScanRequest(""), Json, Token))
            Assert.AreEqual(HttpStatusCode.Accepted, started.StatusCode);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if ((await GetAsync<ScanStatus>(_admin, "/api/scan")).State != ScanState.Running)
                return;
            await Task.Delay(50, Token);
        }
        throw new AssertFailedException("Scan did not finish in time.");
    }

    private bool TryLinkFolder(string link, string target)
    {
        var linkPath = _library.FullPath(link);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{_library.FullPath(target)}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                })!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    return false;
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, _library.FullPath(target));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        _links.Add(linkPath);
        return true;
    }

    private async Task<HttpStatusCode> StatusAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Token);
        return response.StatusCode;
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }

    private async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, Json, Token);
        Assert.IsTrue(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }
}
