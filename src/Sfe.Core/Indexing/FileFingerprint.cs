using System.Security.Cryptography;

namespace Sfe.Core.Indexing;

/// <summary>
/// Cheap content fingerprint used to spot duplicates and moved files: SHA1 of the first megabyte. Files are compared
/// by size and fingerprint, so a difference after the first megabyte goes unnoticed: removing a duplicate compares
/// the whole files first.
/// </summary>
public static class FileFingerprint
{
    public const int ChunkSize = 1024 * 1024;

    public static async Task<string> ComputeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.Asynchronous);
        var buffer = new byte[ChunkSize];
        var read = await stream.ReadAtLeastAsync(buffer, ChunkSize, throwOnEndOfStream: false, cancellationToken);
        return Convert.ToHexString(SHA1.HashData(buffer.AsSpan(0, read)));
    }
}
