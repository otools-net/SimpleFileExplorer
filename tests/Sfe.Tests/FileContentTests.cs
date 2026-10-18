using System.Text;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Reading and saving the content of library files (<see cref="FileContent"/>): versions, saving in place, the previous
/// version in the trash, the index row, and what must never be left behind.
/// </summary>
[TestClass]
public sealed class FileContentTests
{
    private static readonly FileActor Me = new(null, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileContent Content => _services.Get<FileContent>();
    private Trash Trash => _services.Get<Trash>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ReadsUpToTheLimit()
    {
        var file = WriteText("Docs/digits.txt", "0123456789");

        var whole = (await Content.ReadAsync(file, 10, Token))!;
        var tooLarge = (await Content.ReadAsync(file, 9, Token))!;

        Assert.AreEqual("0123456789", Encoding.ASCII.GetString(whole.Bytes!));
        Assert.AreEqual(10, whole.Version!.Size);
        Assert.AreEqual(whole.Version, await FileVersion.OfFileAsync(file, Token), "The same version, read whole or streamed");
        Assert.IsTrue(tooLarge.TooLarge);
        Assert.AreEqual(10, tooLarge.Size);
        Assert.IsNull(await Content.ReadAsync(file + ".missing", 10, Token));
    }

    [TestMethod]
    public async Task SavesOnlyOverTheVersionItStartedFrom()
    {
        var file = WriteText("Docs/notes.txt", "first");
        var opened = await FileVersion.OfFileAsync(file, Token);

        var saved = await SaveAsync("Docs/notes.txt", "second", opened.Token);
        File.WriteAllText(file, "changed elsewhere");
        var stale = await SaveAsync("Docs/notes.txt", "third", saved.Version!.Token);
        var anyway = await SaveAsync("Docs/notes.txt", "fourth", "*");

        Assert.AreEqual(OverwriteStatus.Done, saved.Status, saved.Message);
        Assert.AreEqual(OverwriteStatus.Changed, stale.Status);
        Assert.AreEqual(OverwriteStatus.Done, anyway.Status, anyway.Message);
        Assert.AreEqual("fourth", File.ReadAllText(file));
        Assert.AreEqual(anyway.Version, await FileVersion.OfFileAsync(file, Token), "The answer's version is the file's");
        Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.sfe-tmp"), "No temporary file left");
    }

    [TestMethod]
    public async Task KeepsThePreviousVersionInTheTrashOncePerEditingSession()
    {
        var file = WriteText("Docs/notes.txt", "original");

        var first = await SaveAsync("Docs/notes.txt", "one", "*", session: "a");
        var second = await SaveAsync("Docs/notes.txt", "two", "*", session: "a");
        var otherSession = await SaveAsync("Docs/notes.txt", "three", "*", session: "b");
        var permanent = await SaveAsync("Docs/notes.txt", "four", "*", session: "c", keepPrevious: false);

        Assert.IsNotNull(first.TrashId);
        Assert.IsNull(second.TrashId, "Once per session");
        Assert.IsNotNull(otherSession.TrashId);
        Assert.IsNull(permanent.TrashId, "A space deleting for good keeps none");
        var page = await Trash.ListAsync(null, null, _ => true, Token);
        Assert.HasCount(2, page.Items);
        Assert.IsTrue(page.Items.All(item => item.PreviousVersion && item.Path == "Docs/notes.txt"));

        // Restored, a previous version comes back next to the file.
        var restored = await Trash.RestoreAsync(first.TrashId!.Value, Me, Token);
        Assert.AreEqual(FileOperationStatus.Done, restored.Status, restored.Message);
        Assert.AreEqual("Docs/notes (2).txt", restored.Item!.Path);
        Assert.AreEqual("original", File.ReadAllText(_library.FullPath("Docs/notes (2).txt")));
        Assert.AreEqual("four", File.ReadAllText(file));
    }

    [TestMethod]
    public async Task TheFileKeepsItsIndexRowAndTagsWhichFollowTheNewContent()
    {
        var file = WriteText("Media/notes.txt", "short");
        await ScanAsync();
        var tag = (await Tags.CreateAsync(new TagInput("Todo"), Token)).Tag!.Id;
        var (tagged, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(["Media/notes.txt"], Add: [tag]), Token);
        Assert.IsNotNull(tagged, errors is null ? null : string.Join(" ", errors.SelectMany(e => e.Value)));
        int id;
        await using (var db = await DbAsync())
            id = await db.Files.Where(f => f.FullName == file).Select(f => f.Id).SingleAsync(Token);
        var changed = new List<string>();
        _services.Get<Sfe.Core.Library.LibraryBrowser>().FolderChanged += changed.Add;

        var saved = await SaveAsync("Media/notes.txt", "a much longer content than before", "*");

        Assert.AreEqual(OverwriteStatus.Done, saved.Status, saved.Message);
        await using (var db = await DbAsync())
        {
            var row = await db.Files.SingleAsync(f => f.FullName == file, Token);
            Assert.AreEqual(id, row.Id, "The same row");
            Assert.AreEqual(new FileInfo(file).Length, row.Size);
            Assert.AreEqual(new FileInfo(file).LastWriteTime, row.LastWriteTime);
            Assert.AreEqual(await FileFingerprint.ComputeAsync(file, Token), row.Sha1, "The new content's fingerprint (duplicates)");
        }
        CollectionAssert.AreEqual(new[] { tag }, (await Tags.GetFileTagsAsync("Media/notes.txt", Token))!.Tags.Select(t => t.Id).ToArray());
        CollectionAssert.Contains(changed, "Media", "The change feed hears of it");
    }

    [TestMethod]
    public async Task PurgingAPreviousVersionKeepsTheLiveFilesPreviews()
    {
        var file = WriteText("Media/notes.txt", "original");
        var preview = _services.Get<ThumbnailService>().GetCachePath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        File.WriteAllText(preview, "thumbnail");

        var saved = await SaveAsync("Media/notes.txt", "new", "*");
        Assert.IsTrue(await Trash.PurgeAsync(saved.TrashId!.Value, Me, Token));
        await Trash.CacheDeletes;

        Assert.IsTrue(File.Exists(preview));
        Assert.AreEqual("new", File.ReadAllText(file));
    }

    [TestMethod]
    public async Task ReadOnlyFilesLinksAndScansAreNotSavedOver()
    {
        var file = WriteText("Docs/locked.txt", "keep");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            Assert.AreEqual(OverwriteStatus.ReadOnly, (await SaveAsync("Docs/locked.txt", "x", "*")).Status);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        using (var hold = _services.Get<ScanCoordinator>().TryBeginOperation(_library.FullPath("Docs")))
        {
            Assert.IsTrue(_services.Get<ScanCoordinator>().TryStart(""));
            Assert.AreEqual(OverwriteStatus.ScanRunning, (await SaveAsync("Docs/locked.txt", "x", "*")).Status);
        }
        await _services.Get<ScanCoordinator>().Completion!;

        Assert.AreEqual(OverwriteStatus.NotFound, (await SaveAsync("Docs/nothing.txt", "x", "*")).Status);
        Assert.AreEqual(OverwriteStatus.NotFound, (await SaveAsync("../outside.txt", "x", "*")).Status);
        Assert.AreEqual("keep", File.ReadAllText(file));
        Assert.IsEmpty((await Trash.ListAsync(null, null, _ => true, Token)).Items, "Nothing kept for what wasn't saved");
    }

    [TestMethod]
    public async Task ALinkIsNotSavedOver()
    {
        var file = WriteText("Docs/target.txt", "keep");
        if (!TryCreateFileLink(_library.FullPath("Docs/link.txt"), file))
            Assert.Inconclusive("Symbolic links to files can't be made here (Windows without developer mode).");

        Assert.AreEqual(OverwriteStatus.IsLink, (await SaveAsync("Docs/link.txt", "x", "*")).Status);
        Assert.AreEqual("keep", File.ReadAllText(file));
    }

    [TestMethod]
    public async Task AFileInUseIsLeftAsItWasWithNothingBehind()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Only Windows refuses to replace a file another program holds.");
        var file = WriteText("Docs/busy.txt", "before");

        OverwriteResult result;
        // Another program has it open, reading only (others may read it, not replace it).
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await SaveAsync("Docs/busy.txt", "after", "*");

        Assert.AreEqual(OverwriteStatus.Failed, result.Status);
        StringAssert.Contains(result.Message, "in use");
        Assert.AreEqual("before", File.ReadAllText(file));
        Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.sfe-tmp"), "No temporary file left");
        Assert.IsEmpty((await Trash.ListAsync(null, null, _ => true, Token)).Items, "No previous version kept for a save that didn't happen");
    }

    [TestMethod]
    public async Task ACopyWithContentIsNeverWrittenOverAnother()
    {
        WriteText("Docs/notes.txt", "mine");
        var files = _services.Get<FileOperations>();

        var copy = await files.CreateFileAsync("Docs", "notes copy.txt", Me, Token, Encoding.UTF8.GetBytes("copied"));
        var clash = await files.CreateFileAsync("Docs", "notes.txt", Me, Token, Encoding.UTF8.GetBytes("over"));

        Assert.AreEqual(FileOperationStatus.Done, copy.Status, copy.Message);
        Assert.AreEqual("copied", File.ReadAllText(_library.FullPath("Docs/notes copy.txt")));
        Assert.AreEqual(FileOperationStatus.Exists, clash.Status);
        Assert.AreEqual("mine", File.ReadAllText(_library.FullPath("Docs/notes.txt")));
    }

    [TestMethod]
    public async Task AnExistingSqliteTrashGetsTheColumnOfPreviousVersions()
    {
        // The trash as 1.66 created it: what was deleted before stays a deletion.
        var deleted = await _services.Get<FileOperations>().DeleteAsync("RFile1.txt", permanent: false, Me, Token);
        await using (var db = await DbAsync())
            await db.Database.ExecuteSqlAsync($"ALTER TABLE TrashEntries DROP COLUMN Kind", Token);

        await _services.Get<DatabaseInitializer>().InitializeAsync(Token);
        WriteText("Docs/notes.txt", "original");
        var saved = await SaveAsync("Docs/notes.txt", "new", "*");

        var items = (await Trash.ListAsync(null, null, _ => true, Token)).Items;
        Assert.AreEqual(OverwriteStatus.Done, saved.Status, saved.Message);
        Assert.IsTrue(items.Single(item => item.Id == saved.TrashId).PreviousVersion);
        Assert.AreEqual(FileOperationStatus.Done, deleted.Status, deleted.Message);
        Assert.IsFalse(items.Single(item => item.Path == "RFile1.txt").PreviousVersion);
    }

    private TagService Tags => _services.Get<TagService>();

    private Task<OverwriteResult> SaveAsync(string path, string text, string ifMatch, string session = "s", bool keepPrevious = true) =>
        Content.OverwriteAsync(path, Encoding.UTF8.GetBytes(text), ifMatch, session, keepPrevious, Me, Token);


    [TestMethod]
    public async Task AnyPartOfALargeFileIsReadAtWholeLines()
    {
        var lines = string.Concat(Enumerable.Range(1, 2000).Select(i => $"line {i:0000}\n"));    // 10 bytes a line
        var full = WriteText("big.log", lines);

        var start = (await Content.ReadPartAsync(full, 0, 95, null, Token))!;
        var middle = (await Content.ReadPartAsync(full, start.End + 5, 100, null, Token))!;
        var end = (await Content.ReadPartAsync(full, -1, 95, null, Token))!;
        var past = (await Content.ReadPartAsync(full, 1_000_000, 100, null, Token))!;

        Assert.AreEqual(("line 0001\n", 0L, 90L, 20_000L, TextCodec.Utf8), (start.Text![..10], start.Offset, start.End, start.Size, start.Encoding));
        Assert.AreEqual((100L, "line 0011\n"), (middle.Offset, middle.Text![..10]), "from the line after the offset");
        Assert.IsTrue(middle.Text.EndsWith('\n'));
        Assert.AreEqual((19_910L, 20_000L, "line 1992\n"), (end.Offset, end.End, end.Text![..10]));
        Assert.AreEqual((20_000L, ""), (past.Offset, past.Text));
        Assert.IsNull(await Content.ReadPartAsync(Path.Combine(_library.Root, "nothing.log"), 0, 10, null, Token));
    }

    [TestMethod]
    public async Task PartsOfUtf16AndOfBinaryFiles()
    {
        var wide = _library.FullPath("wide.txt");
        await File.WriteAllBytesAsync(wide, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("one\ntwo\nthree\n")], Token);
        var binary = _library.FullPath("data.bin");
        await File.WriteAllBytesAsync(binary, [0x89, 0x50, 0x00, 0x0A, 0x41], Token);

        var start = (await Content.ReadPartAsync(wide, 0, 10, null, Token))!;
        var next = (await Content.ReadPartAsync(wide, start.End, 100, null, Token))!;
        var refused = (await Content.ReadPartAsync(binary, 0, 100, null, Token))!;

        Assert.AreEqual(("one\n", TextCodec.Utf16Le, 2L), (start.Text, start.Encoding, start.Offset), "the mark is skipped");
        Assert.AreEqual("two\nthree\n", next.Text);
        Assert.IsNull(refused.Text);
    }

    [TestMethod]
    public async Task TheVersionIsReadAgainOnlyWhenTheSizeOrTimeChanged()
    {
        var full = WriteText("notes.txt", "first");
        var version = (await Content.VersionAsync(full, null, Token))!;
        var same = (await Content.VersionAsync(full, version.Token, Token))!;
        var forged = (await Content.VersionAsync(full, version.Token[..version.Token.LastIndexOf('-')] + "-0000", Token))!;
        await File.WriteAllTextAsync(full, "second!", Token);
        var changed = (await Content.VersionAsync(full, version.Token, Token))!;

        Assert.AreEqual(version, same);
        Assert.AreEqual(version.Token[..version.Token.LastIndexOf('-')] + "-0000", forged.Token, "Same size and time: not read again");
        Assert.AreNotEqual(version.Token, changed.Token);
        Assert.AreEqual((await FileVersion.OfFileAsync(full, Token)).Token, changed.Token);
        Assert.IsNull(await Content.VersionAsync(Path.Combine(_library.Root, "nothing.txt"), null, Token));
    }
    private string WriteText(string relative, string text)
    {
        var full = _library.FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    private async Task ScanAsync()
    {
        var coordinator = _services.Get<ScanCoordinator>();
        Assert.IsTrue(coordinator.TryStart(""));
        await coordinator.Completion!;
    }

    private Task<SfeDbContext> DbAsync() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
