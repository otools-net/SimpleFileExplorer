using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Library.Watching;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The change feed and new files through HTTP. Alice is in Users (who may browse, not create files) with SpecialDir
/// hidden from them; the administrator changes folders.
/// </summary>
[TestClass]
public sealed class ChangeFeedRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private int _tenant;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = _tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        await accounts.CreateUserAsync(
            new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]),
            null,
            Token
        );
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(
            AccessStatus.Ok,
            (
                await rules.SetAsync(
                    tenant,
                    "SpecialDir",
                    FolderPrincipalRef.Group(users.Id),
                    FolderLevel.Hidden,
                    null,
                    Token
                )
            ).Status
        );
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
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
    public async Task AFolderChangedElsewhereIsToldToThoseWhoSeeIt()
    {
        using var feed = await _alice.GetAsync($"{V1}/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);
        Assert.AreEqual(HttpStatusCode.OK, feed.StatusCode);
        Assert.AreEqual("text/event-stream", feed.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        Assert.AreEqual("retry: 5000", await reader.ReadLineAsync(Token));

        // Hidden from Alice: not told. Then a folder she sees, from a tab of the administrator's.
        await CreateFolderAsync("SpecialDir", "hidden news", null);
        await CreateFolderAsync("ChildDir", "news", "tab-1");

        var change = await NextChangeAsync(reader);
        Assert.AreEqual("ChildDir", change.Path);
        Assert.AreEqual("tab-1", change.Origin);
    }

    [TestMethod]
    public async Task TheFeedIsNeitherCachedNorHeldBackByAProxy()
    {
        using var feed = await _alice.GetAsync($"{V1}/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);

        Assert.IsTrue(feed.Headers.CacheControl?.NoStore);
        Assert.AreEqual("no", feed.Headers.GetValues("X-Accel-Buffering").Single());
    }

    [TestMethod]
    [DoNotParallelize] // Feeds are counted per user id for the whole process: other tests' users have the same ids.
    public async Task ACallerKeepsAtMostTwelveFeedsOpen()
    {
        var feeds = new List<HttpResponseMessage>();
        try
        {
            for (var i = 0; i < ChangeEndpoints.MaxStreamsPerCaller; i++)
            {
                var feed = await OpenFeedAsync(_alice);
                feeds.Add(feed);
                Assert.AreEqual(HttpStatusCode.OK, feed.StatusCode, $"feed {i + 1}");
            }

            using (var refused = await OpenFeedAsync(_alice))
            {
                Assert.AreEqual(HttpStatusCode.TooManyRequests, refused.StatusCode);
            }
            // Another caller is counted apart.
            using (var other = await OpenFeedAsync(_admin))
            {
                Assert.AreEqual(HttpStatusCode.OK, other.StatusCode);
            }

            // A feed closed makes room again (once the server sees it closed).
            feeds[0].Dispose();
            feeds.RemoveAt(0);
            var reopened = await OpenOnceRoomAsync(_alice);
            feeds.Add(reopened);
            Assert.AreEqual(HttpStatusCode.OK, reopened.StatusCode);
        }
        finally
        {
            feeds.ForEach(f => f.Dispose());
        }
    }

    [TestMethod]
    public void QuietFeedsSendAPingThePageSees()
    {
        // A named event with data reaches the page's listener (a comment, or an event without data, would not): the
        // client takes a feed silent for a minute as a server that doesn't answer.
        CollectionAssert.AreEqual(new[] { "event: ping", "data: {}", "", "" }, ChangeEndpoints.PingEvent.Split('\n'));
        Assert.IsLessThan(TimeSpan.FromSeconds(30), ChangeEndpoints.KeepAlive, "Well within the client's minute");
    }

    [TestMethod]
    public async Task KeepAliveIsToldFirstThenWhenItChanges()
    {
        using var feed = await _alice.GetAsync($"{V1}/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);
        using var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        Assert.AreEqual(KeepAliveState.None, (await NextEventAsync<KeepAliveSignal>(reader, "keepAlive")).State);

        // Turned on for the space: the settings change pings at once, and the feed tells it (no folder named).
        using (
            var saved = await _admin.PatchAsJsonAsync(
                $"{V1}/settings?level=tenant",
                new SettingChanges(new Dictionary<string, JsonNode?> { ["keepAlive.mode"] = "always" }),
                Json,
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Token));
        }

        Assert.AreEqual(KeepAliveState.Ok, (await NextEventAsync<KeepAliveSignal>(reader, "keepAlive")).State);
    }

    [TestMethod]
    public async Task TrashChangesAreToldToThoseWhoMayDelete()
    {
        using var adminFeed = await _admin.GetAsync(
            $"{V1}/files/changes",
            HttpCompletionOption.ResponseHeadersRead,
            Token
        );
        using var admin = new StreamReader(await adminFeed.Content.ReadAsStreamAsync(Token));
        using var aliceFeed = await _alice.GetAsync(
            $"{V1}/files/changes",
            HttpCompletionOption.ResponseHeadersRead,
            Token
        );
        using var alice = new StreamReader(await aliceFeed.Content.ReadAsStreamAsync(Token));
        // The first event comes once each feed listens.
        await NextEventAsync<KeepAliveSignal>(admin, "keepAlive");
        await NextEventAsync<KeepAliveSignal>(alice, "keepAlive");

        using (
            var deleted = await _admin.PostAsJsonAsync(
                $"{V1}/files/delete",
                new DeleteRequest(["ChildDir/AFile1.txt"]),
                Json,
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode, await deleted.Content.ReadAsStringAsync(Token));
        }

        await CreateFolderAsync("RepeatedDir", "after", null);

        Assert.IsNotNull(await NextEventAsync<JsonObject>(admin, "trash"));
        // Alice may only browse: up to the folder created after the delete, no `trash`.
        var seen = new List<string>();
        while (true)
        {
            var (name, data) = await NextAnyEventAsync(alice);
            seen.Add(name);
            if (name == "changed" && JsonSerializer.Deserialize<FolderChange>(data, Json)!.Path == "RepeatedDir")
                break;
        }
        CollectionAssert.DoesNotContain(seen, "trash");
    }

    [TestMethod]
    public async Task ItemsBeingWrittenAreToldWithTheirProgressInFoldersTheCallerSees()
    {
        using var feed = await OpenFeedAsync(_alice);
        using var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        await NextEventAsync<KeepAliveSignal>(reader, "keepAlive");
        var host = await _factory.Services.GetRequiredService<TenantRegistry>().GetAsync(_tenant, Token);
        var writing = host!.Services.GetRequiredService<ItemsBeingWritten>();

        var hidden = writing.Begin(_library.FullPath("SpecialDir/secret.mp4.sfe-partial"), "secret.mp4", 100);
        var seen = writing.Begin(_library.FullPath("ChildDir/clip.mp4.sfe-partial"), "clip.mp4", 100);
        seen.Add(40);
        var told = await WritingUntilAsync(reader, w => w.Items.Any(i => i.BytesDone == 40));
        hidden.Dispose();
        seen.Dispose();
        var ended = await WritingUntilAsync(reader, w => w.Items.Count == 0);

        Assert.AreEqual(new ItemWriting("clip.mp4.sfe-partial", "clip.mp4", 40, 100), told[^1].Items.Single());
        // SpecialDir is hidden from Alice: never told.
        CollectionAssert.AreEquivalent(
            new[] { "ChildDir" },
            told.Concat(ended).Select(w => w.Path).Distinct().ToList()
        );
    }

    [TestMethod]
    public async Task AFolderChangedOutsideSfeIsToldOnceATabWatchesIt()
    {
        using var feed = await _alice.GetAsync(
            $"{V1}/files/changes?tab=watching-tab",
            HttpCompletionOption.ResponseHeadersRead,
            Token
        );
        using var reader = new StreamReader(await feed.Content.ReadAsStreamAsync(Token));
        await NextEventAsync<KeepAliveSignal>(reader, "keepAlive");

        Assert.IsTrue(await WatchAsync(_alice, "watching-tab", "ChildDir"));
        using var writing = new CancellationTokenSource();
        var writer = WriteFilesAsync("ChildDir", writing.Token);
        var change = await NextChangeAsync(reader);
        await writing.CancelAsync();
        await writer;

        Assert.AreEqual(new FolderChange("ChildDir", null, Outside: true), change);
    }

    [TestMethod]
    public async Task OnlyTheTabsOwnFeedFollowsAFolderItSees()
    {
        using var feed = await _alice.GetAsync(
            $"{V1}/files/changes?tab=alice-tab",
            HttpCompletionOption.ResponseHeadersRead,
            Token
        );
        var watchers = (await _factory.Services.GetRequiredService<TenantRegistry>().GetAsync(_tenant, Token))!
            .Services.GetRequiredService<FolderWatchers>();

        // Tab names are unique per test: feeds are kept by user id and tab for the whole process.
        // Hidden from Alice, missing, leaving the library, another tab, another caller with her tab's name: refused.
        Assert.IsFalse(await WatchAsync(_alice, "alice-tab", "SpecialDir"));
        Assert.IsFalse(await WatchAsync(_alice, "alice-tab", "NoSuchFolder"));
        Assert.IsFalse(await WatchAsync(_alice, "alice-tab", "../Outside"));
        Assert.IsFalse(await WatchAsync(_alice, "other-tab", "ChildDir"));
        Assert.IsFalse(await WatchAsync(_admin, "alice-tab", "ChildDir"));
        Assert.IsEmpty(watchers.Folders);

        Assert.IsTrue(await WatchAsync(_alice, "alice-tab", ""));
        Assert.IsTrue(await WatchAsync(_alice, "alice-tab", "ChildDir"));
        CollectionAssert.AreEqual(new[] { "ChildDir" }, watchers.Folders.ToList(), "the top let go");
        Assert.IsFalse(await WatchAsync(_alice, "alice-tab", null));
        Assert.IsEmpty(watchers.Folders);

        // The feed closed lets its folder go.
        Assert.IsTrue(await WatchAsync(_alice, "alice-tab", "ChildDir"));
        feed.Dispose();
        for (var i = 0; i < 50 && watchers.Folders.Count > 0; i++)
            await Task.Delay(100, Token);
        Assert.IsEmpty(watchers.Folders);
    }

    [TestMethod]
    public void TheTrashEventReachesThePage()
    {
        CollectionAssert.AreEqual(new[] { "event: trash", "data: {}", "", "" }, ChangeEndpoints.TrashEvent.Split('\n'));
    }

    [TestMethod]
    public async Task NewFilesNeedTheirRight()
    {
        using var refused = await _alice.PostAsJsonAsync(
            $"{V1}/files/new-file",
            new CreateFileRequest("ChildDir", "notes.txt"),
            Json,
            Token
        );
        using var created = await _admin.PostAsJsonAsync(
            $"{V1}/files/new-file",
            new CreateFileRequest("ChildDir", "notes.txt"),
            Json,
            Token
        );
        using var again = await _admin.PostAsJsonAsync(
            $"{V1}/files/new-file",
            new CreateFileRequest("ChildDir", "notes.txt"),
            Json,
            Token
        );

        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode, await created.Content.ReadAsStringAsync(Token));
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);
        Assert.IsTrue(File.Exists(_library.FullPath("ChildDir/notes.txt")));
    }

    private async Task CreateFolderAsync(string path, string name, string? tab)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{V1}/files/folders")
        {
            Content = JsonContent.Create(new CreateFolderRequest(path, name), options: Json),
        };
        if (tab is not null)
            request.Headers.Add(ChangeEndpoints.TabHeader, tab);
        using var response = await _admin.SendAsync(request, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    private async Task<bool> WatchAsync(HttpClient client, string tab, string? path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{V1}/files/changes/watch")
        {
            Content = JsonContent.Create(new FolderWatchRequest(path), options: Json),
        };
        request.Headers.Add(ChangeEndpoints.TabHeader, tab);
        using var response = await client.SendAsync(request, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FolderWatchAnswer>(Json, Token))!.Watching;
    }

    /// <summary>
    /// A new file in <paramref name="folder"/> every 300 ms, as another program would, until stopped (the watcher
    /// starts on another thread: a file written before it is not seen).
    /// </summary>
    private async Task WriteFilesAsync(string folder, CancellationToken stop)
    {
        for (var i = 0; !stop.IsCancellationRequested && i < 30; i++)
        {
            _library.AddFile($"{folder}/outside-{i}.txt", 10 + i);
            try
            {
                await Task.Delay(300, stop);
            }
            catch (OperationCanceledException) { }
        }
    }

    private Task<HttpResponseMessage> OpenFeedAsync(HttpClient client) =>
        client.GetAsync($"{V1}/files/changes", HttpCompletionOption.ResponseHeadersRead, Token);

    /// <summary>A feed opened as soon as the server has let one go (it sees a closed feed after a moment).</summary>
    private async Task<HttpResponseMessage> OpenOnceRoomAsync(HttpClient client)
    {
        for (var attempt = 0; ; attempt++)
        {
            var feed = await OpenFeedAsync(client);
            if (feed.StatusCode != HttpStatusCode.TooManyRequests || attempt == 50)
                return feed;
            feed.Dispose();
            await Task.Delay(100, Token);
        }
    }

    private Task<FolderChange> NextChangeAsync(StreamReader reader) => NextEventAsync<FolderChange>(reader, "changed");

    /// <summary>The data of the next event named <paramref name="name"/>, the others skipped.</summary>
    private async Task<T> NextEventAsync<T>(StreamReader reader, string name)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? current = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
                current = line["event: ".Length..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && current == name)
                return JsonSerializer.Deserialize<T>(line["data: ".Length..], Json)!;
        }
        throw new AssertFailedException($"The feed ended without a {name} event.");
    }

    /// <summary>The `writing` events up to the first that <paramref name="until"/> accepts (included).</summary>
    private async Task<List<FolderWriting>> WritingUntilAsync(StreamReader reader, Func<FolderWriting, bool> until)
    {
        var events = new List<FolderWriting>();
        do
        {
            events.Add(await NextEventAsync<FolderWriting>(reader, "writing"));
        } while (!until(events[^1]));
        return events;
    }

    /// <summary>The name and data of the next event, whatever it is.</summary>
    private async Task<(string Name, string Data)> NextAnyEventAsync(StreamReader reader)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? current = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
                current = line["event: ".Length..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && current is not null)
                return (current, line["data: ".Length..]);
        }
        throw new AssertFailedException("The feed ended.");
    }
}
