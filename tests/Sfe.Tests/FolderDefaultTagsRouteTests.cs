using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary><c>GET|PUT api/folder-default-tags</c>, and what listings say of folders with default tags.</summary>
[TestClass]
public sealed class FolderDefaultTagsRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _ada = null!;
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
        var administrators = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Administrators, Token))!;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        await accounts.CreateUserAsync(new UserInput("ada", Password: "ada-password-1", GroupIds: [administrators.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(administrators.Id), FolderLevel.Hidden, null, Token)).Status);
        _ada = await ApiClients.SignInAsync(_factory, "ada", "ada-password-1", Token);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _alice?.Dispose();
        _ada?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task MembersSetDefaultTagsThatListingsShowAndTheFilesAlreadyThereGetWhenAsked()
    {
        var result = await PutAsync(_alice, new FolderDefaultTagsChange("RepeatedDir", Add: [BuiltInTags.Favorite], AddToFiles: true));

        CollectionAssert.AreEqual(new[] { BuiltInTags.Favorite }, result.Defaults.Own.Select(t => t.Id).ToArray());
        Assert.AreEqual((6, 6), (result.Files!.Files, result.Files.Added), "Indexed first, with the sub-folders.");

        var root = await BrowseAsync(_alice, "");
        CollectionAssert.AreEqual(new[] { "Favorite" }, root.Items.Single(i => i.Name == "RepeatedDir").DefaultTags!.Select(t => t.Name).ToArray());
        Assert.IsNull(root.Items.Single(i => i.Name == "ChildDir").DefaultTags);
        Assert.IsNull(root.DefaultTags);
        var child = await BrowseAsync(_alice, "RepeatedDir/ChildDir");
        Assert.AreEqual(("RepeatedDir", BuiltInTags.Favorite), (child.DefaultTags!.Inherited.Single().Path, child.DefaultTags.Inherited.Single().Tag.Id));
        var got = (await _alice.GetFromJsonAsync<FolderDefaults>("/api/folder-default-tags?path=RepeatedDir/ChildDir", Json, Token))!;
        Assert.AreEqual("RepeatedDir", got.Inherited.Single().Path);

        // Taken off the folder only: the files keep it.
        var kept = await PutAsync(_alice, new FolderDefaultTagsChange("RepeatedDir", Remove: [BuiltInTags.Favorite]));
        Assert.IsTrue(kept.Defaults.IsEmpty);
        Assert.IsNull(kept.Files);
        Assert.IsNotEmpty((await BrowseAsync(_alice, "RepeatedDir")).Items.Single(i => i.Name == "RDFile1.txt").Tags!);

        // Taken off the files too.
        await PutAsync(_alice, new FolderDefaultTagsChange("RepeatedDir", Add: [BuiltInTags.Favorite]));
        var removed = await PutAsync(_alice, new FolderDefaultTagsChange("RepeatedDir", Remove: [BuiltInTags.Favorite], RemoveFromFiles: true));
        Assert.AreEqual(6, removed.Files!.Removed);
        Assert.IsEmpty((await BrowseAsync(_alice, "RepeatedDir")).Items.Single(i => i.Name == "RDFile1.txt").Tags!);
    }

    [TestMethod]
    public async Task FoldersNotSeenOrMissingAndEmptyChangesAreRefused()
    {
        using (var hidden = await _ada.PutAsJsonAsync("/api/folder-default-tags", new FolderDefaultTagsChange("SpecialDir", Add: [BuiltInTags.Favorite]), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var hidden = await _ada.GetAsync("/api/folder-default-tags?path=SpecialDir", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var missing = await _admin.PutAsJsonAsync("/api/folder-default-tags", new FolderDefaultTagsChange("Nowhere", Add: [BuiltInTags.Favorite]), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        using (var file = await _admin.PutAsJsonAsync("/api/folder-default-tags", new FolderDefaultTagsChange("RFile1.txt", Add: [BuiltInTags.Favorite]), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, file.StatusCode);
        using (var empty = await _admin.PutAsJsonAsync("/api/folder-default-tags", new FolderDefaultTagsChange("Media"), Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        using (var unknown = await _admin.PutAsJsonAsync("/api/folder-default-tags", new FolderDefaultTagsChange("Media", Add: [9999]), Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var visitor = ApiClients.Create(_factory);
        using (var anonymous = await visitor.PutAsJsonAsync("/api/folder-default-tags", new FolderDefaultTagsChange("Media", Add: [BuiltInTags.Favorite]), Token))
            Assert.AreNotEqual(HttpStatusCode.OK, anonymous.StatusCode);

        Assert.IsTrue((await _admin.GetFromJsonAsync<FolderDefaults>("/api/folder-default-tags?path=Media", Json, Token))!.IsEmpty);
    }

    [TestMethod]
    public void TheRightIsGivenToTheMembersOfExistingSpaces()
    {
        var right = RightCatalog.Find(Rights.FolderDefaultTags)!;
        Assert.AreEqual(RightScope.Tenant, right.Scope);
        Assert.IsFalse(right.GuestAllowed);
        CollectionAssert.AreEqual(new[] { BuiltInGroup.Administrators, BuiltInGroup.Users }, right.DefaultGroups.ToArray());
        Assert.AreEqual(12, right.Since, "a right added after 1.34 is given to existing spaces");
    }

    private async Task<FolderDefaultTagsResult> PutAsync(HttpClient client, FolderDefaultTagsChange change)
    {
        using var response = await client.PutAsJsonAsync("/api/folder-default-tags", change, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FolderDefaultTagsResult>(Json, Token))!;
    }

    private async Task<DirectoryListing> BrowseAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<DirectoryListing>($"/api/browse?path={Uri.EscapeDataString(path)}", Json, Token))!;
}
