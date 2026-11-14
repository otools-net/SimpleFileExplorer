namespace Otools.Net.Sfe.Core.Files.Compression;

public enum ArchiveEntryKind
{
    File = 0,
    Folder = 1,

    /// <summary>A symbolic or hard link: listed, never created on extraction.</summary>
    Link = 2,

    /// <summary>A device, a FIFO, a socket: listed, never created.</summary>
    Other = 3,
}
