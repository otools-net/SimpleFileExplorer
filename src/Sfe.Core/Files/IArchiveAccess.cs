namespace Otools.Net.Sfe.Core.Files;

/// <summary>The access of the person downloading, asked at each item and each folder of the zip.</summary>
public interface IArchiveAccess
{
    /// <summary>
    /// Whether the root-relative <paramref name="path"/> may go in the zip (rights <c>files.view</c> and
    /// <c>files.download</c>).
    /// </summary>
    ArchiveVerdict Check(string path);

    /// <summary>
    /// The full path really is (through links) among the server's own files or a work folder: never served.
    /// </summary>
    bool IsProtected(string fullPath);

    /// <summary>
    /// As <see cref="IsProtected"/>, for a path with no link on the way (already resolved): no disk access.
    /// </summary>
    bool IsProtectedReal(string realPath);
}
