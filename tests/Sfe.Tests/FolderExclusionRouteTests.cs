using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary><c>PUT api/index/exclusions</c>, what listings say of excluded folders, and scans of them.</summary>
[TestClass]
public sealed class FolderExclusionRouteTests
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
        _library.AddFile("Media/Sub/deeper.txt", 10);
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
    public async Task SpaceAdministratorsExcludeAFolderThatListingsThenMark()
    {
        var state = await PutAsync(_ada, new FolderExclusionChange("Media", Scan: true, Trickplay: null, Thumbnails: true));
        Assert.AreEqual(new FolderExclusionState("Media", null, "Media"), state);

        // Everyone browsing sees it: on the folder's item, in the folder, and on its sub-folders.
        var root = await BrowseAsync(_alice, "");
        Assert.AreEqual(new FolderExclusionState("Media", null, "Media"), root.Items.Single(i => i.Name == "Media").Excluded);
        Assert.IsNull(root.Items.Single(i => i.Name == "ChildDir").Excluded);
        Assert.IsNull(root.Exclusions);
        var media = await BrowseAsync(_alice, "Media");
        Assert.AreEqual(new FolderExclusionState("Media", null, "Media"), media.Exclusions);
        Assert.AreEqual(new FolderExclusionState("Media", null, "Media"), media.Items.Single(i => i.Name == "Sub").Excluded);

        // Saved for the space, like the settings page does.
        var settings = await _admin.GetFromJsonAsync<SettingsView>("/api/settings?level=tenant", Json, Token);
        CollectionAssert.AreEqual(new[] { "Media" }, settings!.Values.Scan.ExcludedFolders);
        CollectionAssert.AreEqual(new[] { "Media" }, settings.Values.Thumbnails.ExcludedFolders);
        Assert.IsEmpty(settings.Values.Trickplay.ExcludedFolders);

        // A scan of it, or inside it, has nothing to do; a scan of the library skips it.
        foreach (var path in new[] { "Media", "Media/Sub" })
        {
            using var refused = await _admin.PostAsJsonAsync("/api/scan", new ScanRequest(path), Token);
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode, path);
            StringAssert.Contains(await refused.Content.ReadAsStringAsync(Token), "excluded from scans");
        }

        // A sub-folder is let in only through the folder listed.
        using (var inherited = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("Media/Sub", false, null, null), Token))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, inherited.StatusCode);
            StringAssert.Contains(await inherited.Content.ReadAsStringAsync(Token), "“Media”");
        }
        Assert.AreEqual(new FolderExclusionState(null, null, "Media"), await PutAsync(_ada, new FolderExclusionChange("Media", false, null, null)));
        Assert.AreEqual(new FolderExclusionState(null, "Media/Sub", "Media"), await PutAsync(_ada, new FolderExclusionChange("Media/Sub", null, true, null)));
    }

    [TestMethod]
    public async Task OnlyTheRightAndAVisibleFolderAllowIt()
    {
        using (var member = await _alice.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("Media", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, member.StatusCode, "Users don't have the right");
        using (var hidden = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("SpecialDir", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode, "a folder hidden from her");
        using (var missing = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("Nowhere", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        using (var file = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("RFile1.txt", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, file.StatusCode, "files are not excluded, folders are");
        using (var library = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, library.StatusCode, "the library itself");
        using (var outside = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("../..", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, outside.StatusCode);

        var settings = await _admin.GetFromJsonAsync<SettingsView>("/api/settings?level=tenant", Json, Token);
        Assert.IsEmpty(settings!.Values.Scan.ExcludedFolders);
    }

    [TestMethod]
    public async Task TheListShowsEveryExcludedFolderSeenAndLetsAGoneOneBeRemoved()
    {
        await PutAsync(_admin, new FolderExclusionChange("Media", Scan: true, Trickplay: null, Thumbnails: null));
        await PutAsync(_admin, new FolderExclusionChange("ChildDir", Scan: null, Trickplay: true, Thumbnails: true));
        await PutAsync(_admin, new FolderExclusionChange("SpecialDir", Scan: true, Trickplay: null, Thumbnails: null));
        Directory.Delete(_library.FullPath("ChildDir"), recursive: true);

        CollectionAssert.AreEqual(new[]
        {
            new ExcludedFolder("ChildDir", false, true, true, false),
            new ExcludedFolder("Media", true, false, false, true),
            new ExcludedFolder("SpecialDir", true, false, false, true),
        }, await ListAsync(_admin));
        // A space administrator doesn't see the folder hidden from her, even by name.
        CollectionAssert.AreEqual(new[] { "ChildDir", "Media" }, (await ListAsync(_ada)).Select(f => f.Path).ToArray());
        using (var member = await _alice.GetAsync("/api/index/exclusions", Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, member.StatusCode);

        // Gone from the disk: it can be taken off the lists, not put on more of them.
        using (var more = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("ChildDir", true, null, null), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, more.StatusCode);
        Assert.AreEqual(new FolderExclusionState(null, null, null), await PutAsync(_ada, new FolderExclusionChange("ChildDir", null, false, false)));
        using (var never = await _ada.PutAsJsonAsync("/api/index/exclusions", new FolderExclusionChange("Nowhere", false, null, null), Token))
            Assert.AreEqual(HttpStatusCode.NotFound, never.StatusCode, "a folder on no list and not on the disk");
        CollectionAssert.AreEqual(new[] { "Media" }, (await ListAsync(_ada)).Select(f => f.Path).ToArray());
    }

    private async Task<ExcludedFolder[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ExcludedFolder[]>("/api/index/exclusions", Json, Token))!;

    private async Task<FolderExclusionState> PutAsync(HttpClient client, FolderExclusionChange change)
    {
        using var response = await client.PutAsJsonAsync("/api/index/exclusions", change, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FolderExclusionState>(Json, Token))!;
    }

    private async Task<DirectoryListing> BrowseAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<DirectoryListing>($"/api/browse?path={Uri.EscapeDataString(path)}", Json, Token))!;
}
