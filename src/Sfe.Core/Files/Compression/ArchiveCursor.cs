namespace Sfe.Core.Files.Compression;

/// <summary>
/// Where a page stopped: in the folders or the files, after this name and id (keyset, never an offset).
/// </summary>
public sealed record ArchiveCursor(bool InFiles, string Name, long Id);
