namespace Sfe.Core.Files.Compression;

/// <summary>
/// A page of a search by name: the folders, then the files, whose name holds the text, in the archive's order, each
/// with the path of the folder it is in. <c>Next</c> is null once the whole catalog was looked through.
/// </summary>
public sealed record ArchiveSearchPage(
    IReadOnlyList<(ArchiveFolder Folder, string Where)> Folders,
    IReadOnlyList<(ArchiveFile File, string Where)> Files,
    ArchiveSearchCursor? Next
);
