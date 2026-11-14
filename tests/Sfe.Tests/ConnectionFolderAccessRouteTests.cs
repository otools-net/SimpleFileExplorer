using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Files;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The folder rules of a connection on its browsing, reading and changing routes (<see cref="RuledConnection"/>):
/// a hidden folder's name is in no listing, total or later page, its items answer 404 before the provider is called
/// (whatever the spelling of the path), what is only read answers 403, a hidden name is taken, a folder holding a
/// hidden one is never changed or copied whole, rights count only at the level of the group granting them, and a
/// read-only connection caps every rule.
/// </summary>
[TestClass]
public sealed class ConnectionFolderAccessRouteTests
{
    private RuledConnection _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    private HttpClient Bob => _world.Bob;

    [TestInitialize]
    public async Task InitializeAsync() => _world = await RuledConnection.CreateAsync(Cancel);

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task AHiddenFolderIsInNoListingTotalOrLaterPage()
    {
        var pages = new List<ConnectionListing>();
        for (var offset = 0; offset < 10; offset++)
            pages.Add(await BrowseAsync("", offset, limit: 1));
        var mixed = await BrowseAsync("Mixed");
        var lead = await BrowseAsync("Lead");

        var names = pages.SelectMany(p => p.Listing.Items).Select(i => i.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "Lead", "Mixed", "Public", "ReadOnly" }, names);
        Assert.IsTrue(pages.All(p => p.Listing.Total == 4 && p.Listing.DirectoryCount == 4));
        CollectionAssert.AreEqual(new[] { "ok.txt" }, mixed.Listing.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual((1, 0), (mixed.Listing.Total, mixed.Listing.DirectoryCount));
        CollectionAssert.AreEqual(new[] { "Open" }, lead.Listing.Items.Select(i => i.Name).ToArray());
        Assert.IsTrue(lead.Listing.Access!.PathOnly);
        Assert.IsFalse(_world.Called("rivate") || _world.Called("Hidden"), "never asked of the provider");
    }

    [TestMethod]
    public async Task TheListingTellsWhatTheSessionMayDoHereAndInTheChildrenThatDiffer()
    {
        var root = (await BrowseAsync("")).Listing.Access!;
        var readOnly = (await BrowseAsync("ReadOnly")).Listing.Access!;

        CollectionAssert.Contains(root.Rights.ToArray(), Rights.RenameFiles);
        CollectionAssert.AreEquivalent(new[] { "Lead", "ReadOnly", "Mixed" }, root.Children!.Keys.ToArray());
        CollectionAssert.DoesNotContain(root.Children["ReadOnly"].Rights.ToArray(), Rights.RenameFiles);
        CollectionAssert.Contains(root.Children["ReadOnly"].Rights.ToArray(), Rights.ViewFiles);
        Assert.IsTrue(root.Children["Lead"].PathOnly);
        CollectionAssert.DoesNotContain(readOnly.Rights.ToArray(), Rights.DeleteFiles);
    }

    [TestMethod]
    public async Task ItemsOfAHiddenFolderAreNotFoundBeforeTheProviderIsCalled()
    {
        _world.Provider.Capabilities = _world.Provider.Capabilities with { Thumbnails = true };
        string[] asked =
        [
            "browse?path=Private",
            "browse?path=pRiVaTe",
            "browse?path=PRIVATE/Inner",
            "browse?path=Mixed/hidden",
            "browse?path=Public/../Private",
            "files/raw/Private/secret.txt",
            "files/raw/pRiVaTe/secret.txt",
            "files/raw/Private%2Fsecret.txt",
            "files/raw/Public/%2E%2E/Private/secret.txt",
            "files/raw/Mixed/Hidden/h.txt",
            "files/raw/Lead/l.txt",
            "thumbnails/Private/secret.txt",
            "files/properties?path=Private",
            "files/properties?path=private/secret.txt",
            "files/properties?path=Lead/l.txt",
        ];

        foreach (var route in asked)
        {
            using var answer = await Bob.GetAsync($"{_world.Base}/{route}", Cancel);
            Assert.AreEqual(HttpStatusCode.NotFound, answer.StatusCode, route);
        }
        using var seen = await Bob.GetAsync($"{_world.Base}/files/raw/public/A.TXT", Cancel);

        Assert.AreEqual(HttpStatusCode.OK, seen.StatusCode);
        Assert.IsFalse(_world.Called("/secret") || _world.Called("h.txt") || _world.Called("l.txt"));
        Assert.IsFalse(_world.Called("list Private") || _world.Called("Inner"));
    }

    [TestMethod]
    public async Task WhatIsOnlyReadIsNotChangedAndAHiddenNameIsTaken()
    {
        using var inHidden = await PostAsync("files/folders", new CreateFolderRequest("Private", "x"));
        using var inReadOnly = await PostAsync("files/folders", new CreateFolderRequest("ReadOnly", "x"));
        using var hiddenName = await PostAsync("files/folders", new CreateFolderRequest("", "private"));
        using var renameRead = await PostAsync("files/rename", new RenameRequest("ReadOnly/r.txt", "s.txt"));
        using var renameOnto = await PostAsync("files/rename", new RenameRequest("Mixed/ok.txt", "HIDDEN"));
        using var renameTree = await PostAsync("files/rename", new RenameRequest("Mixed", "Other"));
        using var made = await PostAsync("files/folders", new CreateFolderRequest("Lead/Open", "New"));

        Assert.AreEqual(HttpStatusCode.NotFound, inHidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, inReadOnly.StatusCode);
        Assert.AreEqual(FileEndpoints.ExistsType, await TypeOfAsync(hiddenName));
        Assert.AreEqual(HttpStatusCode.Forbidden, renameRead.StatusCode);
        Assert.AreEqual(FileEndpoints.ExistsType, await TypeOfAsync(renameOnto));
        Assert.AreEqual(HttpStatusCode.Forbidden, renameTree.StatusCode, "a hidden folder inside");
        Assert.AreEqual(HttpStatusCode.OK, made.StatusCode);
        CollectionAssert.AreEqual(new[] { "folder Lead/Open/New" }, Changes());
    }

    [TestMethod]
    public async Task DeletesMovesAndCopiesStopAtHiddenAndReadOnlyItems()
    {
        var deleted = await ResultsAsync<DeleteResults>(
            "files/delete",
            new DeleteRequest(["Private", "Mixed", "ReadOnly/r.txt", "lead/l.txt", "Public/b.jpg"])
        );
        var copied = await ResultsAsync<ConnectionTransferResults>(
            "files/transfer",
            new ConnectionTransferRequest("copy", ["Mixed", "Private/secret.txt", "Lead/Open/o.txt"], "Public")
        );
        var moved = await ResultsAsync<ConnectionTransferResults>(
            "files/transfer",
            new ConnectionTransferRequest("move", ["ReadOnly/r.txt", "Mixed"], "Public")
        );
        _world.Account.WithFile("Public/hidden", "named as a hidden folder");
        var ontoHidden = await ResultsAsync<ConnectionTransferResults>(
            "files/transfer",
            new ConnectionTransferRequest("copy", ["Public/hidden"], "Mixed", "replace")
        );
        using var intoHidden = await PostAsync(
            "files/transfer",
            new ConnectionTransferRequest("copy", ["a"], "Private")
        );
        using var intoRead = await PostAsync(
            "files/transfer",
            new ConnectionTransferRequest("copy", ["a"], "ReadOnly")
        );

        CollectionAssert.AreEqual(
            new[]
            {
                FileOperationStatus.NotFound,
                FileOperationStatus.Forbidden,
                FileOperationStatus.Forbidden,
                FileOperationStatus.NotFound,
                FileOperationStatus.Done,
            },
            deleted.Items.Select(i => i.Status).ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { FileOperationStatus.Forbidden, FileOperationStatus.NotFound, FileOperationStatus.Done },
            copied.Items.Select(i => i.Status).ToArray()
        );
        Assert.IsTrue(moved.Items.All(i => i.Status == FileOperationStatus.Forbidden));
        Assert.AreEqual(FileOperationStatus.Exists, ontoHidden.Items.Single().Status);
        Assert.AreEqual(HttpStatusCode.NotFound, intoHidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, intoRead.StatusCode);
        CollectionAssert.AreEqual(new[] { "delete Public/b.jpg", "copy Lead/Open/o.txt -> Public/o.txt" }, Changes());
        Assert.AreEqual("hidden", _world.Account.TextOf("Mixed/Hidden/h.txt"));
    }

    [TestMethod]
    public async Task ARightCountsOnlyAtTheLevelOfTheGroupGrantingIt()
    {
        string[] withoutDelete = [.. RuledConnection.WriterRights.Where(r => r != Rights.DeleteFiles)];
        var accounts = _world.Get<AccessService>();
        var writers = new GroupInput("Writers", Rights: withoutDelete, MemberIds: [_world.BobId]);
        await accounts.UpdateGroupAsync(_world.TenantId, _world.WritersId, writers, cancellationToken: Cancel);
        string[] deleting = [Rights.UseConnections, Rights.Browse, Rights.ViewFiles, Rights.DeleteFiles];
        var deleters = await _world.GroupAsync("Deleters", deleting, Cancel);
        await _world.ShareAsync(
            new() { [_world.WritersId] = FolderLevel.Write, [deleters] = FolderLevel.Read },
            Cancel
        );

        using var paired = await PostAsync("files/delete", new DeleteRequest(["Public/a.txt"]));
        await _world.SetRulesAsync(Cancel, _world.Rule("Public", FolderLevel.Write, deleters));
        var raised = await ResultsAsync<DeleteResults>("files/delete", new DeleteRequest(["Public/a.txt", "x/y"]));

        Assert.AreEqual(HttpStatusCode.Forbidden, paired.StatusCode, "delete is the Read group's, Write the other's");
        Assert.AreEqual(FileOperationStatus.Done, raised.Items[0].Status, "a rule raised the deleting group");
        Assert.AreEqual(FileOperationStatus.Forbidden, raised.Items[1].Status);
        CollectionAssert.AreEqual(new[] { "delete Public/a.txt" }, _world.Account.Calls.ToArray());
    }

    [TestMethod]
    public async Task AReadOnlyConnectionCapsEveryRule()
    {
        var change = new ConnectionChange(ReadOnly: true);
        await _world.Get<ConnectionService>().UpdateAsync(_world.Id, change, null, Cancel);

        using var folder = await PostAsync("files/folders", new CreateFolderRequest("Lead/Open", "New"));
        using var delete = await PostAsync("files/delete", new DeleteRequest(["Lead/Open/o.txt"]));
        var listing = await BrowseAsync("Lead/Open");

        Assert.AreEqual(HttpStatusCode.Forbidden, folder.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, delete.StatusCode);
        CollectionAssert.DoesNotContain(listing.Listing.Access!.Rights.ToArray(), Rights.CreateFolders);
        Assert.IsFalse(_world.Called("folder") || _world.Called("delete"));
    }

    /// <summary>The calls that reached the provider, but for reading an item.</summary>
    private string[] Changes() => [.. _world.Account.Calls.Where(c => !c.StartsWith("get ", StringComparison.Ordinal))];

    private async Task<ConnectionListing> BrowseAsync(string path, int offset = 0, int limit = 50)
    {
        var url = $"{_world.Base}/browse?path={Uri.EscapeDataString(path)}&offset={offset}&limit={limit}";
        using var answer = await Bob.GetAsync(url, Cancel);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, path);
        return (await answer.Content.ReadFromJsonAsync<ConnectionListing>(RuledConnection.Json, Cancel))!;
    }

    private Task<HttpResponseMessage> PostAsync<T>(string route, T body) =>
        Bob.PostAsJsonAsync($"{_world.Base}/{route}", body, RuledConnection.Json, Cancel);

    private async Task<T> ResultsAsync<T>(string route, object body)
    {
        using var answer = await Bob.PostAsJsonAsync($"{_world.Base}/{route}", body, RuledConnection.Json, Cancel);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Cancel));
        return (await answer.Content.ReadFromJsonAsync<T>(RuledConnection.Json, Cancel))!;
    }

    private async Task<string?> TypeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(RuledConnection.Json, Cancel))!.Type;
}
