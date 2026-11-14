namespace Otools.Net.Sfe.Core.Files;

/// <summary>What a chunk did: the upload as it is now, or why it was refused.</summary>
public sealed record UploadChunkResult(FileOperationStatus Status, UploadInfo? Upload, string? Message = null);
