using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>Each account's home folder: its own choice over its groups' default, never created, through HTTP.</summary>
[TestClass]
public sealed class HomeFolderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const string Url = $"{V1}/me/home-folder";

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task NoneUntilChosenOrGivenByAGroup()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        var home = await GetAsync<HomeFolderView>(admin, Url);

        Assert.IsNull(home.Path);
        Assert.IsNull(home.Name);
        Assert.IsFalse(home.Exists);
        Assert.IsFalse(home.Chosen);
        Assert.IsNull(home.Group);
    }

    [TestMethod]
    public async Task AGroupsDefaultNamesTheAccountAndIsNeverCreated()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Token);
        var users = await BuiltInAsync(admin, BuiltInGroup.Users);
        var changed = await UpdateGroupAsync(admin, users, @"\Homes\\{User}/");
        var alice = await AccountAsync(factory, admin, "alice", users.Id);

        var home = await GetAsync<HomeFolderView>(alice, Url);

        Assert.AreEqual("Homes/{User}", changed.HomeFolder, "Cleaned as a path of the library, as typed otherwise");
        Assert.AreEqual("Homes/alice", home.Path);
        Assert.AreEqual("alice", home.Name);
        Assert.IsFalse(home.Chosen);
        Assert.AreEqual("Users", home.Group);
        Assert.AreEqual("Homes/alice", home.GroupPath);
        Assert.IsFalse(home.Exists, "Not made yet");
        Assert.IsFalse(Directory.Exists(_library.FullPath("Homes")), "SFE never makes it");

        Directory.CreateDirectory(_library.FullPath("Homes/alice"));
        Assert.IsTrue((await GetAsync<HomeFolderView>(alice, Url)).Exists);
    }

    [TestMethod]
    public async Task AGroupMadeByHandGoesBeforeTheBuiltInOnes()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Token);
        var users = await BuiltInAsync(admin, BuiltInGroup.Users);
        await UpdateGroupAsync(admin, users, "Homes/{user}");
        var alice = await AccountAsync(factory, admin, "alice", users.Id);
        var family = await CreateGroupAsync(admin, new GroupInput("Family", HomeFolder: "Family/{user}"));
        await CreateGroupAsync(admin, new GroupInput("Zoo", HomeFolder: "Zoo"));
        foreach (var group in (await GroupsAsync(admin)).Where(g => g.Name is "Family" or "Zoo"))
            await UpdateGroupAsync(admin, group with { MemberIds = [.. group.MemberIds, _aliceId] });

        var home = await GetAsync<HomeFolderView>(alice, Url);

        Assert.AreEqual("Family/alice", home.Path);
        Assert.AreEqual(family.Name, home.Group);
    }

    [TestMethod]
    public async Task AnAccountsChoiceGoesOverTheDefaultUntilItIsForgotten()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Token);
        var users = await BuiltInAsync(admin, BuiltInGroup.Users);
        await UpdateGroupAsync(admin, users, "Homes/{user}");
        var alice = await AccountAsync(factory, admin, "alice", users.Id);

        var chosen = await SendAsync(alice, HttpMethod.Put, new HomeFolderInput("ChildDir/"));
        var admins = await GetAsync<HomeFolderView>(admin, Url);
        var forgotten = await SendAsync(alice, HttpMethod.Delete, null);

        Assert.AreEqual("ChildDir", chosen.Path);
        Assert.IsTrue(chosen.Chosen);
        Assert.IsTrue(chosen.Exists);
        Assert.AreEqual("Homes/alice", chosen.GroupPath, "What forgetting the choice goes back to");
        Assert.IsNull(admins.Path, "Each account has its own");
        Assert.AreEqual("Homes/alice", forgotten.Path);
        Assert.IsFalse(forgotten.Chosen);
    }

    [TestMethod]
    public async Task TheRootCanBeChosen()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        var home = await SendAsync(admin, HttpMethod.Put, new HomeFolderInput(""));

        Assert.AreEqual("", home.Path);
        Assert.AreEqual("Root", home.Name);
        Assert.IsTrue(home.Exists);
    }

    [TestMethod]
    [DataRow("RFile1.txt", DisplayName = "a file")]
    [DataRow("Nowhere", DisplayName = "missing")]
    [DataRow("../Root", DisplayName = "outside")]
    public async Task OnlyAFolderOfTheLibraryCanBeChosen(string path)
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        using var response = await admin.PutAsJsonAsync(Url, new HomeFolderInput(path), Json, Token);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsNull((await GetAsync<HomeFolderView>(admin, Url)).Path);
    }

    [TestMethod]
    public async Task AGroupsDefaultIsCheckedAndKeptWhenNotSent()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        var users = await BuiltInAsync(admin, BuiltInGroup.Users);
        await UpdateGroupAsync(admin, users, "Homes/{user}");

        using var refused = await admin.PutAsJsonAsync(
            $"{V1}/access/groups/{users.Id}",
            new GroupInput(users.Name, HomeFolder: "Homes/../x"),
            Json,
            Token
        );
        var problem = (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Token))!;
        var kept = await UpdateGroupAsync(admin, users with { HomeFolder = null });
        var cleared = await UpdateGroupAsync(admin, users, "  ");

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.IsTrue(problem.Errors.ContainsKey("homeFolder"));
        Assert.AreEqual("Homes/{user}", kept.HomeFolder, "Not sent: kept (older clients don't know it)");
        Assert.IsNull(cleared.HomeFolder, "Empty: none");
    }

    [TestMethod]
    public async Task ChoicesAndDefaultsFollowARenamedFolder()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Token);
        var users = await BuiltInAsync(admin, BuiltInGroup.Users);
        Directory.CreateDirectory(_library.FullPath("Homes/alice"));
        await UpdateGroupAsync(admin, users, "Homes/{user}");
        var alice = await AccountAsync(factory, admin, "alice", users.Id);
        await SendAsync(admin, HttpMethod.Put, new HomeFolderInput("ChildDir"));

        using (
            var renamed = await admin.PostAsJsonAsync($"{V1}/files/rename", new RenameRequest("Homes", "People"), Token)
        )
            Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode);
        using (
            var renamed = await admin.PostAsJsonAsync(
                $"{V1}/files/rename",
                new RenameRequest("ChildDir", "Kids"),
                Token
            )
        )
            Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode);

        Assert.AreEqual("People/{user}", (await BuiltInAsync(admin, BuiltInGroup.Users)).HomeFolder);
        Assert.AreEqual("People/alice", (await GetAsync<HomeFolderView>(alice, Url)).Path);
        Assert.AreEqual("Kids", (await GetAsync<HomeFolderView>(admin, Url)).Path);
    }

    [TestMethod]
    [DataRow("Homes/{user}", "a/b", "Homes/a_b", DisplayName = "separators in the name")]
    [DataRow("Homes/{user}", "..", null, DisplayName = "a name making no folder")]
    [DataRow("Shared", "alice", "Shared", DisplayName = "no placeholder")]
    public void ExpandsTheAccountsName(string template, string userName, string? expected) =>
        Assert.AreEqual(expected, HomeFolderService.Expand(template, userName));

    [TestMethod]
    public async Task VisitorsWithoutAnAccountHaveNone()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, Token);
        using (
            var open = await admin.PutAsJsonAsync($"{V1}/access/tenant/guest-access", new { guestAccess = true }, Token)
        )
            Assert.AreEqual(HttpStatusCode.OK, open.StatusCode);
        var visitor = ApiClients.Create(factory);
        _disposables.Add(visitor);

        using var get = await visitor.GetAsync(Url, Token);
        using var put = await visitor.PutAsJsonAsync(Url, new HomeFolderInput("Media"), Json, Token);

        Assert.IsFalse(RightCatalog.Find(Rights.HomeFolder)!.GuestAllowed);
        Assert.IsTrue(
            get.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            get.StatusCode.ToString()
        );
        Assert.IsTrue(
            put.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            put.StatusCode.ToString()
        );
    }

    /// <summary>
    /// An install of 1.85.0 has no HomeFolders table nor Groups.HomeFolder column, and its built-in groups lack the
    /// right: the update adds them to the SQLite file and gives the right to the Administrators and Users groups.
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTableTheColumnAndTheRight()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, Token);
        await using (var connection = new SqliteConnection(_library.ConnectionString))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DROP TABLE "HomeFolders";
                ALTER TABLE "Groups" DROP COLUMN "HomeFolder";
                DELETE FROM "GroupRights" WHERE "Right" = '{Rights.HomeFolder}';
                UPDATE "Tenants" SET "RightsRevision" = 14;
                UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.85.0'
                WHERE "Key" = '{ServerProperty.VersionKey}';
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            await command.ExecuteNonQueryAsync(Token);
        }

        var updated = Factory();
        await ApiClients.MigrateAsync(updated, Token);
        var admin = await ApiClients.SignInAsync(updated, ApiClients.AdminName, ApiClients.AdminPassword, Token);
        _disposables.Add(admin);
        var groups = await GroupsAsync(admin);
        var users = await UpdateGroupAsync(admin, groups.Single(g => g.BuiltIn == BuiltInGroup.Users), "Homes");
        var home = await SendAsync(admin, HttpMethod.Put, new HomeFolderInput("Media"));

        Assert.AreEqual("Homes", users.HomeFolder);
        Assert.AreEqual("Media", home.Path);
        Assert.Contains(Rights.HomeFolder, groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Rights);
        Assert.Contains(Rights.HomeFolder, groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Rights);
        Assert.DoesNotContain(Rights.HomeFolder, groups.Single(g => g.BuiltIn == BuiltInGroup.Guests).Rights);
    }

    private WebApplicationFactory<Program> Factory()
    {
        var factory = ApiClients.CreateFactory(_library);
        _disposables.Add(factory);
        return factory;
    }

    /// <summary>The account made last by <see cref="AccountAsync"/>.</summary>
    private int _aliceId;

    private async Task<HttpClient> AccountAsync(
        WebApplicationFactory<Program> factory,
        HttpClient admin,
        string name,
        int groupId
    )
    {
        using (
            var created = await admin.PostAsJsonAsync(
                $"{V1}/access/users",
                new UserInput(name, Password: $"{name}-password", GroupIds: [groupId]),
                Json,
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
            _aliceId = (await created.Content.ReadFromJsonAsync<UserInfo>(Json, Token))!.Id;
        }

        var client = await ApiClients.SignInAsync(factory, name, $"{name}-password", Token);
        _disposables.Add(client);
        return client;
    }

    private Task<List<GroupInfo>> GroupsAsync(HttpClient admin) =>
        GetAsync<List<GroupInfo>>(admin, $"{V1}/access/groups");

    private async Task<GroupInfo> BuiltInAsync(HttpClient admin, BuiltInGroup builtIn) =>
        (await GroupsAsync(admin)).Single(g => g.BuiltIn == builtIn);

    private Task<GroupInfo> UpdateGroupAsync(HttpClient admin, GroupInfo group, string homeFolder) =>
        UpdateGroupAsync(admin, group with { HomeFolder = homeFolder });

    private async Task<GroupInfo> UpdateGroupAsync(HttpClient admin, GroupInfo group)
    {
        using var response = await admin.PutAsJsonAsync(
            $"{V1}/access/groups/{group.Id}",
            new GroupInput(group.Name, group.ExternalName, group.Rights, group.MemberIds, group.HomeFolder),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<GroupInfo>(Json, Token))!;
    }

    private async Task<GroupInfo> CreateGroupAsync(HttpClient admin, GroupInput input)
    {
        using var response = await admin.PostAsJsonAsync($"{V1}/access/groups", input, Json, Token);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<GroupInfo>(Json, Token))!;
    }

    private async Task<HomeFolderView> SendAsync(HttpClient client, HttpMethod method, HomeFolderInput? input)
    {
        using var request = new HttpRequestMessage(method, Url);
        if (input is not null)
            request.Content = JsonContent.Create(input, options: Json);
        using var response = await client.SendAsync(request, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<HomeFolderView>(Json, Token))!;
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Token);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"{url}: {await response.Content.ReadAsStringAsync(Token)}"
        );
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }
}
