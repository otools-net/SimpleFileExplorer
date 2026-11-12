namespace Sfe.Core.Files;

/// <summary>What the person may do, asked when an upload starts and again when it is finished.</summary>
public interface IUploadAccess
{
    /// <summary>
    /// The person may use <paramref name="right"/> at the root-relative <paramref name="path"/> (there or not yet).
    /// </summary>
    Task<bool> MayAsync(string path, string right);

    /// <summary>
    /// The full path really is (through links) among the server's own files or a work folder: never written to.
    /// </summary>
    bool IsProtected(string fullPath);
}
