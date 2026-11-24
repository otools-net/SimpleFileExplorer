using System.Text;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The previous versions of a file (<see cref="PreviousVersions"/>): listed from the trash, restored as a copy or over
/// the file, following the file when it is renamed.
/// </summary>
[TestClass]
public sealed class PreviousVersionsTests
{
    private static readonly FileActor Me = new(null, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileContent Content => _services.Get<FileContent>();
    private PreviousVersions Versions => _services.Get<PreviousVersions>();
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
    public async Task AFileListsItsVersionsNewestFirstAndNothingElseOfTheTrash()
    {
        WriteText("Docs/notes.txt", "one");
        WriteText("Docs/other.txt", "other");
        await SaveAsync("Docs/notes.txt", "two", "a");
        await SaveAsync("Docs/notes.txt", "three", "b");
        await SaveAsync("Docs/other.txt", "changed", "c");
        await _services.Get<FileOperations>().DeleteAsync("Docs/other.txt", false, Me, Token);

        var versions = await Versions.ListAsync("Docs/notes.txt", Token);

        Assert.HasCount(2, versions);
        Assert.IsTrue(versions.All(v => v.PreviousVersion && v.Path == "Docs/notes.txt"));
        Assert.AreEqual("two", await ContentOfAsync(versions[0].Id), "The newest first: saved over by three");
        Assert.AreEqual("one", await ContentOfAsync(versions[1].Id));
        Assert.IsEmpty(await Versions.ListAsync("Docs/missing.txt", Token));
    }

    [TestMethod]
    public async Task RestoredAsACopyAVersionComesNextToTheFileAndStays()
    {
        var file = WriteText("Docs/notes.txt", "original");
        await SaveAsync("Docs/notes.txt", "edited", "a");
        var version = (await Versions.ListAsync("Docs/notes.txt", Token)).Single();

        var copy = await Versions.RestoreAsCopyAsync(version.Id, file, "Docs/notes.txt", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, copy.Status, copy.Message);
        Assert.AreEqual("Docs/notes (2).txt", copy.Item!.Path);
        Assert.AreEqual("original", File.ReadAllText(_library.FullPath("Docs/notes (2).txt")));
        Assert.AreEqual("edited", File.ReadAllText(file));
        Assert.HasCount(1, await Versions.ListAsync("Docs/notes.txt", Token), "The version stays");
    }

    [TestMethod]
    public async Task RestoredOverTheFileTheContentItHadBecomesAVersion()
    {
        var file = WriteText("Docs/notes.txt", "original");
        await SaveAsync("Docs/notes.txt", "edited", "a");
        var version = (await Versions.ListAsync("Docs/notes.txt", Token)).Single();

        var restored = await Versions.RestoreOverAsync(version.Id, file, "Docs/notes.txt", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, restored.Status, restored.Message);
        Assert.AreEqual("original", File.ReadAllText(file));
        var versions = await Versions.ListAsync("Docs/notes.txt", Token);
        Assert.HasCount(1, versions, "The version restored leaves the list");
        Assert.AreEqual(restored.TrashId, versions[0].Id);
        Assert.AreEqual("edited", await ContentOfAsync(versions[0].Id));
        Assert.IsNull(await Trash.FindAsync(version.Id, Token));
        Assert.IsEmpty(
            Directory.GetFiles(Path.GetDirectoryName(file)!, "*.sfe-tmp", SearchOption.TopDirectoryOnly),
            "Nothing left beside the file"
        );
    }

    [TestMethod]
    public async Task AVersionIsNotRestoredOverAReadOnlyFileNorAfterItIsGone()
    {
        var file = WriteText("Docs/notes.txt", "original");
        await SaveAsync("Docs/notes.txt", "edited", "a");
        var version = (await Versions.ListAsync("Docs/notes.txt", Token)).Single();
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var readOnly = await Versions.RestoreOverAsync(version.Id, file, "Docs/notes.txt", Me, Token);

            Assert.AreEqual(FileOperationStatus.Forbidden, readOnly.Status);
            Assert.AreEqual("edited", File.ReadAllText(file));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        await Trash.PurgeAsync(version.Id, null, Token);

        var gone = await Versions.RestoreOverAsync(version.Id, file, "Docs/notes.txt", Me, Token);

        Assert.AreEqual(FileOperationStatus.NotFound, gone.Status);
        Assert.IsNull(await Versions.ContentPathAsync(version.Id, Token));
    }

    [TestMethod]
    public async Task DeletedFilesAreNotVersions()
    {
        WriteText("Docs/notes.txt", "original");
        await _services.Get<FileOperations>().DeleteAsync("Docs/notes.txt", false, Me, Token);
        var deleted = (await Trash.ListAsync(null, null, _ => true, Token)).Items.Single();

        Assert.IsNull(await Versions.ContentPathAsync(deleted.Id, Token));
        Assert.IsEmpty(await Versions.ListAsync("Docs/notes.txt", Token));
    }

    [TestMethod]
    public async Task VersionsFollowTheirFileWhenItIsRenamedOrItsFolderMoved()
    {
        WriteText("Docs/notes.txt", "original");
        await SaveAsync("Docs/notes.txt", "edited", "a");
        var files = _services.Get<FileOperations>();

        var renamed = await files.RenameAsync("Docs/notes.txt", "plans.txt", Me, Token);
        var moved = await files.RenameAsync("Docs", "Papers", Me, Token);

        Assert.AreEqual(FileOperationStatus.Done, renamed.Status, renamed.Message);
        Assert.AreEqual(FileOperationStatus.Done, moved.Status, moved.Message);

        var versions = await Versions.ListAsync("Papers/plans.txt", Token);
        Assert.HasCount(1, versions);
        Assert.AreEqual("original", await ContentOfAsync(versions[0].Id));
        Assert.IsEmpty(await Versions.ListAsync("Docs/notes.txt", Token));
    }

    private async Task SaveAsync(string path, string text, string session)
    {
        var saved = await Content.OverwriteAsync(path, Encoding.UTF8.GetBytes(text), "*", session, true, Me, Token);
        Assert.AreEqual(OverwriteStatus.Done, saved.Status, saved.Message);
    }

    private async Task<string> ContentOfAsync(int id) =>
        File.ReadAllText((await Versions.ContentPathAsync(id, Token))!);

    private string WriteText(string relative, string text)
    {
        var full = _library.FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }
}
