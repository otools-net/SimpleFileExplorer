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
using Sfe.Core.Indexing;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Properties through HTTP. Alice is in Users (browse, download) with SpecialDir hidden from them; Carol may browse and
/// open files but not download them.
/// </summary>
[TestClass]
public sealed class PropertyRouteTests
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
        var readers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Readers", Rights: [Rights.Browse, Rights.ViewFiles]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("carol", Password: "carol-password-1", GroupIds: [readers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
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
    public async Task AFilesPropertiesSayWhetherAChecksumIsOffered()
    {
        var alice = await GetAsync<ItemProperties>(_alice, "/api/files/properties?path=Media/photo.jpg");
        var carol = await GetAsync<ItemProperties>(_carol, "/api/files/properties?path=Media/photo.jpg");

        Assert.AreEqual("photo.jpg", alice.Item.Name);
        Assert.AreEqual(3 * 1024, alice.Details.Size);
        Assert.IsTrue(alice.CanChecksum);
        Assert.IsFalse(carol.CanChecksum, "Carol may not download");
        using var refused = await _carol.PostAsJsonAsync("/api/files/properties/checksum", new ChecksumRequest("Media/photo.jpg"), Json, Token);
        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.AreEqual("Root", (await GetAsync<ItemProperties>(_alice, "/api/files/properties?path=")).Item.Name);
    }

    [TestMethod]
    public async Task HiddenAndProtectedItemsHaveNoProperties()
    {
        _library.AddFile(".sfe/trash/x.txt", 10);

        foreach (var path in new[] { "SpecialDir", "SpecialDir/SDEspañol", ".sfe", "Nothing", "../Root" })
        {
            using var response = await _alice.GetAsync($"/api/files/properties?path={Uri.EscapeDataString(path)}", Token);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
        }
        using var count = await _alice.PostAsJsonAsync("/api/files/properties/count", new CountRequest("SpecialDir"), Json, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, count.StatusCode);
    }

    [TestMethod]
    public async Task TotalsAndCountsLeaveOutWhatTheCallerDoesNotSee()
    {
        using (var scan = await _admin.PostAsJsonAsync("/api/scan", new ScanRequest(""), Json, Token))
            Assert.IsTrue(scan.IsSuccessStatusCode);
        for (var attempt = 0; (await GetAsync<ScanStatus>(_admin, "/api/scan")).State == ScanState.Running; attempt++)
        {
            Assert.IsLessThan(200, attempt, "The scan did not finish in time");
            await Task.Delay(50, Token);
        }

        var all = await GetAsync<FolderTotals>(_admin, "/api/files/properties/totals?path=");
        var seen = await GetAsync<FolderTotals>(_alice, "/api/files/properties/totals?path=");
        var counted = await CountAsync(_alice, "");

        Assert.AreEqual(21, all.Files);
        Assert.AreEqual(15, seen.Files, "SpecialDir's six files are not counted for Alice");
        Assert.AreEqual(all.Folders - 2, seen.Folders);
        Assert.AreEqual(15, counted.Files);
        Assert.AreEqual(seen.Folders, counted.Folders);
    }

    [TestMethod]
    public async Task AJobIsItsStartersOnly()
    {
        using var started = await _alice.PostAsJsonAsync("/api/files/properties/checksum", new ChecksumRequest("Media/photo.jpg", ChecksumAlgorithm.Md5), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, started.StatusCode, await started.Content.ReadAsStringAsync(Token));
        var job = (await started.Content.ReadFromJsonAsync<PropertyJobView>(Json, Token))!;

        using (var other = await _admin.GetAsync($"/api/files/properties/jobs/{job.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        using (var stop = await _admin.DeleteAsync($"/api/files/properties/jobs/{job.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, stop.StatusCode);
        var done = await WaitAsync(_alice, job.Id);
        Assert.AreEqual(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(await File.ReadAllBytesAsync(_library.FullPath("Media/photo.jpg"), Token))), done.Checksum);
    }

    private async Task<PropertyJobView> CountAsync(HttpClient client, string path)
    {
        using var started = await client.PostAsJsonAsync("/api/files/properties/count", new CountRequest(path), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, started.StatusCode, await started.Content.ReadAsStringAsync(Token));
        return await WaitAsync(client, (await started.Content.ReadFromJsonAsync<PropertyJobView>(Json, Token))!.Id);
    }

    private async Task<PropertyJobView> WaitAsync(HttpClient client, string id)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var job = await GetAsync<PropertyJobView>(client, $"/api/files/properties/jobs/{id}");
            if (job.Done)
                return job;
            await Task.Delay(25, Token);
        }
        throw new AssertFailedException("The job did not end in time.");
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }
}
