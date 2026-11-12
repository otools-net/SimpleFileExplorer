namespace Sfe.Core.Files.Compression;

/// <summary>What a catalog holds, and whether its list is whole.</summary>
/// <param name="Complete">False when the read stopped (a limit, a broken archive): the view says why, from
/// <paramref name="Stopped"/>, and never shows the part read as the whole archive.</param>
/// <param name="NeedsPassword">The read stopped at a password (the archive's list is encrypted).</param>
public sealed record ArchiveCatalogSummary(
    ArchiveFormat Format,
    long Entries,
    long Folders,
    long LeftOut,
    bool Complete,
    string? Stopped,
    bool NeedsPassword = false
);
