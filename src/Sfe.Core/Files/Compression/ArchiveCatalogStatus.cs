namespace Otools.Net.Sfe.Core.Files.Compression;

public enum ArchiveCatalogStatus
{
    Ready,

    /// <summary>Being read (or waiting for its turn): <see cref="ArchiveCatalogState.Read"/> entries so far.</summary>
    Building,

    /// <summary>
    /// Couldn't be read (the disk, the file in use): <see cref="ArchiveCatalogState.Error"/> says so; asked again
    /// later.
    /// </summary>
    Failed,

    /// <summary>
    /// Not read yet, and no new read was started for this caller (<c>mayStart</c> said no): ask again later.
    /// </summary>
    Limited,
}
