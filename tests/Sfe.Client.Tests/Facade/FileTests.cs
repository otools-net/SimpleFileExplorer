using Otools.Net.Sfe.Client.Tests.Infrastructure;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary><see cref="SfeFiles"/> against the real server: listing, items, new folders, renames, deletes.</summary>
[TestClass]
public sealed class FileTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AFolderIsListedAPageAtATimeAsItIsRead()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeListOptions { PageSize = 1 };

        var items = await sfe.Files.ListAsync("Media", options, Token).ToListAsync(Token);

        CollectionAssert.AreEqual(
            new[] { "Episode 2.mkv", "Episode 10.mkv", "Holiday 2020.mp4", "photo.jpg" },
            items.Select(item => item.Name).ToArray()
        );
        Assert.AreEqual(SfeItemKind.Video, items[2].Kind);
        Assert.AreEqual(5L * 1024 * 1024, items[2].Size);
        Assert.AreEqual("Media/photo.jpg", items[3].Path);
    }

    [TestMethod]
    public async Task TheRootListsFoldersFirstInTheOrderAsked()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var bySize = new SfeListOptions { Sort = SfeSortField.Size, Descending = true };

        var items = await sfe.Files.ListAsync(SfePath.Root, bySize, Token).ToListAsync(Token);

        var folders = items.TakeWhile(item => item.IsFolder).Count();
        Assert.AreEqual(items.Count(item => item.IsFolder), folders);
        Assert.AreEqual("RFile2", items[folders].Name);
    }

    [TestMethod]
    public async Task AMissingFolderIsNotFound()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(() =>
            sfe.Files.ListAsync("Nowhere", Token).ToListAsync(Token).AsTask()
        );
    }

    [TestMethod]
    public async Task AnItemIsReadByItsPathAndNamesOfAnyKindWork()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var special = $"SpecialDir/{TestLibrary.SpecialFolder}/SFРусский.txt";

        var file = await sfe.Files.GetAsync(special, Token);
        var folder = await sfe.Files.GetAsync("/SpecialDir/", Token);

        Assert.AreEqual("SFРусский.txt", file.Name);
        Assert.AreEqual(10L, file.Size);
        Assert.IsTrue(folder.IsFolder);
        Assert.AreEqual("SpecialDir", folder.Path);
    }

    [TestMethod]
    public async Task ExistsTellsWhetherAnItemIsThere()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        Assert.IsTrue(await sfe.Files.ExistsAsync("100%_done[1].txt", Token));
        Assert.IsTrue(await sfe.Files.ExistsAsync("ChildDir", Token));
        Assert.IsFalse(await sfe.Files.ExistsAsync("ChildDir/Nothing.txt", Token));
    }

    [TestMethod]
    public async Task AFolderIsMadeOnceAndASecondIsAConflict()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var made = await sfe.Files.CreateFolderAsync("ChildDir/New folder", Token);
        var again = await Assert.ThrowsExactlyAsync<SfeConflictException>(() =>
            sfe.Files.CreateFolderAsync("ChildDir/New folder", Token)
        );

        Assert.IsTrue(made.IsFolder);
        Assert.AreEqual("ChildDir/New folder", made.Path);
        Assert.IsTrue(Directory.Exists(server.Library.FullPath("ChildDir/New folder")));
        Assert.AreEqual("file-exists", again.ProblemType);
    }

    [TestMethod]
    public async Task AFileIsRenamedInItsFolder()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var renamed = await sfe.Files.RenameAsync("ChildDir/AFile1.txt", "Renamed.txt", Token);

        Assert.AreEqual("ChildDir/Renamed.txt", renamed.Path);
        Assert.IsTrue(File.Exists(server.Library.FullPath("ChildDir/Renamed.txt")));
        Assert.IsFalse(File.Exists(server.Library.FullPath("ChildDir/AFile1.txt")));
    }

    [TestMethod]
    public async Task DeletedItemsGoToTheTrashWithAResultEach()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var results = await sfe.Files.DeleteAsync(["RFile1.txt", "Nothing.txt"], Token);

        Assert.AreEqual(SfeItemStatus.Done, results[0].Status);
        Assert.IsNotNull(results[0].TrashId);
        Assert.AreEqual(SfeItemStatus.NotFound, results[1].Status);
        Assert.IsFalse(File.Exists(server.Library.FullPath("RFile1.txt")));
    }

    [TestMethod]
    public async Task ItemsDeletedPermanentlySkipTheTrash()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var results = await sfe.Files.DeletePermanentlyAsync(["ChildDir"], Token);

        Assert.AreEqual(SfeItemStatus.Done, results[0].Status);
        Assert.IsNull(results[0].TrashId);
        Assert.IsFalse(Directory.Exists(server.Library.FullPath("ChildDir")));
        Assert.AreEqual(0, (await sfe.Trash.CountAsync(Token)).Count);
    }
}
