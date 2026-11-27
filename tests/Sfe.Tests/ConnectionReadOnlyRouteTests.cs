using System.Net;
using System.Net.Http.Json;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Read-only or writable told on a connection (<see cref="ConnectionAccessReport"/>, item-access.md): a listing's
/// reason (a folder rule, the groups' level, the connection set read-only, the storage refusing changes), the lock of
/// children that differ, and Properties' Access section. <see cref="RuledConnection"/>: bob's Writers have Write,
/// a rule keeps <c>ReadOnly</c> read only.
/// </summary>
[TestClass]
public sealed class ConnectionReadOnlyRouteTests
{
    private RuledConnection _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync() => _world = await RuledConnection.CreateAsync(Cancel);

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task AFolderARuleKeepsReadOnlyIsTold()
    {
        var root = await BrowseAsync("");
        var kept = await BrowseAsync("ReadOnly");
        var open = await BrowseAsync("Public");

        Assert.IsNull(root.Listing.ReadOnly);
        Assert.IsTrue(root.Listing.Access!.Children!["ReadOnly"].ReadOnly, "its lock in the listing above");
        Assert.IsFalse(root.Listing.Access.Children["Mixed"].ReadOnly, "it only holds a hidden folder");
        Assert.AreEqual(ReadOnlyReason.Rule, kept.Listing.ReadOnly);
        Assert.IsNull(open.Listing.ReadOnly);
    }

    [TestMethod]
    public async Task AConnectionSharedToReadIsToldAsTheLevel()
    {
        await _world.ShareAsync(new() { [_world.WritersId] = FolderLevel.Read }, Cancel);

        Assert.AreEqual(ReadOnlyReason.Level, (await BrowseAsync("Public")).Listing.ReadOnly);
        Assert.AreEqual(ReadOnlyReason.Rule, (await BrowseAsync("ReadOnly")).Listing.ReadOnly);
        Assert.IsNull((await BrowseAsync("Lead/Open")).Listing.ReadOnly, "a rule raises the level");
    }

    [TestMethod]
    public async Task AConnectionSetReadOnlyIsToldFirst()
    {
        var change = new ConnectionChange(ReadOnly: true);
        await _world.Get<ConnectionService>().UpdateAsync(_world.Id, change, null, Cancel);

        Assert.AreEqual(ReadOnlyReason.Connection, (await BrowseAsync("Lead/Open")).Listing.ReadOnly);
        Assert.AreEqual(ReadOnlyReason.Connection, (await BrowseAsync("ReadOnly")).Listing.ReadOnly);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "from the index")]
    [DataRow(false, DisplayName = "from the provider")]
    public async Task AFolderTheStorageRefusesIsToldAndAskedOnceAWhile(bool indexed)
    {
        StorageTellsReadOnly("Public");
        if (!indexed)
            await _world.ListFromProviderAsync(Cancel);

        var first = await BrowseAsync("Public");
        var again = await BrowseAsync("Public");
        var mixed = await BrowseAsync("Mixed");

        Assert.AreEqual(ReadOnlyReason.Provider, first.Listing.ReadOnly);
        Assert.AreEqual(ReadOnlyReason.Provider, again.Listing.ReadOnly);
        Assert.IsNull(mixed.Listing.ReadOnly);
        Assert.AreEqual(1, _world.Account.Calls.Count(c => c == "get Public"), "kept a while per folder");
    }

    [TestMethod]
    public async Task ItemsTheStorageRefusesAreMarkedInListingsReadFromIt()
    {
        StorageTellsReadOnly("Public");
        await _world.ListFromProviderAsync(Cancel);

        var root = await BrowseAsync("");

        var marks = root.Listing.Items.ToDictionary(i => i.Name, i => i.ReadOnly);
        Assert.IsTrue(marks["Public"]);
        Assert.IsNull(marks["Mixed"]);
    }

    [TestMethod]
    public async Task TheStorageIsNotAskedWhereSfeRefusesAlreadyNorWhenItTellsNothing()
    {
        StorageTellsReadOnly("ReadOnly");
        Assert.AreEqual(ReadOnlyReason.Rule, (await BrowseAsync("ReadOnly")).Listing.ReadOnly);
        _world.Provider.Capabilities = _world.Provider.Capabilities with { ReadOnlyItems = false };
        Assert.IsNull((await BrowseAsync("Public")).Listing.ReadOnly);

        Assert.IsFalse(_world.Account.Calls.Any(c => c.StartsWith("get ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PropertiesTellWhatARuleLeaves()
    {
        var file = await PropertiesAsync("ReadOnly/r.txt");
        var folder = await PropertiesAsync("Public");

        var actions = Actions(file.Access!);
        Assert.IsTrue(actions[ItemAction.Open].Allowed);
        Assert.AreEqual(ReadOnlyReason.Rule, actions[ItemAction.SaveOver].Reason);
        Assert.AreEqual(ReadOnlyReason.Rule, actions[ItemAction.Rename].Reason);
        Assert.AreEqual("ReadOnly", file.Access!.RuleFolder);
        Assert.IsNull(file.Access.Disk);
        Assert.IsTrue(folder.Access!.Actions.All(a => a.Allowed), "Writers change Public");
        Assert.IsNull(folder.Access.RuleFolder);
    }

    [TestMethod]
    public async Task PropertiesTellWhatTheStorageRefuses()
    {
        StorageTellsReadOnly("Public");

        var file = await PropertiesAsync("Public/a.txt");
        var folder = await PropertiesAsync("Public");

        var onFile = Actions(file.Access!);
        Assert.IsTrue(onFile[ItemAction.Download].Allowed);
        Assert.AreEqual(ReadOnlyReason.Rights, onFile[ItemAction.SaveOver].Reason, "Writers can't make files");
        Assert.AreEqual(ReadOnlyReason.Provider, onFile[ItemAction.Rename].Reason);
        Assert.AreEqual(ReadOnlyReason.Provider, onFile[ItemAction.Delete].Reason);
        Assert.AreEqual(ReadOnlyReason.Provider, file.Access!.Disk);
        var onFolder = Actions(folder.Access!);
        Assert.AreEqual(ReadOnlyReason.Provider, onFolder[ItemAction.CreateInside].Reason);
        Assert.IsTrue(onFolder[ItemAction.Rename].Allowed, "its own folder, the top, takes changes");
        Assert.AreEqual(ReadOnlyReason.Provider, folder.Access!.Disk);
    }

    /// <summary>The provider marks <paramref name="folder"/> and what is inside read-only.</summary>
    private void StorageTellsReadOnly(string folder)
    {
        _world.Provider.Capabilities = _world.Provider.Capabilities with { ReadOnlyItems = true };
        _world.Account.ReadOnlyFolders.Add(folder);
    }

    private static Dictionary<ItemAction, ActionAccess> Actions(ItemAccess access) =>
        access.Actions.ToDictionary(a => a.Action);

    private async Task<ConnectionListing> BrowseAsync(string path)
    {
        using var answer = await _world.Bob.GetAsync($"{_world.Base}/browse?path={Uri.EscapeDataString(path)}", Cancel);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, path);
        return (await answer.Content.ReadFromJsonAsync<ConnectionListing>(RuledConnection.Json, Cancel))!;
    }

    private async Task<ConnectionItemProperties> PropertiesAsync(string path)
    {
        var url = $"{_world.Base}/files/properties?path={Uri.EscapeDataString(path)}";
        using var answer = await _world.Bob.GetAsync(url, Cancel);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, path);
        return (await answer.Content.ReadFromJsonAsync<ConnectionItemProperties>(RuledConnection.Json, Cancel))!;
    }
}
