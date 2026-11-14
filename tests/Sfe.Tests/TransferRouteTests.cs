using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Transfers between the library and a connection started through <c>api/files/jobs/transfer</c>: who may, the
/// places refused, then the job followed, answered and cancelled through the file jobs' own routes.
/// </summary>
[TestClass]
public sealed class TransferRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private FakeAccount _account = null!;
    private int _adminId;
    private int _bobId;
    private int _id;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(_provider))
            );
        _admin = await ApiClients.SetUpAsync(_factory, Cancel);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Cancel))!.Id;
        _adminId = (await accounts.GetServerAdminIdsAsync(Cancel)).Single();
        var bob = await accounts.CreateUserAsync(
            new("bob", Password: "bob-password-1", GroupIds: [users]),
            null,
            Cancel
        );
        _bobId = bob.Value!.Id;
        _account = _provider.Account("acct").WithFolder("Photos/Trips").WithFile("Photos/a.txt", "text a");
        _id = await CreateConnectionAsync(_adminId);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AnotherAccountsConnectionIsNotFoundAndOneOnlyReadIsNotWritten()
    {
        var bobs = await CreateConnectionAsync(_bobId);
        using var other = await PostAsync(Copy(null, ["RFile1.txt"], bobs, ""));
        using var otherSource = await PostAsync(Copy(bobs, ["a.txt"], null, "ChildDir"));
        var connections = _factory.Services.GetRequiredService<ConnectionService>();
        await connections.UpdateAsync(_id, new ConnectionChange(ReadOnly: true), _adminId, Cancel);
        using var readOnly = await PostAsync(Copy(null, ["RFile1.txt"], _id, ""));
        using var moveOut = await PostAsync(Copy(_id, ["a.txt"], null, "ChildDir") with { Kind = "move" });
        using var copyOut = await PostAsync(Copy(_id, ["a.txt"], null, "ChildDir"));

        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, otherSource.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, moveOut.StatusCode, "A move takes its source away");
        Assert.AreEqual(HttpStatusCode.Accepted, copyOut.StatusCode);
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("upload", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TheSamePlaceOnBothSidesAndOtherWordsAreRefused()
    {
        using var library = await PostAsync(Copy(null, ["RFile1.txt"], null, "ChildDir"));
        using var same = await PostAsync(Copy(_id, ["a.txt"], _id, "Trips"));
        using var kind = await PostAsync(Copy(null, ["RFile1.txt"], _id, "") with { Kind = "link" });
        using var none = await PostAsync(Copy(null, [], _id, ""));

        foreach (var answer in new[] { library, same, kind, none })
            Assert.AreEqual(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [TestMethod]
    public async Task AStartedTransferIsFollowedThroughTheJobRoutes()
    {
        using var started = await PostAsync(Copy(null, ["RFile1.txt", "ChildDir"], _id, "Trips"));
        Assert.AreEqual(HttpStatusCode.Accepted, started.StatusCode, await started.Content.ReadAsStringAsync(Cancel));
        var view = (await started.Content.ReadFromJsonAsync<FileJobView>(Json, Cancel))!;
        var job = await FinishedAsync(view.Id);

        StringAssert.EndsWith(started.Headers.Location!.ToString(), $"files/jobs/{view.Id}");
        Assert.AreEqual(((int?)null, (int?)_id), (view.FromConnection, view.ToConnection));
        Assert.AreEqual(FileJobKind.Copy, job.Kind);
        Assert.IsTrue(job.Results.All(r => r.Status == FileOperationStatus.Done), job.Results[0].Message);
        Assert.IsTrue(_account.Exists("Photos/Trips/RFile1.txt"));
        Assert.IsTrue(_account.Exists("Photos/Trips/ChildDir/AFile2"));
        var listed = (await _admin.GetFromJsonAsync<List<FileJobView>>($"{V1}/files/jobs", Json, Cancel))!;
        Assert.AreEqual(_id, listed.Single(j => j.Id == view.Id).ToConnection);
    }

    [TestMethod]
    public async Task ATransfersQuestionIsAnsweredAndAnotherJobCancelled()
    {
        _account.WithFile("Photos/RFile1.txt", "old");

        var answered = await StartAsync(Copy(null, ["RFile1.txt"], _id, ""));
        var waiting = await WaitForAsync(answered.Id, j => j.State == FileJobState.Waiting);
        using var answer = await _admin.PostAsJsonAsync(
            $"{V1}/files/jobs/{answered.Id}/answer",
            new JobAnswer(ConflictChoice.KeepBoth),
            Json,
            Cancel
        );
        var done = await FinishedAsync(answered.Id);
        var cancelled = await StartAsync(Copy(null, ["RFile1.txt"], _id, ""));
        await WaitForAsync(cancelled.Id, j => j.State == FileJobState.Waiting);
        using var cancel = await _admin.PostAsync($"{V1}/files/jobs/{cancelled.Id}/cancel", null, Cancel);
        var stopped = await FinishedAsync(cancelled.Id);

        Assert.AreEqual("RFile1.txt", waiting.Conflict!.Existing.Name);
        Assert.AreEqual(HttpStatusCode.NoContent, answer.StatusCode);
        Assert.AreEqual("RFile1 (2).txt", done.Results.Single().NewPath);
        Assert.AreEqual(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.AreEqual(FileJobState.Cancelled, stopped.State);
        Assert.AreEqual("old", _account.TextOf("Photos/RFile1.txt"));
    }

    [TestMethod]
    public async Task OnlyItsOwnerRetriesATransferOnceItEnded()
    {
        _account.WithFile("Photos/RFile1.txt", "old");
        var job = await StartAsync(Copy(null, ["RFile1.txt", "ChildDir"], _id, ""));
        await WaitForAsync(job.Id, j => j.State == FileJobState.Waiting);
        using var running = await RetryAsync(_admin, job.Id);
        await LetUsersCopyAsync();
        using var bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Cancel);
        using var others = await RetryAsync(bob, job.Id);
        using var unknown = await RetryAsync(_admin, job.Id + 100);
        using var cancel = await _admin.PostAsync($"{V1}/files/jobs/{job.Id}/cancel", null, Cancel);
        await FinishedAsync(job.Id);
        using var retried = await RetryAsync(_admin, job.Id);
        var again = (await retried.Content.ReadFromJsonAsync<FileJobView>(Json, Cancel))!;
        await WaitForAsync(again.Id, j => j.State == FileJobState.Waiting);
        using var answer = await _admin.PostAsJsonAsync(
            $"{V1}/files/jobs/{again.Id}/answer",
            new JobAnswer(ConflictChoice.Skip),
            Json,
            Cancel
        );
        var done = await FinishedAsync(again.Id);
        using var twice = await RetryAsync(_admin, job.Id);
        using var nothing = await RetryAsync(_admin, again.Id);

        Assert.AreEqual(HttpStatusCode.Conflict, running.StatusCode);
        StringAssert.Contains(await running.Content.ReadAsStringAsync(Cancel), TransferEndpoints.JobRunningType);
        Assert.AreEqual(HttpStatusCode.NotFound, others.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, retried.StatusCode);
        Assert.AreNotEqual(job.Id, again.Id, "A new job");
        StringAssert.EndsWith(retried.Headers.Location!.ToString(), $"files/jobs/{again.Id}");
        Assert.AreEqual((2, (int?)_id), (again.Total, again.ToConnection));
        Assert.AreEqual(FileOperationStatus.Done, done.Results.Single(r => r.Path == "ChildDir").Status);
        Assert.IsTrue(_account.Exists("Photos/ChildDir/AFile2"));
        foreach (var refused in new[] { twice, nothing })
        {
            Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode);
            StringAssert.Contains(
                await refused.Content.ReadAsStringAsync(Cancel),
                TransferEndpoints.NothingToRetryType
            );
        }
    }

    /// <summary>Bob may copy (and use connections): what keeps him from another's job is its owner.</summary>
    private async Task LetUsersCopyAsync()
    {
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Cancel))!;
        var input = new GroupInput(users.Name, Rights: [.. users.Rights, Rights.CopyFiles], MemberIds: users.MemberIds);
        var updated = await accounts.UpdateGroupAsync(tenant, users.Id, input, cancellationToken: Cancel);
        Assert.AreEqual(AccessStatus.Ok, updated.Status);
    }

    private Task<HttpResponseMessage> RetryAsync(HttpClient client, int id) =>
        client.PostAsync($"{V1}/files/jobs/{id}/retry", null, Cancel);

    private static TransferRequest Copy(int? from, IReadOnlyList<string> paths, int? to, string target) =>
        new("copy", new TransferSide(from, paths), new TransferTarget(to, target));

    private async Task<int> CreateConnectionAsync(int ownerId)
    {
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(
                new(
                    FakeProvider.FakeKind,
                    $"Files {ownerId}",
                    ConnectionLevel.Account,
                    OwnerId: ownerId,
                    RootPath: "Photos"
                ),
                new ConnectionGrant("acct", "acct@fake", "id:acct"),
                ownerId,
                Cancel
            );
        return made.Value!.Id;
    }

    private Task<HttpResponseMessage> PostAsync(TransferRequest request) =>
        _admin.PostAsJsonAsync($"{V1}/files/jobs/transfer", request, Json, Cancel);

    private async Task<FileJobView> StartAsync(TransferRequest request)
    {
        using var response = await PostAsync(request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<FileJobView>(Json, Cancel))!;
    }

    private Task<FileJobView> FinishedAsync(int id) =>
        WaitForAsync(id, j => j.State is FileJobState.Completed or FileJobState.Cancelled);

    private async Task<FileJobView> WaitForAsync(int id, Func<FileJobView, bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await _admin.GetFromJsonAsync<FileJobView>($"{V1}/files/jobs/{id}", Json, Cancel))!;
            if (condition(job))
                return job;
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Job {id} is {job.State}");
            await Task.Delay(20, Cancel);
        }
    }
}
