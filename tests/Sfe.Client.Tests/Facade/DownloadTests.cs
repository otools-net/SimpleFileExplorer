using System.IO.Compression;
using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>Files read from the real server: whole, in part, going on after a cut-off, as a stream, as a zip.</summary>
[TestClass]
public sealed class DownloadTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AFileIsDownloadedWhole()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        using var destination = new MemoryStream();
        var reported = new List<long>();

        var written = await sfe.Files.DownloadAsync(
            "100%_done[1].txt",
            destination,
            new SfeDownloadOptions { Progress = new SyncProgress<long>(reported) },
            Token
        );

        var expected = await File.ReadAllBytesAsync(server.Library.FullPath("100%_done[1].txt"), Token);
        Assert.AreEqual(expected.LongLength, written);
        CollectionAssert.AreEqual(expected, destination.ToArray());
        Assert.AreEqual(written, reported[^1]);
    }

    [TestMethod]
    public async Task APartOfAFileIsDownloadedAndTheRestLater()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        using var destination = new MemoryStream();
        var expected = await File.ReadAllBytesAsync(server.Library.FullPath("Media/photo.jpg"), Token);

        var first = await sfe.Files.DownloadAsync(
            "Media/photo.jpg",
            destination,
            new SfeDownloadOptions { Length = 1000 },
            Token
        );
        var rest = await sfe.Files.DownloadAsync(
            "Media/photo.jpg",
            destination,
            new SfeDownloadOptions { Offset = destination.Length },
            Token
        );

        Assert.AreEqual(1000, first);
        Assert.AreEqual(expected.Length - 1000, rest);
        CollectionAssert.AreEqual(expected, destination.ToArray());
    }

    [TestMethod]
    public async Task ADownloadCutOffGoesOnWhereItStopped()
    {
        using var server = await TokenServer.StartAsync(Token);
        CuttingHandler? cutter = null;
        using var sfe = server.CreateClient(
            options => options.RetryDelay = TimeSpan.FromMilliseconds(1),
            inner =>
                cutter = new CuttingHandler(inner, request => request.RequestUri!.AbsolutePath.Contains("raw"), 100_000)
        );
        using var destination = new MemoryStream();

        var written = await sfe.Files.DownloadAsync("Media/Episode 10.mkv", destination, cancellationToken: Token);

        var expected = await File.ReadAllBytesAsync(server.Library.FullPath("Media/Episode 10.mkv"), Token);
        Assert.AreEqual(expected.LongLength, written);
        CollectionAssert.AreEqual(expected, destination.ToArray());
        var resumed = cutter!.Requests[^1];
        Assert.AreEqual(100_000, resumed.Headers.Range?.Ranges.Single().From);
        Assert.IsNotNull(resumed.Headers.IfRange?.Date);
    }

    [TestMethod]
    public async Task AFileIsOpenedAsAStreamFromAnOffset()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var expected = await File.ReadAllBytesAsync(server.Library.FullPath("RFile2"), Token);

        await using var stream = await sfe.Files.OpenReadAsync("RFile2", 100, Token);
        using var read = new MemoryStream();
        await stream.CopyToAsync(read, Token);

        CollectionAssert.AreEqual(expected[100..], read.ToArray());
    }

    [TestMethod]
    public async Task AMissingFileIsNotFound()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(() =>
            sfe.Files.DownloadAsync("Nothing.txt", Stream.Null, cancellationToken: Token)
        );
    }

    [TestMethod]
    public async Task SeveralItemsAreDownloadedAsOneZip()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        using var destination = new MemoryStream();

        var name = await sfe.Archives.DownloadZipAsync(
            ["ChildDir", "RFile1.txt"],
            destination,
            cancellationToken: Token
        );

        destination.Position = 0;
        using var zip = new ZipArchive(destination, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        Assert.EndsWith(".zip", name);
        Assert.Contains("RFile1.txt", entries);
        Assert.Contains("ChildDir/AFile1.txt", entries);
    }
}
