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
        new($"{bytes.Length:x}-{modified.Ticks:x}-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..16]}", bytes.Length, modified);
}

/// <param name="Bytes">The content; null when the file is larger than asked (<see cref="TooLarge"/>).</param>
/// <param name="Version">Null when the file was too large to be read.</param>
public sealed record FileRead(byte[]? Bytes, FileVersion? Version, long Size, DateTime Modified)
{
    public bool TooLarge => Bytes is null;
}

/// <summary>
/// Reads (and, to come, writes) the content of the library's files for the viewers and editors of the web client
/// (agents/docs/development/file-viewers-plan.md): one place for the size limits and versions, whatever the format.
/// Paths are full and already checked by the routes (rights, folder rules, server files).
/// </summary>
public sealed class FileContent
{
    /// <summary>
    /// The content of <paramref name="full"/> when it is at most <paramref name="maxBytes"/> long. A file still being
    /// written (a log) is read as it is now; null when it is not there.
    /// </summary>
    public async Task<FileRead?> ReadAsync(string full, long maxBytes, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        await using (stream)
        {
            var modified = File.GetLastWriteTimeUtc(full);
            var length = stream.Length;
            if (length > maxBytes)
                return new FileRead(null, null, length, modified);
            // It may grow while being read: read what fits, one byte more tells it went past the limit.
            var buffer = new byte[Math.Min(length + 1, maxBytes + 1)];
            var read = 0;
            int count;
            while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
                read += count;
            if (read > maxBytes)
                return new FileRead(null, null, read, modified);
            var bytes = read == buffer.Length ? buffer : buffer[..read];
            return new FileRead(bytes, FileVersion.Of(bytes, modified), read, modified);
        }
    }
}
