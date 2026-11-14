using Sfe.Core.Access;
using Sfe.Core.Connections;

namespace Sfe.Core.Transfers;

/// <summary>
/// The folder rules of a connection at each item of a transfer (<see cref="RemoteTransferEndpoint"/>), checked
/// before the provider is called: what the person doesn't see is not found, what it sees but may not change is
/// refused, a name hidden from it is taken. The access is read again at each check.
/// </summary>
internal sealed class RemoteTransferChecks(IConnectionJobAccess access, string right)
{
    /// <summary>The person's access now; refused once they may no longer use the connection.</summary>
    public async Task<ConnectionFolders> FoldersAsync(CancellationToken cancellationToken) =>
        await access.FoldersAsync(cancellationToken)
        ?? throw new RemoteStorageException(RemoteErrorKind.Refused, "You may no longer use this connection.");

    /// <summary>A file or folder whose content the person may read: <c>files.view</c> there.</summary>
    public async Task ReadableAsync(string path, CancellationToken cancellationToken)
    {
        var folders = await FoldersAsync(cancellationToken);
        if (!folders.Sees(path))
            throw NotFound(path);
        if (!folders.Allows(path, Rights.ViewFiles))
            throw Refused(path);
    }

    /// <summary>
    /// <paramref name="name"/> may be written into <paramref name="folder"/> with the job's right: the folder allows
    /// it, and the name is not one hidden from the person (then it counts as taken, whatever is there).
    /// </summary>
    public async Task WritableAsync(string folder, string name, CancellationToken cancellationToken)
    {
        var folders = await FoldersAsync(cancellationToken);
        Check(folders, folder, right);
        var path = RemotePaths.Combine(folder, name);
        if (!folders.At(path).IsVisible)
            throw Taken(name);
        if (!folders.Allows(path, right))
            throw Refused(path);
    }

    /// <summary>
    /// The item at <paramref name="path"/> may be taken away with <paramref name="changeRight"/> (the job's right
    /// for a move's source, <c>files.delete</c> to replace): at it, its folder and, for a folder, all below it.
    /// </summary>
    public async Task ChangeableAsync(string path, string? changeRight, CancellationToken cancellationToken)
    {
        var used = changeRight ?? right;
        var folders = await FoldersAsync(cancellationToken);
        Check(folders, path, used);
        if (!folders.Allows(RemotePaths.Parent(path), used) || !folders.AllowsThroughout(path, used))
            throw Refused(path);
    }

    /// <summary>A name the person may see in a folder of the connection (a free name is never a hidden one).</summary>
    public async Task<bool> IsVisibleAsync(string path, CancellationToken cancellationToken) =>
        (await FoldersAsync(cancellationToken)).At(path).IsVisible;

    private static void Check(ConnectionFolders folders, string path, string used)
    {
        if (folders.Allows(path, used))
            return;
        throw folders.Sees(path) ? Refused(path) : NotFound(path);
    }

    private static RemoteStorageException Taken(string name) =>
        new(RemoteErrorKind.Exists, $"There is already an item named “{name}” there.");

    private static RemoteStorageException NotFound(string path) =>
        new(RemoteErrorKind.NotFound, $"“{RemotePaths.NameOf(path)}” was not found in the connection.");

    private static RemoteStorageException Refused(string path) =>
        new(RemoteErrorKind.Refused, $"You may not do that with “{RemotePaths.NameOf(path)}” in the connection.");
}
