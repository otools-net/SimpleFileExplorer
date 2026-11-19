using System.Security.Cryptography;
using Otools.Net.Sfe.Core.Transfers;

namespace Otools.Net.Sfe.Tests;

/// <summary>Content hashes computed while a file streams (<see cref="ContentHashes"/>).</summary>
[TestClass]
public sealed class ContentHashTests
{
    private const int Block = 4 * 1024 * 1024;

    /// <summary>
    /// Dropbox's hash of the 9 MiB + 3 bytes <c>i % 251</c>, computed apart from this code by the documented rule
    /// (Python's hashlib: the SHA-256 of each 4 MiB block, then of the block hashes end to end).
    /// </summary>
    private const string ThreeBlocks = "59dc104be0c1678117add4f125c431457d095e0c00c0a9dd42d93d420c18ce63";

    private const string OneFullBlock = "b9654428408015906b44a00935b70af33830aa344b780b0eabd535a133150d04";

    [TestMethod]
    public void AnEmptyFileHasTheHashOfNothing()
    {
        const string Nothing = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        Assert.AreEqual(Nothing, Hash(ContentHashes.Dropbox, [], [0]));
        Assert.AreEqual(Nothing, Hash(ContentHashes.Sha256, [], [0]));
        Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", Hash(ContentHashes.Md5, [], [0]));
    }

    [TestMethod]
    public void UnderOneBlockIsTheHashOfTheBlocksHash()
    {
        var data = Content(1000);

        Assert.AreEqual(Rule(data), Hash(ContentHashes.Dropbox, data, [data.Length]));
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(data)), Hash("SHA256", data, [7, 993]));
        Assert.AreEqual(Convert.ToHexStringLower(MD5.HashData(data)), Hash(ContentHashes.Md5, data, [500, 500]));
    }

    [TestMethod]
    public void ExactlyOneBlockAndOneByteMore()
    {
        var one = Content(Block);
        var more = Content(Block + 1);

        Assert.AreEqual(OneFullBlock, Hash(ContentHashes.Dropbox, one, [Block]));
        Assert.AreEqual(Rule(more), Hash(ContentHashes.Dropbox, more, [Block + 1]));
        Assert.AreNotEqual(Rule(one), Rule(more));
    }

    [TestMethod]
    public void PartsOfAnySizeGiveTheSameHash()
    {
        var data = Content(9 * 1024 * 1024 + 3);

        Assert.AreEqual(ThreeBlocks, Hash(ContentHashes.Dropbox, data, [data.Length]));
        Assert.AreEqual(ThreeBlocks, Hash(ContentHashes.Dropbox, data, [1, Block - 2, 3, 65_537, 1_000_003]));
        Assert.AreEqual(ThreeBlocks, Hash(ContentHashes.Dropbox, data, [Block, Block, 3]));
        Assert.AreEqual(ThreeBlocks, Rule(data), "The test's own rule agrees");
    }

    [TestMethod]
    public void AnUnknownKindHasNoHash()
    {
        Assert.IsNull(ContentHashes.Create("quickxor"));
        Assert.IsNull(ContentHashes.Create(null));
    }

    /// <summary>
    /// The hash of <paramref name="data"/> fed in parts of the sizes given, the last one repeated to the end.
    /// </summary>
    private static string Hash(string name, byte[] data, int[] parts)
    {
        using var hash = ContentHashes.Create(name)!;
        var offset = 0;
        for (var i = 0; offset < data.Length; i++)
        {
            var size = Math.Min(parts[Math.Min(i, parts.Length - 1)], data.Length - offset);
            hash.Append(data.AsSpan(offset, size));
            offset += size;
        }
        return hash.Finish();
    }

    /// <summary>Dropbox's rule, written plainly.</summary>
    private static string Rule(byte[] data)
    {
        var blocks = new List<byte>();
        for (var offset = 0; offset < data.Length; offset += Block)
            blocks.AddRange(SHA256.HashData(data.AsSpan(offset, Math.Min(Block, data.Length - offset))));
        return Convert.ToHexStringLower(SHA256.HashData(blocks.ToArray()));
    }

    private static byte[] Content(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
            data[i] = (byte)(i % 251);
        return data;
    }
}
