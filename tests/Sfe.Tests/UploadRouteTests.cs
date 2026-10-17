using System.Net;
using System.Net.Http.Headers;
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
/// Uploads through HTTP. Alice is in Users, which may upload, with SpecialDir hidden and Media read-only; Carol may
/// only browse; the administrator sees everyone's unfinished uploads.
/// </summary>
[TestClass]
public sealed class UploadRouteTests
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
        Assert.AreEqual(AccessStatus.Ok, (await accounts.UpdateGroupAsync(tenant, users.Id,
            new GroupInput(users.Name, Rights: [.. users.Rights, Rights.UploadFiles], MemberIds: users.MemberIds), cancellationToken: Token)).Status);
        var readers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Readers", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("carol", Password: "carol-password-1", GroupIds: [readers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "Media", FolderPrincipalRef.Group(users.Id), FolderLevel.Read, null, Token)).Status);
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
    public async Task AFileIsStartedSentInChunksAndFinished()
    {
        var content = Enumerable.Range(0, 50_000).Select(i => (byte)i).ToArray();
        var upload = await StartAsync(_alice, new UploadStart("ChildDir", "photo.jpg", content.Length));

        using (var first = await ChunkAsync(_alice, upload.Id, 0, content[..20_000]))
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync(Token));
        using (var repeated = await ChunkAsync(_alice, upload.Id, 0, content[..20_000]))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, repeated.StatusCode);
            Assert.AreEqual(20_000, (await repeated.Content.ReadFromJsonAsync<UploadInfo>(Json, Token))!.Received, "Resend from there");
        }
        using (var second = await ChunkAsync(_alice, upload.Id, 20_000, content[20_000..]))
            Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        using var finished = await _alice.PostAsJsonAsync($"/api/files/uploads/{upload.Id}/finish", new UploadFinish(), Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, finished.StatusCode, await finished.Content.ReadAsStringAsync(Token));
        Assert.AreEqual("ChildDir/photo.jpg", (await finished.Content.ReadFromJsonAsync<LibraryItem>(Json, Token))!.Path);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(_library.FullPath("ChildDir/photo.jpg"), Token));
    }

    [TestMethod]
    public async Task UploadsFollowRightsAndFolderRules()
    {
        using (var readOnly = await _alice.PostAsJsonAsync("/api/files/uploads", new UploadStart("Media", "a.jpg", 1), Json, Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, readOnly.StatusCode);
        using (var hidden = await _alice.PostAsJsonAsync("/api/files/uploads", new UploadStart("SpecialDir", "a.jpg", 1), Json, Token))
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var noRight = await _carol.PostAsJsonAsync("/api/files/uploads", new UploadStart("ChildDir", "a.jpg", 1), Json, Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, noRight.StatusCode);
        using (var escape = await _alice.PostAsJsonAsync("/api/files/uploads", new UploadStart("ChildDir", "../../a.jpg", 1), Json, Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, escape.StatusCode);
        using (var intoHidden = await _alice.PostAsJsonAsync("/api/files/uploads", new UploadStart("", "SpecialDir/a.jpg", 1), Json, Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, intoHidden.StatusCode, "A folder upload into a folder hidden from her");
        Assert.IsFalse(File.Exists(_library.FullPath("SpecialDir/a.jpg")));
    }

    [TestMethod]
    public async Task TheCheckTellsWhichNamesAreTakenEvenHiddenOnes()
    {
        using var response = await _alice.PostAsJsonAsync("/api/files/uploads/check",
            new UploadCheckRequest("", ["RFile1.txt", "new.txt", "SpecialDir", "ChildDir/AFile1.txt", "../RFile1.txt"]), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        var check = (await response.Content.ReadFromJsonAsync<UploadCheck>(Json, Token))!;

        CollectionAssert.AreEquivalent(new[] { "RFile1.txt", "SpecialDir", "ChildDir/AFile1.txt" }, check.Existing.ToArray());
        Assert.IsFalse(check.CanReplace, "Alice may not delete");
    }

    [TestMethod]
    public async Task UnfinishedUploadsAreTheOwnersAndTheAdministratorsOnly()
    {
        var upload = await StartAsync(_alice, new UploadStart("ChildDir", "big.mp4", 100));

        var mine = await _alice.GetFromJsonAsync<UploadInfo[]>("/api/files/uploads", Json, Token);
        Assert.AreEqual(upload.Id, mine!.Single().Id);
        using (var notAll = await _alice.GetAsync("/api/files/uploads?all=true", Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, notAll.StatusCode);
        Assert.IsEmpty((await _admin.GetFromJsonAsync<UploadInfo[]>("/api/files/uploads", Json, Token))!);
        var all = await _admin.GetFromJsonAsync<UploadInfo[]>("/api/files/uploads?all=true", Json, Token);
        Assert.AreEqual("alice", all!.Single().OwnerName);
        using (var foreign = await ChunkAsync(_admin, upload.Id, 0, [1, 2]))
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode, "Nobody else sends into it");

        using (var cancelled = await _admin.DeleteAsync($"/api/files/uploads/{upload.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.IsEmpty((await _alice.GetFromJsonAsync<UploadInfo[]>("/api/files/uploads", Json, Token))!);
        using (var gone = await _alice.GetAsync($"/api/files/uploads/{upload.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [TestMethod]
    public async Task ANameTakenAtTheFinishIsAskedThenKeptBoth()
    {
        var upload = await StartAsync(_alice, new UploadStart("", "RFile1.txt", 2));
        using (var chunk = await ChunkAsync(_alice, upload.Id, 0, [1, 2]))
            Assert.AreEqual(HttpStatusCode.OK, chunk.StatusCode);

        using (var asked = await _alice.PostAsJsonAsync($"/api/files/uploads/{upload.Id}/finish", new UploadFinish(), Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, asked.StatusCode);
            StringAssert.Contains(await asked.Content.ReadAsStringAsync(Token), FileEndpoints.ExistsType);
        }
        using (var replace = await _alice.PostAsJsonAsync($"/api/files/uploads/{upload.Id}/finish", new UploadFinish(ConflictChoice.Replace), Json, Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, replace.StatusCode, "Replacing needs the right to delete");
        using var kept = await _alice.PostAsJsonAsync($"/api/files/uploads/{upload.Id}/finish", new UploadFinish(ConflictChoice.KeepBoth), Json, Token);

        Assert.AreEqual("RFile1 (2).txt", (await kept.Content.ReadFromJsonAsync<LibraryItem>(Json, Token))!.Path);
        Assert.AreEqual(10, new FileInfo(_library.FullPath("RFile1.txt")).Length);
    }

    [TestMethod]
    public async Task AChunkLargerThanTheLimitIsRefused()
    {
        var upload = await StartAsync(_alice, new UploadStart("ChildDir", "huge.bin", Uploads.MaxChunkBytes * 2L));

        using var response = await ChunkAsync(_alice, upload.Id, 0, new byte[Uploads.MaxChunkBytes + 10]);

        Assert.IsTrue(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, response.StatusCode.ToString());
        Assert.AreEqual(0, (await _alice.GetFromJsonAsync<UploadInfo>($"/api/files/uploads/{upload.Id}", Json, Token))!.Received);
    }

    private async Task<UploadInfo> StartAsync(HttpClient client, UploadStart start)
    {
        using var response = await client.PostAsJsonAsync("/api/files/uploads", start, Json, Token);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<UploadInfo>(Json, Token))!;
    }

    private Task<HttpResponseMessage> ChunkAsync(HttpClient client, string id, long offset, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"/api/files/uploads/{id}?offset={offset}", content, Token);
    }
}
