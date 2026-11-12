namespace Sfe.Core.Files.Compression;

/// <summary>A folder's page: its folders first, then its files, by name.</summary>
public sealed record ArchivePage(
    IReadOnlyList<ArchiveFolder> Folders,
    IReadOnlyList<ArchiveFile> Files,
    ArchiveCursor? Next
);
