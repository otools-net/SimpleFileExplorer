using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Deleting and the trash through HTTP. Alice is in Users, which may delete to the trash (not permanently), with
/// SpecialDir hidden, Media read-only and RepeatedDir/RepeatedDir hidden; Carol may also delete permanently.
/// </summary>
[TestClass]
public sealed class TrashRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _carol = null!;
    private FolderRuleService _rules = null!;
    private int _tenant;
    private int _users;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var users = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Token))!;
        _users = users.Id;
        Assert.AreEqual(AccessStatus.Ok, (await accounts.UpdateGroupAsync(_tenant, _users,
            new GroupInput(users.Name, Rights: [.. users.Rights, Rights.DeleteFiles], MemberIds: users.MemberIds), cancellationToken: Token)).Status);
        var cleaners = (await accounts.CreateGroupAsync(_tenant, new GroupInput("Cleaners", Rights: [Rights.Browse, Rights.DeleteFiles, Rights.DeletePermanently]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [_users]), null, Token);
        await accounts.CreateUserAsync(new UserInput("carol", Password: "carol-password-1", GroupIds: [cleaners.Id]), null, Token);
        _rules = _factory.Services.GetRequiredService<FolderRuleService>();
        await RuleAsync("SpecialDir", FolderLevel.Hidden);
        await RuleAsync("Media", FolderLevel.Read);
        await RuleAsync("RepeatedDir/RepeatedDir", FolderLevel.Hidden);
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, "SpecialDir", FolderPrincipalRef.Group(cleaners.Id), FolderLevel.Hidden, null, Token)).Status);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _carol = await ApiClients.SignInAsync(_factory, "carol", "carol-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _carol?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task EachPathGetsItsOwnAnswer()
    {
        var results = await DeleteAsync(_alice, ["ChildDir/AFile1.txt", "Media/photo.jpg", "SpecialDir/SDEspañol", "RepeatedDir", "Nowhere", "ChildDir/AFile1.txt"]);

        var byPath = results.Items.ToDictionary(i => i.Path);
        Assert.HasCount(5, results.Items, "Each path once");
        Assert.AreEqual(FileOperationStatus.Done, byPath["ChildDir/AFile1.txt"].Status);
        Assert.IsNotNull(byPath["ChildDir/AFile1.txt"].TrashId);
        Assert.AreEqual(FileOperationStatus.Forbidden, byPath["Media/photo.jpg"].Status, "Read-only folder");
        Assert.AreEqual(FileOperationStatus.NotFound, byPath["SpecialDir/SDEspañol"].Status, "Hidden folder");
        Assert.AreEqual(FileOperationStatus.Forbidden, byPath["RepeatedDir"].Status, "Holds a folder Alice doesn't see");
        Assert.AreEqual(FileOperationStatus.NotFound, byPath["Nowhere"].Status);
        Assert.IsFalse(results.Permanent);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));
        Assert.IsTrue(Directory.Exists(_library.FullPath("RepeatedDir")));
    }

    [TestMethod]
    public async Task DeletingForGoodNeedsItsOwnRight()
    {
        var refused = await DeleteAsync(_alice, ["ChildDir/AFile1.txt"], permanent: true);
        var done = await DeleteAsync(_carol, ["ChildDir/AFile2"], permanent: true);

        Assert.AreEqual(FileOperationStatus.Forbidden, refused.Items.Single().Status);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/AFile1.txt")));
        Assert.AreEqual(FileOperationStatus.Done, done.Items.Single().Status);
        Assert.IsNull(done.Items.Single().TrashId);
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/AFile2")));
        Assert.IsEmpty((await ListAsync(_admin)).Items);
    }

    [TestMethod]
    public async Task WhenTheSpaceDeletesPermanentlyTheTrashIsSkipped()
    {
        using (var saved = await _admin.PatchAsJsonAsync("/api/settings?level=tenant", new { values = new Dictionary<string, object> { ["files.deletePermanently"] = true } }, Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Token));

        var alice = await DeleteAsync(_alice, ["ChildDir/AFile1.txt"]);
        var carol = await DeleteAsync(_carol, ["ChildDir/AFile2"]);
        using var config = await _alice.GetAsync("/api/config", Token);

        Assert.AreEqual(FileOperationStatus.Forbidden, alice.Items.Single().Status, "Trash-only rights can't delete then");
        Assert.IsTrue(carol.Permanent);
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/AFile2")));
        Assert.IsEmpty((await ListAsync(_admin)).Items);
        StringAssert.Contains(await config.Content.ReadAsStringAsync(Token), "\"deletePermanently\":true");
    }

    [TestMethod]
    public async Task ATrashedFolderTakesItsRulesAlongAndGetsThemBack()
    {
        var guests = (await _factory.Services.GetRequiredService<AccessService>().GetBuiltInGroupAsync(_tenant, BuiltInGroup.Guests, Token))!.Id;
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, "ChildDir", FolderPrincipalRef.Group(guests), FolderLevel.Hidden, null, Token)).Status);

        var id = (await DeleteAsync(_admin, ["ChildDir"])).Items.Single().TrashId!.Value;
        var whileTrashed = await _rules.GetAsync(_tenant, Token);
        using var hidden = await _alice.PostAsync($"/api/files/trash/{id}/restore", null, Token);
        using var restore = await _admin.PostAsync($"/api/files/trash/{id}/restore", null, Token);

        Assert.IsFalse(whileTrashed.Any(r => r.Path == "ChildDir"), "No stale rule where the folder was");
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode, "A folder with rules of its own: only those managing access see it");
        Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode, await restore.Content.ReadAsStringAsync(Token));
        Assert.AreEqual("ChildDir", (await restore.Content.ReadFromJsonAsync<LibraryItem>(Json, Token))!.Path);
        Assert.IsTrue((await _rules.GetAsync(_tenant, Token)).Any(r => r.Path == "ChildDir" && r.Principal.GroupId == guests && r.Level == FolderLevel.Hidden));
    }

    [TestMethod]
    public async Task EveryoneSeesWhatTheyDeletedAndWhatWasDeletedWhereTheyMayDelete()
    {
        var mine = (await DeleteAsync(_alice, ["ChildDir/AFile1.txt"])).Items.Single().TrashId!.Value;
        var inChildDir = (await DeleteAsync(_admin, ["ChildDir/AFile2"])).Items.Single().TrashId!.Value;
        var inMedia = (await DeleteAsync(_admin, ["Media/photo.jpg"])).Items.Single().TrashId!.Value;
        var inHidden = (await DeleteAsync(_admin, ["SpecialDir"])).Items.Single().TrashId!.Value;

        var alice = (await ListAsync(_alice)).Items.Select(i => i.Id).ToList();
        var admin = (await ListAsync(_admin)).Items.Select(i => i.Id).ToList();
        using var restoreMedia = await _alice.PostAsync($"/api/files/trash/{inMedia}/restore", null, Token);
        using var restoreHidden = await _alice.PostAsync($"/api/files/trash/{inHidden}/restore", null, Token);
        using var purge = await _alice.DeleteAsync($"/api/files/trash/{mine}", Token);
        using var bob = ApiClients.Create(_factory);

        CollectionAssert.AreEquivalent(new[] { mine, inChildDir }, alice);
        CollectionAssert.AreEquivalent(new[] { mine, inChildDir, inMedia, inHidden }, admin);
        Assert.AreEqual(HttpStatusCode.NotFound, restoreMedia.StatusCode, "Not hers to see");
        Assert.AreEqual(HttpStatusCode.NotFound, restoreHidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, purge.StatusCode, "Purging needs the right to delete permanently");
        Assert.AreNotEqual(HttpStatusCode.OK, (await bob.GetAsync("/api/files/trash", Token)).StatusCode);
    }

    [TestMethod]
    public async Task EmptyingTakesWhatTheCallerMayDeleteForGood()
    {
        await DeleteAsync(_admin, ["ChildDir/AFile1.txt", "Media/photo.jpg", "SpecialDir"]);

        using var carol = await _carol.DeleteAsync("/api/files/trash", Token);
        var left = (await ListAsync(_admin)).Items.Select(i => i.Path).ToList();
        using var admin = await _admin.DeleteAsync("/api/files/trash", Token);

        Assert.AreEqual(2, (await carol.Content.ReadFromJsonAsync<EmptiedTrash>(Json, Token))!.Purged, "Not the hidden folder");
        CollectionAssert.AreEqual(new[] { "SpecialDir" }, left);
        Assert.AreEqual(1, (await admin.Content.ReadFromJsonAsync<EmptiedTrash>(Json, Token))!.Purged);
        Assert.IsEmpty((await ListAsync(_admin)).Items);
    }

    [TestMethod]
    public async Task TheWorkFolderAndTheRootAreNeverDeleted()
    {
        _library.AddFile(".sfe/trash/x/kept.txt", 10);

        var results = await DeleteAsync(_admin, [".sfe", ".sfe/trash/x/kept.txt", "", "/"], permanent: true);

        Assert.IsTrue(results.Items.All(i => i.Status == FileOperationStatus.NotFound), string.Join(", ", results.Items.Select(i => $"{i.Path}: {i.Status}")));
        Assert.IsTrue(File.Exists(_library.FullPath(".sfe/trash/x/kept.txt")));
    }

    private async Task<DeleteResults> DeleteAsync(HttpClient client, string[] paths, bool permanent = false)
    {
        using var response = await client.PostAsJsonAsync("/api/files/delete", new DeleteRequest(paths, permanent), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<DeleteResults>(Json, Token))!;
    }

    private async Task<TrashPage> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/files/trash", Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TrashPage>(Json, Token))!;
    }

    private async Task RuleAsync(string path, FolderLevel level) =>
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, path, FolderPrincipalRef.Group(_users), level, null, Token)).Status);
}
