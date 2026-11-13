using System.Text;
using Sfe.Core.Connections;
using Sfe.Core.Options;

namespace Sfe.Tests;

/// <summary>
/// What every <see cref="IRemoteStorage"/> must do, checked on the fake the other suites rely on
/// (<see cref="FakeStorage"/>): a real provider's tests assert the same behaviour against recorded answers.
/// </summary>
[TestClass]
public sealed class FakeStorageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static (FakeAccount Account, IRemoteStorage Storage) Open(string root = "")
    {
        var provider = new FakeProvider();
        var account = provider
            .Account("ann")
            .WithFile("Photos/a.jpg", "aaa")
            .WithFile("Photos/b.jpg", "bbbb")
            .WithFile("Photos/2024/c.jpg", "c")
            .WithFile("notes.txt", "0123456789")
            .WithFolder("Empty");
        return (account, provider.Open("ann", root, new ConnectionOptions()));
    }

    private static async Task<List<RemoteItem>> ListAllAsync(IRemoteStorage storage, string path)
    {
        var items = new List<RemoteItem>();
        string? cursor = null;
        do
        {
            var page = await storage.ListAsync(path, cursor, None);
            items.AddRange(page.Items);
            cursor = page.Cursor;
        } while (cursor is not null);
        return items;
    }

    [TestMethod]
    public async Task ListsAFolderPageByPage()
    {
        var (account, storage) = Open();

        var first = await storage.ListAsync("Photos", null, None);
        var all = await ListAllAsync(storage, "Photos");

        Assert.HasCount(account.PageSize, first.Items);
        Assert.IsNotNull(first.Cursor);
        CollectionAssert.AreEquivalent(
            new[] { "Photos/a.jpg", "Photos/b.jpg", "Photos/2024" },
            all.Select(i => i.Path).ToArray()
        );
        var folder = all.Single(i => i.IsFolder);
        Assert.IsNull(folder.Size);
        Assert.IsNull(folder.Modified);
        Assert.AreEqual(4, all.Single(i => i.Name == "b.jpg").Size);
    }

    [TestMethod]
    public async Task AMissingFolderIsNotFoundAndAMissingItemIsNull()
    {
        var (_, storage) = Open();

        var failure = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            storage.ListAsync("Nowhere", null, None)
        );

        Assert.AreEqual(RemoteErrorKind.NotFound, failure.Kind);
        Assert.IsNull(await storage.GetAsync("Nowhere/x", None));
        Assert.IsTrue((await storage.GetAsync("", None))!.IsFolder);
    }

    [TestMethod]
    public async Task ARootedStorageSeesOnlyItsFolder()
    {
        var (_, storage) = Open("Photos");

        var all = await ListAllAsync(storage, "");

        CollectionAssert.AreEquivalent(new[] { "a.jpg", "b.jpg", "2024" }, all.Select(i => i.Path).ToArray());
        Assert.IsNull(await storage.GetAsync("notes.txt", None));
        Assert.AreEqual("2024/c.jpg", (await storage.GetAsync("2024/c.jpg", None))!.Path);
    }

    [TestMethod]
    public async Task NamesCompareAsTheProviderSays()
    {
        var (_, storage) = Open();

        // Dropbox's rule, the fake's default: any case finds the item, shown as first spelled.
        Assert.AreEqual("Photos/a.jpg", (await storage.GetAsync("photos/A.JPG", None))!.Path);
        var failure = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            storage.CreateFolderAsync("PHOTOS", None)
        );
        Assert.AreEqual(RemoteErrorKind.Exists, failure.Kind);
    }

    [TestMethod]
    public async Task ReadsAFileWholeOrInPart()
    {
        var (_, storage) = Open();

        await using var whole = await storage.OpenReadAsync("notes.txt", null, None);
        await using var part = await storage.OpenReadAsync("notes.txt", new RemoteRange(2, 5), None);
        await using var tail = await storage.OpenReadAsync("notes.txt", new RemoteRange(7), None);

        Assert.AreEqual("0123456789", await TextAsync(whole));
        Assert.IsNull(whole.Range);
        Assert.AreEqual("2345", await TextAsync(part));
        Assert.AreEqual(new RemoteRange(2, 5), part.Range);
        Assert.AreEqual(10, part.TotalLength);
        Assert.AreEqual("789", await TextAsync(tail));
    }

    [TestMethod]
    public async Task UploadsInPartsAndRefusesAPartOutOfPlace()
    {
        var (account, storage) = Open();

        var upload = await storage.BeginUploadAsync(None);
        await storage.AppendUploadAsync(upload, 0, Bytes("hello "), None);
        var misplaced = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            storage.AppendUploadAsync(upload, 3, Bytes("xxx"), None)
        );
        await storage.AppendUploadAsync(upload, 6, Bytes("world"), None);
        var item = await storage.FinishUploadAsync(
            upload,
            new RemoteUploadTarget("New/hello.txt", 11, RemoteConflict.Fail),
            None
        );

        Assert.AreEqual(RemoteErrorKind.Refused, misplaced.Kind);
        Assert.AreEqual("New/hello.txt", item.Path);
        Assert.AreEqual(11, item.Size);
        Assert.AreEqual("hello world", account.TextOf("New/hello.txt"));
    }

    [TestMethod]
    public async Task AnUploadMeetingAFileFollowsTheConflictChoice()
    {
        var (account, storage) = Open();

        async Task<RemoteItem> UploadAsync(string text, RemoteConflict conflict)
        {
            var upload = await storage.BeginUploadAsync(None);
            await storage.AppendUploadAsync(upload, 0, Bytes(text), None);
            return await storage.FinishUploadAsync(upload, new("notes.txt", text.Length, conflict), None);
        }

        var failure = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            UploadAsync("x", RemoteConflict.Fail)
        );
        var kept = await UploadAsync("second", RemoteConflict.KeepBoth);
        var replaced = await UploadAsync("third", RemoteConflict.Replace);

        Assert.AreEqual(RemoteErrorKind.Exists, failure.Kind);
        Assert.AreEqual("notes (1).txt", kept.Path);
        Assert.AreEqual("notes.txt", replaced.Path);
        Assert.AreEqual("third", account.TextOf("notes.txt"));
        Assert.AreEqual("second", account.TextOf("notes (1).txt"));
    }

    [TestMethod]
    public async Task MovesAndCopiesAFolderWithEverythingInIt()
    {
        var (account, storage) = Open();

        var copy = await storage.CopyAsync("Photos", "Backup", RemoteConflict.Fail, None);
        var moved = await storage.MoveAsync("Photos", "Empty/Pictures", RemoteConflict.Fail, None);

        Assert.IsTrue(copy.IsFolder);
        Assert.AreEqual("Empty/Pictures", moved.Path);
        Assert.AreEqual("c", account.TextOf("Backup/2024/c.jpg"));
        Assert.AreEqual("c", account.TextOf("Empty/Pictures/2024/c.jpg"));
        Assert.IsFalse(account.Exists("Photos"));
        Assert.IsFalse(account.Exists("Photos/a.jpg"));
    }

    [TestMethod]
    public async Task AMoveMeetingSomethingFailsAndAFolderNeverGoesIntoItself()
    {
        var (account, storage) = Open();

        var exists = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            storage.MoveAsync("Photos/a.jpg", "Photos/b.jpg", RemoteConflict.Fail, None)
        );
        var inside = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            storage.MoveAsync("Photos", "Photos/2024/Photos", RemoteConflict.Fail, None)
        );
        var overFolder = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            storage.MoveAsync("notes.txt", "Empty", RemoteConflict.Replace, None)
        );
        var renamed = await storage.MoveAsync("Photos/a.jpg", "Photos/A.JPG", RemoteConflict.Fail, None);

        Assert.AreEqual(RemoteErrorKind.Exists, exists.Kind);
        Assert.AreEqual(RemoteErrorKind.Refused, inside.Kind);
        Assert.AreEqual(RemoteErrorKind.Exists, overFolder.Kind);
        Assert.AreEqual("Photos/A.JPG", renamed.Path);
        Assert.AreEqual("bbbb", account.TextOf("Photos/b.jpg"));
    }

    [TestMethod]
    public async Task DeletesIntoTheProvidersTrashAndNeverTheRoot()
    {
        var (account, storage) = Open();

        await storage.DeleteAsync("Photos", None);
        var root = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => storage.DeleteAsync("", None));

        Assert.IsFalse(account.Exists("Photos/2024/c.jpg"));
        CollectionAssert.Contains(account.Trash, "Photos");
        Assert.AreEqual(RemoteErrorKind.NotFound, root.Kind);
        Assert.IsTrue(account.Exists("notes.txt"));
    }

    [TestMethod]
    public async Task TellsWhatChangedSinceACursorInsideItsRootOnly()
    {
        var (account, storage) = Open("Photos");

        var start = await storage.ChangesAsync(null, None);
        account.WithFile("Photos/new.jpg", "n").WithFile("elsewhere.txt", "e");
        await storage.DeleteAsync("a.jpg", None);
        var changes = await storage.ChangesAsync(start.Cursor, None);
        var nothing = await storage.ChangesAsync(changes.Cursor, None);

        Assert.IsEmpty(start.Changes);
        CollectionAssert.AreEquivalent(new[] { "new.jpg", "a.jpg" }, changes.Changes.Select(c => c.Path).ToArray());
        Assert.IsNull(changes.Changes.Single(c => c.Path == "a.jpg").Item);
        Assert.IsEmpty(nothing.Changes);
    }

    [TestMethod]
    public async Task EveryCallFailsAsTheProviderDoes()
    {
        var (account, storage) = Open();
        account.Failing = RemoteErrorKind.RateLimited;
        account.RetryAfter = TimeSpan.FromSeconds(7);

        var limited = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => storage.GetAsync("x", None));
        account.Failing = RemoteErrorKind.CredentialsEnded;
        var ended = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => storage.SpaceAsync(None));

        Assert.AreEqual(RemoteErrorKind.RateLimited, limited.Kind);
        Assert.AreEqual(TimeSpan.FromSeconds(7), limited.RetryAfter);
        Assert.AreEqual(RemoteErrorKind.CredentialsEnded, ended.Kind);
    }

    [TestMethod]
    public async Task SignsInByCodeOrPassword()
    {
        var provider = new FakeProvider();
        var options = new ConnectionOptions();
        var none = new Dictionary<string, string>();

        var step = await provider.BeginSignInAsync(
            new SignInStart(SignInKind.OAuthRedirect, options, "https://sfe/back", "STATE", none),
            None
        );
        var grant = await provider.CompleteSignInAsync(
            new SignInCompletion(SignInKind.OAuthRedirect, options, step.Pending, "https://sfe/back", Code("good:ann")),
            None
        );
        var wrong = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            provider.CompleteSignInAsync(
                new SignInCompletion(SignInKind.OAuthCode, options, step.Pending, null, Code("bad")),
                None
            )
        );

        Assert.IsNull(step.Grant);
        StringAssert.StartsWith(step.Address, FakeProvider.AuthorizeAddress);
        StringAssert.Contains(step.Address, "state=STATE");
        Assert.AreEqual("ann", grant.Credentials);
        Assert.AreEqual("ann@fake", grant.Account);
        Assert.AreEqual(RemoteErrorKind.CredentialsEnded, wrong.Kind);
    }

    private static Dictionary<string, string> Code(string code) => new() { ["code"] = code };

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> TextAsync(RemoteContent content) =>
        await new StreamReader(content.Stream).ReadToEndAsync();
}
