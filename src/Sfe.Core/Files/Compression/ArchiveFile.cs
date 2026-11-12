namespace Sfe.Core.Files.Compression;

/// <summary>An entry of the catalog: where it is (folder, name) and what is needed to open it.</summary>
public sealed record ArchiveFile(long Id, long FolderId, string Name, ArchiveEntry Entry);
