namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// Bounds on what reading an archive costs the server: an archive is a stranger's file, and its directory may claim
/// anything (agents/docs/features/archive-reading.md, "Catalogs").
/// </summary>
public sealed class ArchiveOptions
{
    /// <summary>Entries listed at most; past it the list stops, and says so.</summary>
    public int MaxEntries { get; set; } = 10_000_000;

    /// <summary>Folders nested at most; deeper entries are left out, and listed.</summary>
    public int MaxDepth { get; set; } = 10_000;

    /// <summary>
    /// Largest size an entry may claim against its packed size. Sizes are enforced as declared (an entry giving more is
    /// cut off as broken), so this only refuses absurd claims; deflate itself never passes ~1 030 : 1.
    /// </summary>
    public int MaxCompressionRatio { get; set; } = 10_000;

    /// <summary>
    /// Largest compressed tar (<c>.tar.gz</c>…) explored: it has no directory, listing it decompresses all of it.
    /// </summary>
    public int MaxCompressedTarMegabytes { get; set; } = 2048;

    /// <summary>Archives' lists read at once by the whole server (catalogs built), the others queued.</summary>
    public int CatalogBuilds { get; set; } = 1;

    /// <summary>
    /// Zips written at once by the whole server (compressing keeps a processor busy), the others queued.
    /// </summary>
    public int CompressJobs { get; set; } = 1;

    /// <summary>
    /// Disk the catalogs of a library may take; past it the least recently used are deleted (made again if asked).
    /// </summary>
    public int CatalogMegabytes { get; set; } = 1024;

    /// <summary>Largest entry opened in the viewer from inside an archive; larger ones are only downloaded.</summary>
    public int MaxPreviewMegabytes { get; set; } = 64;

    /// <summary>
    /// Most data unpacked and thrown away to reach one file of a solid 7z or rar, or of a compressed tar (they keep no
    /// way to jump to it): past it the file is only extracted, or the archive downloaded.
    /// </summary>
    public int MaxSkippedMegabytes { get; set; } = 2048;

    /// <summary>
    /// Largest list of entries of a 7z read (its header, unpacked): SharpCompress holds a 7z's list whole, ~570 bytes
    /// per entry, so a larger one is refused rather than read (listed, opened or extracted). 32 MB: ~400 000 entries.
    /// </summary>
    public int MaxSevenZipListMegabytes { get; set; } = Files.Compression.SevenZipList.DefaultMaxMegabytes;

    /// <summary>
    /// Entries listed at most for an archive whose names are encrypted: its list is held in memory (never written to
    /// disk), while its password is.
    /// </summary>
    public int MaxEncryptedListEntries { get; set; } = 500_000;
}
