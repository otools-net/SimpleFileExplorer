namespace Otools.Net.Sfe.Core.Files;

/// <summary>What a chunk did: the upload as it is now, or why it was refused.</summary>
/// <param name="Damaged">The chunk's bytes didn't match its digest (<see cref="ChunkDigest"/>): send it again.</param>
public sealed record UploadChunkResult(
    FileOperationStatus Status,
    UploadInfo? Upload,
    string? Message = null,
    bool Damaged = false
);
