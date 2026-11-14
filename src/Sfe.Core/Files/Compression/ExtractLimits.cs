namespace Otools.Net.Sfe.Core.Files.Compression;

/// <param name="MaxDepth">Folders nested at most (<see cref="Options.ArchiveOptions.MaxDepth"/>).</param>
/// <param name="MaxEntries">Entries read at most; past it the extraction stops and says so.</param>
/// <param name="MaxCompressionRatio">A file claiming more than this many times its packed size is left out.</param>
/// <param name="PortableNames">Names Windows refuses are left out on other systems too
/// (<see cref="FileNames.Check"/>).</param>
/// <param name="MaxFullPath">Longest full path written (the index's limit), or none.</param>
public sealed record ExtractLimits(
    int MaxDepth,
    long MaxEntries,
    double MaxCompressionRatio,
    bool PortableNames,
    int? MaxFullPath = null,
    long MaxSevenZipList = SevenZipList.DefaultMaxMegabytes * 1024L * 1024
);
