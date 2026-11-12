namespace Sfe.Core.Files.Compression;

public sealed record ArchiveCatalogState(
    ArchiveCatalogStatus Status,
    ArchiveCatalog? Catalog = null,
    long Read = 0,
    string? Error = null
);
