namespace Sfe.Core.Transfers;

/// <summary>A file or folder on one side of a transfer (the library, or a connection).</summary>
/// <param name="Path">Relative to that side's root (the library's, or the connection's root folder).</param>
/// <param name="Size">Bytes of a file; null for a folder, or when the side doesn't tell.</param>
/// <param name="Modified">UTC; null when the side keeps none (Dropbox folders).</param>
/// <param name="Hash">The side's content hash, of the kind <see cref="ITransferEndpoint.HashName"/> names.</param>
/// <param name="Downloadable">False for what has no content to read (an online document): left out.</param>
public sealed record TransferEntry(
    string Name,
    string Path,
    bool IsFolder,
    long? Size = null,
    DateTime? Modified = null,
    string? Hash = null,
    bool Downloadable = true
);

/// <summary>Where a transfer reads or writes: the library (no connection) or a connection, and a path there.</summary>
public sealed record TransferPlace(int? ConnectionId, string Path)
{
    public bool IsLibrary => ConnectionId is null;
}

/// <summary>One side's free space for a transfer: null bytes when it doesn't tell.</summary>
public sealed record TransferSpace(long? FreeBytes);
