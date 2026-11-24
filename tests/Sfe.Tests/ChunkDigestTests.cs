using System.Security.Cryptography;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Tests;

/// <summary>The <c>Content-Digest</c> of an upload's chunk (<see cref="ChunkDigest"/>).</summary>
[TestClass]
public sealed class ChunkDigestTests
{
    private static readonly byte[] Content = "hello"u8.ToArray();

    [TestMethod]
    public void TheSha256IsReadAmongOtherAlgorithms()
    {
        var value = ChunkDigest.Of(Content);
        var expected = SHA256.HashData(Content);
        var upperCase = $"SHA-256{value["sha-256".Length..]}";

        var readAlone = ChunkDigest.TryParse(value, out var alone);
        var readAmong = ChunkDigest.TryParse($"sha-512=:AAAA:, {upperCase}", out var among);

        Assert.IsTrue(readAlone);
        Assert.IsTrue(readAmong);
        CollectionAssert.AreEqual(expected, alone);
        CollectionAssert.AreEqual(expected, among);
        Assert.IsTrue(ChunkDigest.Matches(alone!, expected));
        Assert.IsFalse(ChunkDigest.Matches(alone!, SHA256.HashData("hellO"u8)));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("sha-512=:AAAA:")]
    public void NoSha256MeansAChunkTakenUnchecked(string? header)
    {
        Assert.IsTrue(ChunkDigest.TryParse(header, out var sha256));
        Assert.IsNull(sha256);
    }

    [TestMethod]
    [DataRow("sha-256=:not base64:")]
    [DataRow("sha-256=AAAA")]
    [DataRow("sha-256=:AAAA:")]
    public void ASha256ThatCantBeReadIsRefused(string header) => Assert.IsFalse(ChunkDigest.TryParse(header, out _));
}
