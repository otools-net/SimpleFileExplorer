namespace Sfe.Core.Files.Compression;

/// <param name="Folders">Folders right inside it.</param>
/// <param name="Files">Entries right inside it (files, links…).</param>
/// <param name="AllFiles">Files in its whole tree; <paramref name="Size"/> and <paramref name="Packed"/> are
/// theirs.</param>
public sealed record ArchiveFolder(
    long Id,
    long ParentId,
    string Name,
    int Depth,
    DateTime? Time,
    long Folders,
    long Files,
    long AllFiles,
    long Size,
    long Packed
);
