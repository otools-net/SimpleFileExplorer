using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The file management routes (new folder, rename) through HTTP: rights, folder rules, names, conflicts, what follows a
/// renamed folder, and places no route may write to. Alice is in Users, which may create folders and rename, with
/// SpecialDir hidden, Media read-only and RepeatedDir/RepeatedDir hidden; Bob only browses.
/// </summary>
[TestClass]
public sealed class FileRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private FolderRuleService _rules = null!;
    private int _tenant;
    private int _users;
    private int _aliceId;
    private readonly List<string> _links = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library, settings => settings["Sfe:ShowHiddenItems"] = "true");
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var users = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Token))!;
        _users = users.Id;
        Assert.AreEqual(AccessStatus.Ok, (await accounts.UpdateGroupAsync(_tenant, _users,
            new GroupInput(users.Name, Rights: [.. users.Rights, Rights.CreateFolders, Rights.RenameFiles], MemberIds: users.MemberIds), cancellationToken: Token)).Status);
        var viewers = (await accounts.CreateGroupAsync(_tenant, new GroupInput("Viewers", Rights: [Rights.Browse, Rights.ViewFiles]), cancellationToken: Token)).Value!;
        _aliceId = (await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [_users]), null, Token)).Value!.Id;
        await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-1", GroupIds: [viewers.Id]), null, Token);
        _rules = _factory.Services.GetRequiredService<FolderRuleService>();
        await RuleAsync("SpecialDir", FolderLevel.Hidden);
        await RuleAsync("Media", FolderLevel.Read);
        await RuleAsync("RepeatedDir/RepeatedDir", FolderLevel.Hidden);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var link in _links)
            Directory.Delete(link);
        _bob?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task FoldersAreCreatedAndRenamedAsListingsShowThem()
    {
        var created = await OkAsync(_admin, "/api/files/folders", new CreateFolderRequest("ChildDir", "Holidays"));
        var renamed = await OkAsync(_admin, "/api/files/rename", new RenameRequest("ChildDir/Holidays", "Holidays 2024"));
        var file = await OkAsync(_admin, "/api/files/rename", new RenameRequest("ChildDir/AFile1.txt", "notes.txt"));

        Assert.AreEqual("ChildDir/Holidays", created.Path);
        Assert.AreEqual(ItemKind.Directory, created.Kind);
        Assert.AreEqual("ChildDir/Holidays 2024", renamed.Path);
        Assert.IsTrue(Directory.Exists(_library.FullPath("ChildDir/Holidays 2024")));
        Assert.AreEqual("notes.txt", file.Name);
        Assert.AreEqual("txt", file.Extension);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/notes.txt")));
        var listing = await GetAsync<DirectoryListing>(_admin, "/api/browse?path=ChildDir");
        CollectionAssert.IsSubsetOf(new[] { "Holidays 2024", "notes.txt" }, listing.Items.Select(i => i.Name).ToList(), "The listing kept before is not served again");
    }

    [TestMethod]
    public async Task RightsAndFolderRulesDecideWhereSomeoneMayChangeFiles()
    {
        Assert.AreEqual(HttpStatusCode.OK, await PostStatusAsync(_alice, "/api/files/folders", new CreateFolderRequest("ChildDir", "Mine")));
        Assert.AreEqual(HttpStatusCode.OK, await PostStatusAsync(_alice, "/api/files/rename", new RenameRequest("ChildDir/AFile2", "AFile2.bin")));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostStatusAsync(_alice, "/api/files/folders", new CreateFolderRequest("Media", "x")), "Read-only folder");
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostStatusAsync(_alice, "/api/files/rename", new RenameRequest("Media/photo.jpg", "x.jpg")));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostStatusAsync(_alice, "/api/files/rename", new RenameRequest("Media", "Videos")),
            "Media itself is read-only");
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_alice, "/api/files/folders", new CreateFolderRequest("SpecialDir", "x")), "Hidden folder");
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_alice, "/api/files/rename", new RenameRequest("SpecialDir/SDEspañol", "x")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_alice, "/api/files/rename", new RenameRequest("SpecialDir", "Open")));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostStatusAsync(_alice, "/api/files/rename", new RenameRequest("RepeatedDir", "Other")),
            "A folder holding one Alice doesn't see is not hers to rename");
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostStatusAsync(_bob, "/api/files/folders", new CreateFolderRequest("ChildDir", "x")), "Without the right");
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostStatusAsync(_bob, "/api/files/rename", new RenameRequest("RFile1.txt", "x.txt")));
        using var visitor = ApiClients.Create(_factory);
        Assert.AreNotEqual(HttpStatusCode.OK, await PostStatusAsync(visitor, "/api/files/folders", new CreateFolderRequest("ChildDir", "x")));

        Assert.IsTrue(Directory.Exists(_library.FullPath("Media")));
        Assert.IsTrue(Directory.Exists(_library.FullPath("RepeatedDir")));
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
        Assert.IsFalse(Directory.Exists(_library.FullPath("ChildDir/x")));
    }

    [TestMethod]
    [DataRow("..")]
    [DataRow("a/b")]
    [DataRow("..\\RFile1.txt")]
    [DataRow("CON")]
    [DataRow("nul.txt")]
    [DataRow("a:b")]
    [DataRow("file::$DATA")]
    [DataRow("trailing.")]
    [DataRow("trailing ")]
    [DataRow("nul\0byte")]
    [DataRow("")]
    [DataRow(".sfe")]
    public async Task NamesThatCantBeUsedAre400s(string name)
    {
        using var create = await PostAsync(_admin, "/api/files/folders", new CreateFolderRequest("", name));
        using var rename = await PostAsync(_admin, "/api/files/rename", new RenameRequest("RFile1.txt", name));

        Assert.AreEqual(HttpStatusCode.BadRequest, create.StatusCode, await create.Content.ReadAsStringAsync(Token));
        Assert.AreEqual(HttpStatusCode.BadRequest, rename.StatusCode);
        var problem = (await create.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Token))!;
        Assert.IsTrue(problem.Errors.ContainsKey("name"));
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
    }

    [TestMethod]
    public async Task PathsOutsideTheLibraryAreNotFound()
    {
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/folders", new CreateFolderRequest("..", "x")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/folders", new CreateFolderRequest("../..", "x")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/rename", new RenameRequest("../index.db", "x.db")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/rename", new RenameRequest("", "Root2")), "The root itself");
        if (OperatingSystem.IsWindows())
            Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/folders", new CreateFolderRequest("C:", "x")));
        Assert.IsTrue(File.Exists(_library.DatabasePath));
    }

    [TestMethod]
    public async Task ConflictsSayWhatIsInTheWayAndNothingMore()
    {
        using var folder = await PostAsync(_admin, "/api/files/folders", new CreateFolderRequest("", "ChildDir"));
        using var hidden = await PostAsync(_alice, "/api/files/folders", new CreateFolderRequest("", "SpecialDir"));
        using var renamed = await PostAsync(_alice, "/api/files/rename", new RenameRequest("RFile1.txt", "SpecialDir"));

        foreach (var response in new[] { folder, hidden, renamed })
        {
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Token))!;
            Assert.AreEqual(FileEndpoints.ExistsType, problem.Type);
            Assert.IsFalse(problem.Detail!.Contains(_library.Root, StringComparison.OrdinalIgnoreCase));
        }
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.txt")));
    }

    [TestMethod]
    public async Task ARenamedFolderKeepsItsRulesAndQuickLinks()
    {
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, "ChildDir", FolderPrincipalRef.Account(_aliceId), FolderLevel.Write, null, Token)).Status);
        var guests = (await _factory.Services.GetRequiredService<AccessService>().GetBuiltInGroupAsync(_tenant, BuiltInGroup.Guests, Token))!.Id;
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, "ChildDir/Private", FolderPrincipalRef.Group(guests), FolderLevel.Hidden, null, Token)).Status);
        var quickLinks = _factory.Services.GetRequiredService<QuickLinkService>();
        await quickLinks.AddAsync(_aliceId, _tenant, [new QuickLinkEntry("ChildDir", true), new QuickLinkEntry("ChildDir/AFile1.txt", false), new QuickLinkEntry("ChildDirectory", true)], null, Token);

        await OkAsync(_alice, "/api/files/rename", new RenameRequest("ChildDir", "Kids"));

        var rules = (await _rules.GetAsync(_tenant, Token)).Select(r => r.Path).ToList();
        CollectionAssert.IsSubsetOf(new[] { "Kids", "Kids/Private" }, rules);
        Assert.IsFalse(rules.Any(r => r.StartsWith("ChildDir", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(new[] { "Kids", "Kids/AFile1.txt", "ChildDirectory" },
            (await quickLinks.GetAsync(_aliceId, _tenant, Token)).Select(l => l.Path).ToArray());
    }

    [TestMethod]
    public async Task RulesLeftWhereAFolderWouldGoKeepItWhereItIs()
    {
        await RuleAsync("Gone", FolderLevel.Read);

        using var response = await PostAsync(_admin, "/api/files/rename", new RenameRequest("RepeatedDir", "Gone"));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(FileEndpoints.FolderRulesType, (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Token))!.Type);
        Assert.IsTrue(Directory.Exists(_library.FullPath("RepeatedDir")));
        Assert.IsTrue((await _rules.GetAsync(_tenant, Token)).Any(r => r.Path == "RepeatedDir/RepeatedDir"), "The folder's own rules stay");
    }

    [TestMethod]
    public async Task NoRouteReachesTheWorkFolder()
    {
        Directory.CreateDirectory(_library.FullPath(".sfe/trash"));
        _library.AddFile(".sfe/trash/deleted.txt", 10);

        var root = await GetAsync<DirectoryListing>(_admin, "/api/browse");

        CollectionAssert.DoesNotContain(root.Items.Select(i => i.Name).ToList(), ".sfe", "Not even with hidden items shown");
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_admin, "/api/browse?path=.sfe"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_admin, "/api/browse?path=.sfe/trash"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_admin, "/api/files/raw/.sfe/trash/deleted.txt"));
        Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_admin, "/api/files/raw/ChildDir/../.sfe/trash/deleted.txt"));
        if (OperatingSystem.IsWindows())
            Assert.AreEqual(HttpStatusCode.NotFound, await StatusAsync(_admin, "/api/files/raw/.SFE./trash/deleted.txt"));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/folders", new CreateFolderRequest(".sfe", "x")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/rename", new RenameRequest(".sfe", "trash")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/rename", new RenameRequest(".sfe/trash/deleted.txt", "x.txt")));
        Assert.AreEqual(HttpStatusCode.BadRequest, await PostStatusAsync(_admin, "/api/files/rename", new RenameRequest("ChildDir", ".sfe")));
        Assert.IsTrue(File.Exists(_library.FullPath(".sfe/trash/deleted.txt")));
    }

    [TestMethod]
    public async Task ALinkToTheServersFilesIsNoWayToWriteThere()
    {
        var keys = Path.Combine(_library.BasePath, "keys");
        Directory.CreateDirectory(keys);
        await File.WriteAllTextAsync(Path.Combine(keys, "key.xml"), "secret", Token);
        if (!TryLinkFolder("ChildDir/Keys", keys))
            Assert.Inconclusive("Folder links can't be made here.");

        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/folders", new CreateFolderRequest("ChildDir/Keys", "x")));
        Assert.AreEqual(HttpStatusCode.NotFound, await PostStatusAsync(_admin, "/api/files/rename", new RenameRequest("ChildDir/Keys/key.xml", "stolen.xml")));

        Assert.IsTrue(File.Exists(Path.Combine(keys, "key.xml")));
        Assert.IsFalse(Directory.Exists(Path.Combine(keys, "x")));
    }

    private async Task RuleAsync(string path, FolderLevel level) =>
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, path, FolderPrincipalRef.Group(_users), level, null, Token)).Status);

    private bool TryLinkFolder(string link, string target)
    {
        var linkPath = _library.FullPath(link);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                })!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    return false;
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }
            _links.Add(linkPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private async Task<LibraryItem> OkAsync(HttpClient client, string url, object body)
    {
        using var response = await PostAsync(client, url, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<LibraryItem>(Json, Token))!;
    }

    private Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body) => client.PostAsJsonAsync(url, body, Json, Token);

    private async Task<HttpStatusCode> PostStatusAsync(HttpClient client, string url, object body)
    {
        using var response = await PostAsync(client, url, body);
        return response.StatusCode;
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
}
