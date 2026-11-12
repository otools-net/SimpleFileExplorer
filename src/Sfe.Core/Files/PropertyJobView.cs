namespace Sfe.Core.Files;

/// <param name="Bytes">Counted (a count), or read so far (a checksum).</param>
/// <param name="Total">The file's size, for a checksum's progress.</param>
/// <param name="LeftOut">Links not followed and folders that couldn't be read, while counting.</param>
/// <param name="Capped">The count stopped at <see cref="PropertyJobs.MaxCountedEntries"/>: the folder holds
/// more.</param>
/// <param name="Checksum">The checksum, in lowercase hex, once done.</param>
public sealed record PropertyJobView(
    string Id,
    PropertyJobKind Kind,
    string Path,
    ChecksumAlgorithm? Algorithm,
    bool Done,
    long Files,
    long Folders,
    long Bytes,
    long? Total,
    long LeftOut,
    bool Capped,
    string? Checksum,
    string? Error
);
