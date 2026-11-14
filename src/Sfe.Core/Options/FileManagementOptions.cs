namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// Managing the library's files from the web client: new folders, renames (and, later, moves, the trash, uploads).
/// </summary>
public sealed class FileManagementOptions
{
    /// <summary>
    /// New names must also be valid on Windows (no <c>&lt; &gt; : " \ | ? *</c>, no name ending with a dot or a space,
    /// no <c>CON</c>, <c>NUL</c>…), even on a Linux server: libraries are often shared with Windows computers or moved
    /// to one. Off, a Linux server only refuses what Linux itself refuses. Windows servers always refuse them.
    /// </summary>
    public bool PortableNames { get; set; } = true;

    /// <summary>
    /// Where the work files of file management go (<see cref="WorkFolderLocation"/>). The server's since 1.82.0; the
    /// libraries of earlier installs kept the library's
    /// (<see cref="Upgrades.Steps.V1_82_0.KeepWorkFolderInLibraries"/>).
    /// </summary>
    public WorkFolderLocation WorkFolder { get; set; } = WorkFolderLocation.Server;

    /// <summary>
    /// Every delete skips the trash: the files are gone at once, and deleting needs the right to delete permanently.
    /// Off by default. What is already in the trash stays there (restore and purge still work).
    /// </summary>
    public bool DeletePermanently { get; set; }

    public TrashOptions Trash { get; set; } = new();

    /// <summary>
    /// A copy is refused when it would leave less free space than this on the target's drive (megabytes).
    /// </summary>
    public int MinimumFreeSpaceMegabytes { get; set; } = 1024;

    public UploadOptions Uploads { get; set; } = new();

    /// <summary>
    /// The largest zip of several files or a folder one may download (megabytes, as the index counts it); 0 = no limit.
    /// </summary>
    public int MaxArchiveMegabytes { get; set; }

    /// <summary>Exploring and extracting archives kept in the library (zip, 7z, rar, tar).</summary>
    public ArchiveOptions Archives { get; set; } = new();
}
