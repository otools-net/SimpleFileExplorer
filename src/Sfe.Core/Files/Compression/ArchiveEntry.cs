namespace Sfe.Core.Files.Compression;

public enum ArchiveEntryKind
{
    File = 0,
    Folder = 1,
    /// <summary>A symbolic or hard link: listed, never created on extraction.</summary>
    Link = 2,
    /// <summary>A device, a FIFO, a socket: listed, never created.</summary>
    Other = 3,
}

/// <summary>
/// One entry as an archive's directory lists it, before its name is checked (<see cref="ArchiveEntryNames"/>).
/// </summary>
/// <param name="Name">The name as written in the archive (any separator, maybe hostile).</param>
/// <param name="Time">The modified time as stored (zip: local time of the machine that made it).</param>
/// <param name="Locator">Where the entry is: zip, the offset of its local header; other formats, its position in
/// the archive's order.</param>
/// <param name="Method">Zip's compression method (0 stored, 8 deflate…); 0 for other formats.</param>
public sealed record ArchiveEntry(
    string Name,
    ArchiveEntryKind Kind,
    long Size,
    long Packed,
    DateTime? Time,
    bool Encrypted,
    long Locator,
    int Method = 0);
