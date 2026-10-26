using Sfe.Core.Options;

namespace Sfe.Core.Files.Compression;

/// <summary>The archives SFE reads (agents/docs/development/compressed-archives-plan.md, "Formats").</summary>
public enum ArchiveFormat
{
    Zip,
    SevenZip,
    Rar,
    Tar,
    TarGzip,
    TarBzip2,
    TarXz,
    /// <summary>One file compressed alone (<c>.gz</c>, <c>.bz2</c>, <c>.xz</c>): extracted, never explored.</summary>
    Single,
}

/// <summary>
/// Which archive a file is: from its name, then checked against its first bytes (a renamed file is what its bytes
/// say, and a file whose bytes are no archive is none).
/// </summary>
public static class ArchiveFormats
{
    /// <summary>The bytes <see cref="Detect"/> reads: tar's "ustar" mark sits at 257.</summary>
    private const int HeaderBytes = 512;

    private static readonly byte[] SevenZipMark = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] GzipMark = [0x1F, 0x8B];
    private static readonly byte[] XzMark = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];

    private static readonly (string Suffix, ArchiveFormat Format)[] Suffixes =
    [
        (".tar.gz", ArchiveFormat.TarGzip), (".tgz", ArchiveFormat.TarGzip),
        (".tar.bz2", ArchiveFormat.TarBzip2), (".tbz2", ArchiveFormat.TarBzip2), (".tbz", ArchiveFormat.TarBzip2),
        (".tar.xz", ArchiveFormat.TarXz), (".txz", ArchiveFormat.TarXz),
        (".tar", ArchiveFormat.Tar),
        (".zip", ArchiveFormat.Zip),
        (".7z", ArchiveFormat.SevenZip),
        (".rar", ArchiveFormat.Rar),
        (".gz", ArchiveFormat.Single), (".bz2", ArchiveFormat.Single), (".xz", ArchiveFormat.Single),
    ];

    /// <summary>The format a name claims; null for a name of no archive.</summary>
    public static ArchiveFormat? OfName(string name)
    {
        foreach (var (suffix, format) in Suffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                return format;
        return null;
    }

    /// <summary>
    /// Whether the archive viewer is offered for a file (the client applies the same rule): never for one file
    /// compressed alone, and a compressed tar only up to <see cref="ArchiveOptions.MaxCompressedTarMegabytes"/>
    /// (it has no list of entries: listing it decompresses all of it).
    /// </summary>
    public static bool IsExplorable(ArchiveFormat format, long size, ArchiveOptions options) => format switch
    {
        ArchiveFormat.Single => false,
        ArchiveFormat.TarGzip or ArchiveFormat.TarBzip2 or ArchiveFormat.TarXz =>
            size <= options.MaxCompressedTarMegabytes * 1024L * 1024,
        _ => true,
    };

    /// <summary>
    /// The format of the file at <paramref name="fullPath"/>: its first bytes decide the family, its name tells a
    /// compressed tar from one file compressed alone. Null when the bytes are no archive SFE reads, or don't match what
    /// the name claims a compressed tar is (a <c>.zip</c> holding 7z bytes is a 7z).
    /// </summary>
    public static ArchiveFormat? Detect(string fullPath)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        int read;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            read = stream.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false);
        return Detect(header[..read], Path.GetFileName(fullPath), new FileInfo(fullPath).Length);
    }

    internal static ArchiveFormat? Detect(ReadOnlySpan<byte> header, string name, long length)
    {
        var claimed = OfName(name);
        if (header.StartsWith("PK\x03\x04"u8) || header.StartsWith("PK\x05\x06"u8))
            return ArchiveFormat.Zip;
        if (header.StartsWith(SevenZipMark))
            return ArchiveFormat.SevenZip;
        if (header.StartsWith("Rar!\x1A\x07"u8))
            return ArchiveFormat.Rar;
        if (header.StartsWith(GzipMark))
            return claimed == ArchiveFormat.TarGzip ? ArchiveFormat.TarGzip : ArchiveFormat.Single;
        if (header.StartsWith("BZh"u8))
            return claimed == ArchiveFormat.TarBzip2 ? ArchiveFormat.TarBzip2 : ArchiveFormat.Single;
        if (header.StartsWith(XzMark))
            return claimed == ArchiveFormat.TarXz ? ArchiveFormat.TarXz : ArchiveFormat.Single;
        if (header.Length >= 262 && header[257..262].SequenceEqual("ustar"u8))
            return ArchiveFormat.Tar;
        // Old tars (v7) carry no mark: trusted by name when made of whole 512-byte blocks.
        if (claimed == ArchiveFormat.Tar && length > 0 && length % 512 == 0)
            return ArchiveFormat.Tar;
        return null;
    }
}
