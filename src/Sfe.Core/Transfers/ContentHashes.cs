using System.Security.Cryptography;

namespace Sfe.Core.Transfers;

/// <summary>
/// The content hashes a transfer verifies a file with, by the name a side gives
/// (<see cref="ITransferEndpoint.HashName"/>): <c>dropbox</c>, <c>sha256</c>, <c>md5</c>.
/// </summary>
public static class ContentHashes
{
    public const string Dropbox = "dropbox";
    public const string Sha256 = "sha256";
    public const string Md5 = "md5";

    /// <summary>A new hash of the kind <paramref name="name"/> names (any case); null for another kind.</summary>
    public static IncrementalContentHash? Create(string? name) =>
        name?.ToLowerInvariant() switch
        {
            Dropbox => new DropboxContentHash(),
            Sha256 => new PlainContentHash(HashAlgorithmName.SHA256),
            Md5 => new PlainContentHash(HashAlgorithmName.MD5),
            _ => null,
        };

    /// <summary>One hash of the whole content.</summary>
    private sealed class PlainContentHash(HashAlgorithmName algorithm) : IncrementalContentHash
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(algorithm);

        public override void Append(ReadOnlySpan<byte> data) => _hash.AppendData(data);

        public override string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        protected override void Dispose(bool disposing) => _hash.Dispose();
    }

    /// <summary>
    /// Dropbox's <c>content_hash</c> (https://www.dropbox.com/developers/reference/content-hash): the SHA-256 of each
    /// 4 MiB block (the last one shorter), then the SHA-256 of those hashes put end to end. An empty file has no block:
    /// the SHA-256 of nothing.
    /// </summary>
    private sealed class DropboxContentHash : IncrementalContentHash
    {
        private const int BlockSize = 4 << 20;

        private readonly IncrementalHash _block = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly IncrementalHash _blocks = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private int _inBlock;

        public override void Append(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var taken = Math.Min(BlockSize - _inBlock, data.Length);
                _block.AppendData(data[..taken]);
                _inBlock += taken;
                data = data[taken..];
                if (_inBlock == BlockSize)
                    EndBlock();
            }
        }

        public override string Finish()
        {
            if (_inBlock > 0)
                EndBlock();
            return Convert.ToHexStringLower(_blocks.GetHashAndReset());
        }

        protected override void Dispose(bool disposing)
        {
            _block.Dispose();
            _blocks.Dispose();
        }

        private void EndBlock()
        {
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            _block.GetHashAndReset(hash);
            _blocks.AppendData(hash);
            _inBlock = 0;
        }
    }
}
