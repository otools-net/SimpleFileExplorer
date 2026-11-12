using System.Security.Cryptography;

namespace Sfe.Core.Files;

/// <summary>
/// The version of a file's content as it was read: changes when its bytes do. Not the modification time alone: FAT
/// keeps it to 2 s, and copy tools and network shares keep the old one.
/// </summary>
/// <param name="Token">Size, time and a hash of the bytes, compared as a whole (an HTTP entity tag).</param>
public sealed record FileVersion(string Token, long Size, DateTime Modified)
{
    public static FileVersion Of(ReadOnlySpan<byte> bytes, DateTime modified) =>
        new(
            $"{bytes.Length:x}-{modified.Ticks:x}-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..16]}",
            bytes.Length,
            modified
        );

    /// <summary>The version of the file as it is now on the disk (read whole, without holding it in memory).</summary>
    public static async Task<FileVersion> OfFileAsync(string full, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            full,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            useAsync: true
        );
        var modified = File.GetLastWriteTimeUtc(full);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken))[..16];
        return new FileVersion($"{stream.Length:x}-{modified.Ticks:x}-{hash}", stream.Length, modified);
    }
}
