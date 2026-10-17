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
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Moves and copies through HTTP. Alice is in Users, which may move and copy, with SpecialDir hidden and Media
/// read-only; Carol may only browse. Visitors don't see RepeatedDir.
/// </summary>
[TestClass]
public sealed class FileJobRouteTests
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
            new GroupInput(users.Name, Rights: [.. users.Rights, Rights.MoveFiles, Rights.CopyFiles], MemberIds: users.MemberIds), cancellationToken: Token)).Status);
        var readers = (await accounts.CreateGroupAsync(_tenant, new GroupInput("Readers", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [_users]), null, Token);
        await accounts.CreateUserAsync(new UserInput("carol", Password: "carol-password-1", GroupIds: [readers.Id]), null, Token);
        _rules = _factory.Services.GetRequiredService<FolderRuleService>();
        await RuleAsync(FolderPrincipalRef.Group(_users), "SpecialDir", FolderLevel.Hidden);
        await RuleAsync(FolderPrincipalRef.Group(_users), "Media", FolderLevel.Read);
        var guests = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Guests, Token))!;
        await RuleAsync(FolderPrincipalRef.Group(guests.Id), "RepeatedDir", FolderLevel.Hidden);
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
    public async Task AMoveRunsInTheBackgroundAndReportsEachItem()
    {
        var started = await StartAsync(_alice, new FileJobRequest(FileJobKind.Move, ["ChildDir/AFile1.txt", "Media/photo.jpg", "SpecialDir/SDEspañol"], "RepeatedDir"));
        var job = await FinishedAsync(_alice, started.Id);

        Assert.AreEqual(FileJobState.Completed, job.State);
        var byPath = job.Results.ToDictionary(r => r.Path);
        Assert.AreEqual(FileOperationStatus.Done, byPath["ChildDir/AFile1.txt"].Status, byPath["ChildDir/AFile1.txt"].Message);
        Assert.AreEqual(FileOperationStatus.Forbidden, byPath["Media/photo.jpg"].Status, "Read-only folder");
        Assert.AreEqual(FileOperationStatus.NotFound, byPath["SpecialDir/SDEspañol"].Status, "Hidden folder");
        Assert.IsTrue(File.Exists(_library.FullPath("RepeatedDir/AFile1.txt")));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));

        var list = await _alice.GetFromJsonAsync<FileJobView[]>("/api/files/jobs", Json, Token);
        Assert.AreEqual(started.Id, list!.Single().Id);
    }

    [TestMethod]
    public async Task ACopyFromAReadOnlyFolderIsAllowed()
    {
        var job = await FinishedAsync(_alice, (await StartAsync(_alice, new FileJobRequest(FileJobKind.Copy, ["Media/photo.jpg"], "ChildDir"))).Id);

        Assert.AreEqual(FileOperationStatus.Done, job.Results.Single().Status, job.Results.Single().Message);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/photo.jpg")));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/photo.jpg")));
    }

    [TestMethod]
    public async Task ANameClashIsAskedAndAnsweredByItsOwnerOnly()
    {
        _library.AddFile("ChildDir/RFile1.txt", 5);
        var started = await StartAsync(_alice, new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "ChildDir"));
        var waiting = await WaitForAsync(_alice, started.Id, j => j.State == FileJobState.Waiting);
        Assert.AreEqual("RFile1.txt", waiting.Conflict!.Existing.Name);

        using (var other = await _admin.PostAsJsonAsync($"/api/files/jobs/{started.Id}/answer", new JobAnswer(ConflictChoice.Skip), Json, Token))
            Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode, "Someone else's job");
        using (var other = await _admin.GetAsync($"/api/files/jobs/{started.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        using (var answered = await _alice.PostAsJsonAsync($"/api/files/jobs/{started.Id}/answer", new JobAnswer(ConflictChoice.KeepBoth), Json, Token))
            Assert.AreEqual(HttpStatusCode.NoContent, answered.StatusCode);

        var job = await FinishedAsync(_alice, started.Id);
        Assert.AreEqual("ChildDir/RFile1 (2).txt", job.Results.Single().NewPath);
        using (var again = await _alice.PostAsJsonAsync($"/api/files/jobs/{started.Id}/answer", new JobAnswer(ConflictChoice.Skip), Json, Token))
            Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode, "Nothing asked");
    }

    [TestMethod]
    public async Task AWaitingJobCanBeCancelled()
    {
        _library.AddFile("ChildDir/RFile1.txt", 5);
        var started = await StartAsync(_alice, new FileJobRequest(FileJobKind.Move, ["RFile1.txt", "RFile2"], "ChildDir"));
        await WaitForAsync(_alice, started.Id, j => j.State == FileJobState.Waiting);

        using (var cancelled = await _alice.PostAsync($"/api/files/jobs/{started.Id}/cancel", null, Token))
            Assert.AreEqual(HttpStatusCode.NoContent, cancelled.StatusCode);

        var job = await FinishedAsync(_alice, started.Id);
        Assert.AreEqual(FileJobState.Cancelled, job.State);
        Assert.IsTrue(File.Exists(_library.FullPath("RFile2")));
    }

    [TestMethod]
    public async Task MovingAndCopyingNeedTheirRights()
    {
        using (var refused = await _carol.PostAsJsonAsync("/api/files/jobs", new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "ChildDir"), Json, Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        using (var listed = await _carol.GetAsync("/api/files/jobs", Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, listed.StatusCode);
        using (var empty = await _alice.PostAsJsonAsync("/api/files/jobs", new FileJobRequest(FileJobKind.Copy, [], "ChildDir"), Json, Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [TestMethod]
    public async Task TheCheckTellsWhoWouldGainAccess()
    {
        using var response = await _alice.PostAsJsonAsync("/api/files/jobs/check", new MoveCheckRequest(["RepeatedDir/ChildDir", "RFile1.txt", "SpecialDir"], ""), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        var check = (await response.Content.ReadFromJsonAsync<MoveCheck>(Json, Token))!;

        var gain = check.Gains.Single();
        Assert.AreEqual("RepeatedDir/ChildDir", gain.Folder);
        Assert.AreEqual("Visitors", gain.Who);
        Assert.IsTrue(gain.IsGroup);
        Assert.AreEqual(FolderLevel.Hidden, gain.Before);
    }

    [TestMethod]
    public async Task KeepingAccessPinsWhatTheFolderHad()
    {
        var job = await FinishedAsync(_alice, (await StartAsync(_alice, new FileJobRequest(FileJobKind.Move, ["RepeatedDir/ChildDir"], "", ConflictChoice.KeepBoth, KeepAccess: true))).Id);

        Assert.AreEqual(FileOperationStatus.Done, job.Results.Single().Status, job.Results.Single().Message);
        Assert.AreEqual("ChildDir (2)", job.Results.Single().NewPath);
        var rules = await _rules.GetAsync(_tenant, Token);
        Assert.IsTrue(rules.Any(r => r.Path == "ChildDir (2)" && r.Level == FolderLevel.Hidden), string.Join(", ", rules.Select(r => $"{r.Path}={r.Level}")));
    }

    private async Task<FileJobView> StartAsync(HttpClient client, FileJobRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/files/jobs", request, Json, Token);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileJobView>(Json, Token))!;
    }

    private Task<FileJobView> FinishedAsync(HttpClient client, int id) =>
        WaitForAsync(client, id, j => j.State is FileJobState.Completed or FileJobState.Cancelled);

    private async Task<FileJobView> WaitForAsync(HttpClient client, int id, Func<FileJobView, bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await client.GetFromJsonAsync<FileJobView>($"/api/files/jobs/{id}", Json, Token))!;
            if (condition(job))
                return job;
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Job {id} is {job.State}");
            await Task.Delay(20, Token);
        }
    }

    private async Task RuleAsync(FolderPrincipalRef principal, string path, FolderLevel level) =>
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, path, principal, level, null, Token)).Status);
}
