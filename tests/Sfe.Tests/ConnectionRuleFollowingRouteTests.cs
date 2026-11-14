using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The folder rules of a connection following its folders (<see cref="ConnectionMoves"/>, on
/// <see cref="RuledConnection"/>): a rename or move through the app takes them along with the folder's id, a move
/// letting more people in is asked about first (409 <c>access-gains</c>) and keeps their access when asked, another
/// library's rules follow too, a folder moved out of the connection takes its rules away, a deleted one keeps them.
/// </summary>
[TestClass]
public sealed class ConnectionRuleFollowingRouteTests
{
    private RuledConnection _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    private HttpClient Bob => _world.Bob;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _world = await RuledConnection.CreateAsync(Cancel);
        _world.Account.WithFile("Docs/Notes/n.txt", "notes").WithFile("Docs/Plans/p.txt", "plans");
        await _world.SetRulesAsync(
            Cancel,
            _world.Rule("Docs", FolderLevel.Write),
            _world.Rule("Docs/Notes", FolderLevel.Write)
        );
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task ARenamedFolderTakesItsRulesAndTheirDeepPathsWithItsId()
    {
        using var renamed = await PostAsync("files/rename", new RenameRequest("Docs", "Papers"));

        Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode, await renamed.Content.ReadAsStringAsync(Cancel));
        var paths = (await RulesAsync()).Select(r => r.Path).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "Papers", "Papers/Notes" }, paths);
        Assert.IsFalse(paths.Any(p => p.StartsWith("Docs", StringComparison.Ordinal)), string.Join(", ", paths));
        var folder = (await RulesAsync()).Single(r => r.Path == "Papers");
        Assert.AreEqual(_world.Account.Find("Papers")!.Id, folder.ItemId);
    }

    [TestMethod]
    public async Task AMoveLettingMorePeopleInIsAskedThenKeepsTheirAccessWhenConfirmed()
    {
        var viewers = await ViewersAsync();
        await _world.SetRulesAsync(Cancel, _world.Rule("Docs", FolderLevel.Hidden, viewers));
        var move = new ConnectionTransferRequest("move", ["Docs/Plans"], "Public");

        using var asked = await PostAsync("files/transfer", move);
        var problem = JsonDocument.Parse(await asked.Content.ReadAsStringAsync(Cancel)).RootElement;
        Assert.AreEqual(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.AreEqual(ConnectionMoves.AccessGainsType, problem.GetProperty("type").GetString());
        var gain = problem.GetProperty("gains").EnumerateArray().Single();
        Assert.AreEqual("Viewers", gain.GetProperty("who").GetString());
        Assert.AreEqual("hidden", gain.GetProperty("before").GetString());
        Assert.AreEqual("read", gain.GetProperty("after").GetString());
        Assert.IsTrue(_world.Account.Exists("Docs/Plans"), "nothing moved before the answer");

        var moved = await ResultsAsync(move with { KeepAccess = true });

        Assert.AreEqual(FileOperationStatus.Done, moved.Items.Single().Status);
        var pin = (await RulesAsync()).Single(r => r.Path == "Public/Plans");
        Assert.AreEqual((viewers, FolderLevel.Hidden), (pin.GroupId!.Value, pin.Level));
    }

    [TestMethod]
    public async Task AMoveConfirmedWithoutKeepingAccessAddsNoRule()
    {
        var viewers = await ViewersAsync();
        await _world.SetRulesAsync(Cancel, _world.Rule("Docs", FolderLevel.Hidden, viewers));

        var moved = await ResultsAsync(
            new ConnectionTransferRequest("move", ["Docs/Plans"], "Public", KeepAccess: false)
        );

        Assert.AreEqual(FileOperationStatus.Done, moved.Items.Single().Status);
        Assert.IsFalse((await RulesAsync()).Any(r => r.Path.StartsWith("Public", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AnotherLibrarysRulesFollowTheFolderWithoutBeingAsked()
    {
        await OtherLibraryRuleAsync("Docs/Notes");

        var moved = await ResultsAsync(new ConnectionTransferRequest("move", ["Docs/Notes"], "Public"));

        Assert.AreEqual(FileOperationStatus.Done, moved.Items.Single().Status);
        var rows = await AllRulesAsync();
        Assert.AreEqual("Public/Notes", rows.Single(r => r.TenantId != _world.TenantId).Path);
        Assert.IsTrue(rows.Any(r => r.TenantId == _world.TenantId && r.Path == "Public/Notes"));
        Assert.IsFalse(rows.Any(r => r.Path == "Docs/Notes"));
    }

    [TestMethod]
    public async Task RulesWhereTheFolderGoesStopTheMove()
    {
        await _world.SetRulesAsync(
            Cancel,
            _world.Rule("Docs", FolderLevel.Write),
            _world.Rule("Docs/Notes", FolderLevel.Write),
            _world.Rule("Gone", FolderLevel.Write)
        );

        using var renamed = await PostAsync("files/rename", new RenameRequest("Docs", "Gone"));

        Assert.AreEqual(HttpStatusCode.Conflict, renamed.StatusCode);
        Assert.AreEqual(FileEndpoints.FolderRulesType, await TypeOfAsync(renamed));
        Assert.IsNotNull(_world.Account.Find("Docs"), "the provider was not asked");
        Assert.IsTrue((await RulesAsync()).Any(r => r.Path == "Docs/Notes"));
    }

    [TestMethod]
    public async Task AFolderMovedOutOfTheConnectionTakesItsRulesAway()
    {
        await OtherLibraryRuleAsync("Docs/Notes");
        var request = new TransferRequest("move", new TransferSide(_world.Id, ["Docs"]), new TransferTarget(null, ""));

        using var started = await Bob.PostAsJsonAsync(
            "/api/files/jobs/transfer",
            request,
            RuledConnection.Json,
            Cancel
        );
        var job = await FinishedAsync(started);

        Assert.IsTrue(job.Results.All(r => r.Status == FileOperationStatus.Done), string.Join(", ", job.Results));
        Assert.IsNull(_world.Account.Find("Docs"));
        Assert.IsFalse((await AllRulesAsync()).Any(r => r.Path.StartsWith("Docs", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ADeletedFolderKeepsItsRules()
    {
        using var deleted = await PostAsync("files/delete", new DeleteRequest(["Docs"]));

        Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode);
        Assert.IsNull(_world.Account.Find("Docs"));
        var paths = (await RulesAsync()).Select(r => r.Path).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "Docs", "Docs/Notes" }, paths);
    }

    /// <summary>A group Viewers (bob in it) using the connection at Read.</summary>
    private async Task<int> ViewersAsync()
    {
        var viewers = await _world.GroupAsync("Viewers", [Rights.UseConnections, Rights.Browse], Cancel);
        await _world.ShareAsync(new() { [_world.WritersId] = FolderLevel.Write, [viewers] = FolderLevel.Read }, Cancel);
        return viewers;
    }

    /// <summary>A rule of another library at <paramref name="path"/>, as a server connection's is written.</summary>
    private async Task<int> OtherLibraryRuleAsync(string path)
    {
        var accounts = _world.Get<AccessService>();
        var other = (await accounts.CreateTenantAsync(new TenantInput("Other"), _world.Library.Root, Cancel)).Value!;
        var group = (
            await accounts.CreateGroupAsync(other.Id, new GroupInput("Others"), cancellationToken: Cancel)
        ).Value!;
        await using var db = await _world.Get<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Cancel);
        var rule = new ConnectionFolderRule
        {
            ConnectionId = _world.Id,
            TenantId = other.Id,
            Path = path,
            GroupId = group.Id,
            Level = FolderLevel.Hidden,
            Created = DateTime.UtcNow,
        };
        db.ConnectionFolderRules.Add(rule);
        await db.SaveChangesAsync(Cancel);
        return rule.Id;
    }

    private Task<IReadOnlyList<ConnectionRuleInfo>> RulesAsync() =>
        _world.Get<ConnectionRuleService>().GetAsync(_world.Id, _world.TenantId, Cancel);

    private async Task<List<ConnectionFolderRule>> AllRulesAsync()
    {
        await using var db = await _world.Get<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Cancel);
        return await db
            .ConnectionFolderRules.AsNoTracking()
            .Where(r => r.ConnectionId == _world.Id)
            .ToListAsync(Cancel);
    }

    private Task<HttpResponseMessage> PostAsync<T>(string route, T body) =>
        Bob.PostAsJsonAsync($"{_world.Base}/{route}", body, RuledConnection.Json, Cancel);

    private async Task<ConnectionTransferResults> ResultsAsync(ConnectionTransferRequest request)
    {
        using var answer = await PostAsync("files/transfer", request);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Cancel));
        return (await answer.Content.ReadFromJsonAsync<ConnectionTransferResults>(RuledConnection.Json, Cancel))!;
    }

    private async Task<string?> TypeOfAsync(HttpResponseMessage answer) =>
        JsonDocument.Parse(await answer.Content.ReadAsStringAsync(Cancel)).RootElement.GetProperty("type").GetString();

    private async Task<FileJobView> FinishedAsync(HttpResponseMessage started)
    {
        Assert.AreEqual(HttpStatusCode.Accepted, started.StatusCode, await started.Content.ReadAsStringAsync(Cancel));
        var id = (await started.Content.ReadFromJsonAsync<FileJobView>(RuledConnection.Json, Cancel))!.Id;
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await Bob.GetFromJsonAsync<FileJobView>($"/api/files/jobs/{id}", RuledConnection.Json, Cancel))!;
            if (job.State is FileJobState.Completed or FileJobState.Cancelled)
                return job;
            Assert.IsLessThan(until, DateTime.UtcNow, $"Job {id} is {job.State}");
            await Task.Delay(20, Cancel);
        }
    }
}
