using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// <see cref="SfeTrash"/> against the real server: listed, counted, restored from, deleted from, emptied.
/// </summary>
[TestClass]
public sealed class TrashTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task DeletedItemsAreListedAndCountedNewestFirst()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        await sfe.Files.DeleteAsync(["RFile1.txt"], Token);
        await sfe.Files.DeleteAsync(["ChildDir"], Token);

        var items = await sfe.Trash.ListAsync(Token).ToListAsync(Token);
        var count = await sfe.Trash.CountAsync(Token);

        CollectionAssert.AreEqual(new[] { "ChildDir", "RFile1.txt" }, items.Select(item => item.Path).ToArray());
        Assert.IsTrue(items[0].IsFolder);
        Assert.AreEqual(2, count.Count);
        Assert.IsFalse(count.More);
    }

    [TestMethod]
    public async Task ADeletedFileIsRestoredWhereItWas()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var deleted = await sfe.Files.DeleteAsync(["ChildDir/AFile1.txt"], Token);

        var restored = await sfe.Trash.RestoreAsync(deleted[0].TrashId!.Value, Token);

        Assert.AreEqual("ChildDir/AFile1.txt", restored.Path);
        Assert.IsTrue(File.Exists(server.Library.FullPath("ChildDir/AFile1.txt")));
    }

    [TestMethod]
    public async Task AnItemOfTheTrashIsDeletedForGood()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var deleted = await sfe.Files.DeleteAsync(["RFile1.txt", "RFile2"], Token);

        await sfe.Trash.DeleteAsync(deleted[0].TrashId!.Value, Token);

        var left = await sfe.Trash.ListAsync(Token).ToListAsync(Token);
        Assert.AreEqual("RFile2", left.Single().Path);
        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(() =>
            sfe.Trash.RestoreAsync(deleted[0].TrashId!.Value, Token)
        );
    }

    [TestMethod]
    public async Task TheTrashIsEmptied()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        await sfe.Files.DeleteAsync(["RFile1.txt", "RFile2", "ChildDir"], Token);

        var purged = await sfe.Trash.EmptyAsync(Token);

        Assert.AreEqual(3, purged);
        Assert.AreEqual(0, (await sfe.Trash.CountAsync(Token)).Count);
    }
}
