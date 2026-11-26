using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Tasks;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The change feed's `task` event (agents/docs/features/notifications.md): a run that ended told to the account that
/// started it, a failed one to those who see the tasks, and what ended while a page was away when its feed opens
/// again. Alice is in Users (no task rights); the administrator sees and runs the tasks.
/// </summary>
[TestClass]
public sealed class TaskEndingFeedTests
{
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private TaskHistory _history = null!;

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
        await accounts.CreateUserAsync(
            new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]),
            null,
            Token
        );
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        var host = await _factory.Services.GetRequiredService<TenantRegistry>().GetAsync(tenant, Token);
        _history = host!.Services.GetRequiredService<TaskHistory>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ARunStartedByHandIsToldToTheAccountThatStartedIt()
    {
        using var adminFeed = await OpenAsync(_admin);
        using var aliceFeed = await OpenAsync(_alice);

        using (var started = await _admin.PostAsync($"{V1}/tasks/trashPurge/start", null, Token))
            Assert.AreEqual(
                HttpStatusCode.Accepted,
                started.StatusCode,
                await started.Content.ReadAsStringAsync(Token)
            );

        var told = await ServerEvents.NextAsync<TaskEnded>(adminFeed, "task", Token);
        Assert.AreEqual(BackgroundTaskKind.TrashPurge, told.Task);
        Assert.AreEqual(TaskOutcome.Completed, told.Outcome);
        Assert.IsTrue(told.Mine);
        Assert.IsGreaterThan(0, told.Id);
        Assert.IsNotNull(told.Finished);
        // Alice neither started it nor sees the tasks: the folder created after it comes first.
        await CreateFolderAsync("after");
        CollectionAssert.DoesNotContain(await ServerEvents.NamesBeforeAsync(aliceFeed, "changed", Token), "task");
    }

    [TestMethod]
    public async Task AFailedRunIsToldToThoseWhoSeeTheTasks()
    {
        using var adminFeed = await OpenAsync(_admin);
        using var aliceFeed = await OpenAsync(_alice);

        // Nobody started these: a run that went well and a transfer aren't told; the failure is, to the administrator.
        await EndAsync(BackgroundTaskKind.Scan, TaskOutcome.Completed);
        await EndAsync(BackgroundTaskKind.Transfer, TaskOutcome.Failed);
        await EndAsync(BackgroundTaskKind.SimilarVideos, TaskOutcome.Failed, "ffmpeg is missing");

        var told = await ServerEvents.NextAsync<TaskEnded>(adminFeed, "task", Token);
        Assert.AreEqual(BackgroundTaskKind.SimilarVideos, told.Task);
        Assert.AreEqual("ffmpeg is missing", told.Summary);
        Assert.IsFalse(told.Mine);
        await CreateFolderAsync("after");
        CollectionAssert.DoesNotContain(await ServerEvents.NamesBeforeAsync(aliceFeed, "changed", Token), "task");
    }

    [TestMethod]
    public async Task AFeedOpenedAgainHearsWhatEndedSinceTheTimeItGives()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await EndAsync(BackgroundTaskKind.Trickplay, TaskOutcome.Failed);

        using (var since = await OpenAsync(_admin, before))
            Assert.AreEqual(
                BackgroundTaskKind.Trickplay,
                (await ServerEvents.NextAsync<TaskEnded>(since, "task", Token)).Task
            );

        // Without a time, or with a later one: only what comes next.
        foreach (var feed in new[] { await OpenAsync(_admin), await OpenAsync(_admin, DateTimeOffset.UtcNow) })
        {
            using (feed)
            {
                await CreateFolderAsync($"after-{Guid.NewGuid():N}");
                CollectionAssert.DoesNotContain(await ServerEvents.NamesBeforeAsync(feed, "changed", Token), "task");
            }
        }
    }

    private async Task EndAsync(BackgroundTaskKind task, TaskOutcome outcome, string? summary = null) =>
        await _history.Begin(task, null, TaskTrigger.Schedule).CompleteAsync(outcome, summary);

    /// <summary>A feed of <paramref name="client"/>, once it listens (its first event read).</summary>
    private async Task<StreamReader> OpenAsync(HttpClient client, DateTimeOffset? since = null)
    {
        var query = since is { } time
            ? $"?since={Uri.EscapeDataString(time.ToString("O", CultureInfo.InvariantCulture))}"
            : "";
        var feed = await client.GetAsync($"{V1}/files/changes{query}", HttpCompletionOption.ResponseHeadersRead, Token);
        Assert.AreEqual(HttpStatusCode.OK, feed.StatusCode);
        var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        Assert.AreEqual("keepAlive", (await ServerEvents.NextAsync(reader, Token)).Name);
        return reader;
    }

    private async Task CreateFolderAsync(string name)
    {
        using var response = await _admin.PostAsJsonAsync(
            $"{V1}/files/folders",
            new CreateFolderRequest("", name),
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }
}
