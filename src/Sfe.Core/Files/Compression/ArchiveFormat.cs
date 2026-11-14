namespace Otools.Net.Sfe.Core.Files.Compression;

/// <summary>The archives SFE reads (agents/docs/features/archive-reading.md, "Formats").</summary>
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
