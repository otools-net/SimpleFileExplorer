namespace Sfe.Core.Files;

public enum PropertyJobKind
{
    /// <summary>Counts a folder's files, folders and bytes on the disk.</summary>
    Count,

    /// <summary>Reads a file whole for its checksum.</summary>
    Checksum,
}
