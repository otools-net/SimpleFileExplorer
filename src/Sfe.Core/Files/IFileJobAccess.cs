namespace Sfe.Core.Files;

/// <summary>
/// What the person who started a job may do, asked again for every item as it runs (rights and folder rules can change
/// meanwhile): the routes implement it with the person's current access.
/// </summary>
public interface IFileJobAccess
{
    /// <summary>The person may use <paramref name="right"/> at the root-relative <paramref name="path"/>.</summary>
    Task<bool> MayAsync(string path, string right);

    /// <summary>
    /// The person may use <paramref name="right"/> at the folder <paramref name="path"/> and everywhere below it.
    /// </summary>
    Task<bool> MayThroughoutAsync(string path, string right);

    /// <summary>
    /// The full path really is (through links) among the server's own files or a work folder: never written to.
    /// </summary>
    bool IsProtected(string fullPath);

    /// <summary>
    /// The full path may be read but nothing written, moved away or deleted there: <see cref="IsProtected"/>, or a
    /// place outside the library, reached through a link, on a server that only reads through those.
    /// </summary>
    bool IsReadOnly(string fullPath);

    /// <summary>
    /// The person's access now, for a walk through folders (a compression: <c>files.view</c> and <c>files.download</c>
    /// at each); null when they have none any more.
    /// </summary>
    Task<IArchiveAccess?> ArchiveAccessAsync() => Task.FromResult<IArchiveAccess?>(null);

    /// <summary>
    /// Why the job may not go on with its next items (the access token that started it was revoked); null while it
    /// may.
    /// </summary>
    Task<string?> StoppedAsync() => Task.FromResult<string?>(null);
}
