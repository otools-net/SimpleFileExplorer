using System.Security.Cryptography;
using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary><see cref="SfeProperties"/> against the real server: details, index totals, counts, checksums.</summary>
[TestClass]
public sealed class PropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AFilesPropertiesTellItsDatesAndWhetherItCanBeChecked()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var properties = await sfe.Properties.GetAsync("RFile2", Token);

        Assert.AreEqual("RFile2", properties.Item.Path);
        Assert.AreEqual(2048L, properties.Item.Size);
        Assert.AreEqual(
            File.GetLastWriteTimeUtc(server.Library.FullPath("RFile2")),
            properties.Details.Modified.UtcDateTime
        );
        Assert.IsFalse(properties.Details.IsLink);
        Assert.IsTrue(properties.CanChecksum);
    }

    [TestMethod]
    public async Task AFolderNotIndexedHasNoTotals()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var totals = await sfe.Properties.GetTotalsAsync("ChildDir", Token);

        Assert.IsFalse(totals.Indexed);
        Assert.AreEqual(0L, totals.Files);
    }

    [TestMethod]
    public async Task AFolderIsCountedOnTheDisk()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var reported = new List<SfeFolderCount>();

        var count = await sfe.Properties.CountAsync("RepeatedDir", new SyncProgress<SfeFolderCount>(reported), Token);

        Assert.AreEqual(6L, count.Files);
        Assert.AreEqual(2L, count.Folders);
        Assert.AreEqual(60L, count.Bytes);
        Assert.IsNotEmpty(reported);
    }

    [TestMethod]
    [DataRow(SfeChecksumAlgorithm.Sha256)]
    [DataRow(SfeChecksumAlgorithm.Md5)]
    public async Task AFilesChecksumIsTheOneOfItsBytes(SfeChecksumAlgorithm algorithm)
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var bytes = await File.ReadAllBytesAsync(server.Library.FullPath("Media/photo.jpg"), Token);
        var expected = algorithm == SfeChecksumAlgorithm.Md5 ? MD5.HashData(bytes) : SHA256.HashData(bytes);

        var checksum = await sfe.Properties.ChecksumAsync("Media/photo.jpg", algorithm, cancellationToken: Token);

        Assert.AreEqual(Convert.ToHexStringLower(expected), checksum);
    }
}
