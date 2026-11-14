using System.Text;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Transfers;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="RemoteTransferEndpoint"/>: a connection's side of a transfer over the fake provider (listing through
/// pages, reads from an offset, uploads part by part, free names and space, deletes into the provider's trash).
/// </summary>
[TestClass]
public sealed class RemoteTransferEndpointTests
{
    private const int Connection = 7;

    private readonly FakeProvider _provider = new();
    private readonly InstantClock _clock = new();
    private readonly FixedJobAccess _access = new();
    private FakeAccount _account = null!;
    private RemoteTransferEndpoint _endpoint = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestCleanup]
    public async Task CleanupAsync() => await _endpoint.DisposeAsync();

    [TestMethod]
    public async Task AFolderIsListedAcrossThePagesOfTheProvider()
    {
        Open();
        foreach (var name in new[] { "1", "2", "3", "4", "5" })
            _account.WithFile($"Docs/{name}.txt", name);

        var entries = new List<TransferEntry>();
        await foreach (var entry in _endpoint.ListAsync("Docs", Token))
            entries.Add(entry);

        Assert.HasCount(5, entries);
        Assert.AreEqual(3, _account.Calls.Count(c => c == "list Docs"));
        Assert.AreEqual("fake", _endpoint.HashName);
        Assert.IsNull(await _endpoint.GetAsync("Docs/9.txt", Token));
    }

    [TestMethod]
    public async Task AFileIsReadFromAnOffsetWithRangedReads()
    {
        Open();
        _account.WithFile("a.txt", "0123456789");

        var entry = await _endpoint.GetAsync("a.txt", Token);

        Assert.AreEqual("456789", await ReadAsync(entry!, 4));
        Assert.AreEqual("0123456789", await ReadAsync(entry!, 0));
    }

    [TestMethod]
    public async Task AFileIsReadFromAnOffsetBySkippingWithoutRangedReads()
    {
        Open(_provider.Capabilities with { RangedReads = false });
        _account.WithFile("a.txt", "0123456789");

        var entry = await _endpoint.GetAsync("a.txt", Token);

        Assert.AreEqual("456789", await ReadAsync(entry!, 4));
    }

    [TestMethod]
    public async Task AFileIsWrittenInPartsThenCommitted()
    {
        Open(_provider.Capabilities with { MaxUploadPart = 4 });
        _account.WithFolder("Docs");
        _account.FailNext("upload-append", RemoteErrorKind.Unavailable);

        await using var write = await _endpoint.BeginWriteAsync("Docs", "b.txt", 11, null, Token);
        await write.WriteAsync(Bytes("hello "), Token);
        await write.WriteAsync(Bytes("world"), Token);
        var written = await write.CommitAsync(Token);

        Assert.AreEqual(11, write.Offset);
        Assert.AreEqual("hello world", _account.TextOf("Docs/b.txt"));
        Assert.AreEqual(new TransferEntry("b.txt", "Docs/b.txt", false, 11, written.Modified, written.Hash), written);
        Assert.AreEqual(5, _account.Calls.Count(c => c.StartsWith("upload-append", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ACommitNeverGoesOverAnItemOfTheName()
    {
        Open();
        _account.WithFile("b.txt", "old");

        await using var write = await _endpoint.BeginWriteAsync("", "B.TXT", 3, null, Token);
        await write.WriteAsync(Bytes("new"), Token);
        var failure = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => write.CommitAsync(Token));

        Assert.AreEqual(RemoteErrorKind.Exists, failure.Kind);
        Assert.AreEqual("old", _account.TextOf("b.txt"));
    }

    [TestMethod]
    public async Task TheProvidersOffsetMovesTheWritesOffset()
    {
        Open();

        await using var write = await _endpoint.BeginWriteAsync("", "c.txt", 6, null, Token);
        await write.WriteAsync(Bytes("abc"), Token);
        _account.WrongOffsetNext(1);
        await write.WriteAsync(Bytes("def"), Token);

        Assert.AreEqual(1, write.Offset);
    }

    [TestMethod]
    public async Task AFolderMadeOrAlreadyThereIsAnswered()
    {
        Open();
        _account.WithFolder("Docs");

        var made = await _endpoint.CreateFolderAsync("Docs", "New", Token);
        var existing = await _endpoint.CreateFolderAsync("", "docs", Token);

        Assert.AreEqual("Docs/New", made.Path);
        Assert.IsTrue(_account.Exists("Docs/New"));
        Assert.AreEqual("Docs", existing.Path);
        Assert.IsTrue(existing.IsFolder);
    }

    [TestMethod]
    public async Task AFreeNameIsNumberedByTheProvidersRuleForCase()
    {
        Open();
        _account.WithFile("Photo.JPG", "1").WithFile("photo (2).jpg", "2").WithFolder("v1.0");

        Assert.AreEqual("photo (3).jpg", await _endpoint.FreeNameAsync("", "photo.jpg", false, Token));
        Assert.AreEqual("V1.0 (2)", await _endpoint.FreeNameAsync("", "V1.0", true, Token));
        Assert.AreEqual("new.jpg", await _endpoint.FreeNameAsync("", "new.jpg", false, Token));
    }

    [TestMethod]
    public async Task AFreeNameKeepsADifferentCaseWhereNamesAreCaseSensitive()
    {
        Open(_provider.Capabilities with { CaseInsensitiveNames = false });
        _account.WithFile("Photo.JPG", "1");

        Assert.AreEqual("photo.jpg", await _endpoint.FreeNameAsync("", "photo.jpg", false, Token));
        Assert.AreEqual("Photo (2).JPG", await _endpoint.FreeNameAsync("", "Photo.JPG", false, Token));
    }

    [TestMethod]
    public async Task TheFreeSpaceIsTheProvidersWhenItTells()
    {
        Open();
        _account.TotalSpace = 100;
        _account.WithFile("a.txt", "0123456789");

        Assert.AreEqual(90, (await _endpoint.FreeSpaceAsync("", Token)).FreeBytes);

        await _endpoint.DisposeAsync();
        Open(_provider.Capabilities with { FreeSpace = false });
        Assert.IsNull((await _endpoint.FreeSpaceAsync("", Token)).FreeBytes);
    }

    [TestMethod]
    public async Task AMovedSourceGoesToTheProvidersTrash()
    {
        Open();
        _account.WithFile("Docs/a.txt", "a");
        var entry = await _endpoint.GetAsync("Docs/a.txt", Token);

        await _endpoint.DeleteSourceAsync(entry!, Token);

        Assert.IsFalse(_account.Exists("Docs/a.txt"));
        CollectionAssert.AreEqual(new[] { "Docs/a.txt" }, _account.Trash);
    }

    [TestMethod]
    public async Task WhatFolderRulesHideIsNotFoundNorListedAndItsNameIsTaken()
    {
        Open();
        _account.WithFile("Docs/a.txt", "a").WithFile("Docs/Hidden/h.txt", "h").WithFile("Docs/Hidden.txt", "x");
        _access.Folders = FixedJobAccess.Hiding("Docs/Hidden");

        var listed = new List<string>();
        await foreach (var entry in _endpoint.ListAsync("Docs", Token))
            listed.Add(entry.Name);
        var hidden = await _endpoint.GetAsync("docs/HIDDEN/h.txt", Token);
        var free = await _endpoint.FreeNameAsync("Docs", "hidden", isFolder: true, Token);
        var written = await Assert.ThrowsAsync<RemoteStorageException>(() =>
            _endpoint.BeginWriteAsync("Docs", "Hidden", 1, null, Token)
        );
        var docs = await _endpoint.GetAsync("Docs", Token);
        var moved = await Assert.ThrowsAsync<RemoteStorageException>(() => _endpoint.DeleteSourceAsync(docs!, Token));

        CollectionAssert.AreEquivalent(new[] { "a.txt", "Hidden.txt" }, listed);
        Assert.IsNull(hidden);
        Assert.AreEqual("hidden (2)", free);
        Assert.AreEqual(RemoteErrorKind.Exists, written.Kind);
        Assert.AreEqual(RemoteErrorKind.Refused, moved.Kind, "a folder holding a hidden one stays");
        Assert.IsTrue(_account.Exists("Docs/Hidden/h.txt"));
        Assert.IsFalse(_account.Calls.Any(c => c.Contains("hidden/", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("upload", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ThePersonsAccessIsReadAgainAtEveryItem()
    {
        Open();
        _account.WithFile("Docs/a.txt", "a").WithFile("Docs/b.txt", "b");

        var first = await _endpoint.GetAsync("Docs/a.txt", Token);
        _access.Folders = FixedJobAccess.Hiding("Docs");
        var second = await _endpoint.GetAsync("Docs/b.txt", Token);
        var read = await Assert.ThrowsAsync<RemoteStorageException>(() => _endpoint.OpenReadAsync(first!, 0, Token));
        _access.Folders = null;
        var gone = await Assert.ThrowsAsync<RemoteStorageException>(() => _endpoint.GetAsync("Other", Token));

        Assert.IsNotNull(first);
        Assert.IsNull(second);
        Assert.AreEqual(RemoteErrorKind.NotFound, read.Kind);
        Assert.AreEqual(RemoteErrorKind.Refused, gone.Kind);
        CollectionAssert.AreEqual(new[] { "get Docs/a.txt" }, _account.Calls);
    }

    private void Open(ProviderCapabilities? capabilities = null)
    {
        _provider.Capabilities = capabilities ?? _provider.Capabilities;
        _account = _provider.Account("alice");
        var storage = _provider.Open("alice", "", new());
        var calls = new TransferCalls(new ConnectionGates(_clock), _clock);
        _endpoint = new RemoteTransferEndpoint(
            _access.Side(Connection, storage, _provider.Capabilities, Rights.MoveFiles),
            calls,
            new RemoteListings(_clock)
        );
    }

    private async Task<string> ReadAsync(TransferEntry entry, long offset)
    {
        await using var stream = await _endpoint.OpenReadAsync(entry, offset, Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(Token);
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
