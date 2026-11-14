using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Tests.Infrastructure;
using static Sfe.Tests.Infrastructure.TokenClients;

namespace Sfe.Tests;

/// <summary>
/// File jobs started with an access token: each item is checked with the token's rights over the account's, as the
/// request was, and the items left once the token is revoked are not done; a job started from a session is unchanged.
/// </summary>
[TestClass]
public sealed class AccessTokenJobTests
{
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
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ATokenWithoutTheRightToDeleteReplacesNothing()
    {
        var factory = Factory();
        var (_, adminId) = await AdminAsync(factory);
        _library.AddFile("ChildDir/RFile1.txt", 5);
        string[] copying = [Rights.Browse, Rights.ViewFiles, Rights.CopyFiles];
        var copier = await CreateAsync(factory, adminId, new("Copier", Rights: copying), Token);
        var replacer = await CreateAsync(
            factory,
            adminId,
            new("Replacer", Rights: [.. copying, Rights.DeleteFiles]),
            Token
        );
        var replace = new FileJobRequest(FileJobKind.Copy, ["RFile1.txt"], "ChildDir", ConflictChoice.Replace);

        // Not allowed to replace, the job asks instead (as for a session without the right).
        var copierClient = Program(factory, copier.Secret);
        var started = await StartAsync(copierClient, replace);
        var refused = await WaitForAsync(copierClient, started.Id, j => j.State == FileJobState.Waiting || IsEnded(j));
        using (var cancelled = await copierClient.PostAsync($"{V1}/files/jobs/{started.Id}/cancel", null, Token))
            Assert.AreEqual(HttpStatusCode.NoContent, cancelled.StatusCode);
        var keptSize = new FileInfo(_library.FullPath("ChildDir/RFile1.txt")).Length;
        var replaced = await RunAsync(Program(factory, replacer.Secret), replace);

        Assert.IsFalse(refused.Results.Any(r => r.Status == FileOperationStatus.Done));
        Assert.AreEqual(5, keptSize, "The file in the way is still there");
        Assert.AreEqual(FileOperationStatus.Done, replaced.Results.Single().Status);
        Assert.AreEqual(10, new FileInfo(_library.FullPath("ChildDir/RFile1.txt")).Length);
    }

    [TestMethod]
    public async Task RevokingTheTokenStopsTheItemsLeftButNotASessionsJob()
    {
        var factory = Factory();
        var (admin, adminId) = await AdminAsync(factory);
        _library.AddFile("ChildDir/RFile1.txt", 5);
        var token = await CreateAsync(factory, adminId, new("Mover"), Token);
        var program = Program(factory, token.Secret);
        var request = new FileJobRequest(FileJobKind.Copy, ["RFile1.txt", "RFile2"], "ChildDir");

        var started = await StartAsync(program, request);
        await WaitForAsync(program, started.Id, j => j.State == FileJobState.Waiting);
        using (var revoking = await admin.DeleteAsync($"{V1}/me/tokens/{token.Token.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NoContent, revoking.StatusCode);
        await AnswerAsync(admin, started.Id);
        var stopped = await WaitForAsync(admin, started.Id, IsEnded);

        var bySession = await StartAsync(admin, request);
        await WaitForAsync(admin, bySession.Id, j => j.State == FileJobState.Waiting);
        await AnswerAsync(admin, bySession.Id);
        var done = await WaitForAsync(admin, bySession.Id, IsEnded);

        var second = stopped.Results.Single(r => r.Path == "RFile2");
        Assert.AreEqual(FileOperationStatus.Forbidden, second.Status);
        Assert.AreEqual(JobToken.StoppedProblem, second.Message);
        Assert.IsTrue(done.Results.All(r => r.Status == FileOperationStatus.Done), "A session's job goes on");
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/RFile2")));
    }

    private static bool IsEnded(FileJobView job) => job.State is FileJobState.Completed or FileJobState.Cancelled;

    private async Task AnswerAsync(HttpClient client, int jobId)
    {
        using var answered = await client.PostAsJsonAsync(
            $"{V1}/files/jobs/{jobId}/answer",
            new JobAnswer(ConflictChoice.KeepBoth),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.NoContent, answered.StatusCode);
    }

    private async Task<FileJobView> RunAsync(HttpClient client, FileJobRequest request)
    {
        var started = await StartAsync(client, request);
        return await WaitForAsync(client, started.Id, IsEnded);
    }

    private async Task<FileJobView> StartAsync(HttpClient client, FileJobRequest request)
    {
        using var response = await client.PostAsJsonAsync($"{V1}/files/jobs", request, Json, Token);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileJobView>(Json, Token))!;
    }

    private async Task<FileJobView> WaitForAsync(HttpClient client, int id, Func<FileJobView, bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await client.GetFromJsonAsync<FileJobView>($"{V1}/files/jobs/{id}", Json, Token))!;
            if (condition(job))
                return job;
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Job {id} is {job.State}");
            await Task.Delay(20, Token);
        }
    }

    private HttpClient Program(WebApplicationFactory<Program> factory, string token)
    {
        var client = Bearer(factory, token);
        _disposables.Add(client);
        return client;
    }

    private WebApplicationFactory<Program> Factory()
    {
        var factory = ApiClients.CreateFactory(_library);
        _disposables.Add(factory);
        return factory;
    }

    private async Task<(HttpClient Client, int UserId)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = await ApiClients.SetUpAsync(factory, Token);
        _disposables.Add(client);
        var session = await GetAsync<SessionInfo>(client, $"{V1}/session", Token);
        return (client, session.User!.Id);
    }
}
