namespace Otools.Net.Sfe.Core.Files.Compression;

/// <summary>
/// Where a search stopped: in the folders or the files, after this id (what was looked through, not only found).
/// </summary>
public sealed record ArchiveSearchCursor(bool InFiles, long Id);
